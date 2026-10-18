using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow;

// A settings window in the style of macOS System Settings. Everything binds to AppSettings, which the taskbar widget
// also listens to, so every change shows up on the taskbar immediately; the preview at the top uses the same view
// and the same placement maths as the real widget.
public partial class SettingsWindow : Window
{
    public sealed record Option(object Value, string Label);
    public sealed record SidebarItem(string Title, string Glyph, Brush Tile, Panel Page);

    private static readonly string[] SwatchColors =
        ["#D7AC99", "#0A84FF", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158", "#64D2FF", "#FFFFFF"];

    // The preview taskbar is a schematic 860 DIP long (a real one is much longer), scaled down to fit the window. On a
    // taskbar at the side of the screen it is a shorter strip standing upright.
    private const double PreviewLength = 860, PreviewThickness = 48, PreviewIcon = 44, PreviewGap = 4, PreviewPadding = 2;
    private const double HorizontalTrayLength = 110, VerticalPreviewLength = 330, VerticalTrayLength = 56;
    private const double TaskbarIconCount = 7, VerticalIconCount = 3;
    private const double VerticalPreviewMaxHeight = 210;

    private readonly AppSettings _settings;
    private readonly TaskbarWidgetView _previewView;
    private readonly Canvas _iconLayer = new() { Width = PreviewLength, Height = PreviewThickness, IsHitTestVisible = false };
    private readonly Border _previewBackground = new() { Width = PreviewLength, Height = PreviewThickness, CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
    private readonly Dictionary<string, Button> _swatches = [];
    private bool? _vertical; // the taskbar edge the texts and the preview currently describe
    private Panel? _page;    // the page on show, so a change of page can be told from the first one
    private bool _closing;

    public SettingsWindow(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = settings;

        ColorModeList.ItemsSource = new[]
        {
            new Option(VisualizerColorMode.Accent, "FluentFlow"), new Option(VisualizerColorMode.Windows, "Windows"),
            new Option(VisualizerColorMode.Custom, "Custom")
        };
        BuildSwatches();

        Sidebar.ItemsSource = new[]
        {
            new SidebarItem("General", "", Tile("#8E8E93"), GeneralPage),
            new SidebarItem("Position", "", Tile("#0A84FF"), PositionPage),
            new SidebarItem("Appearance", "", Tile("#BF5AF2"), AppearancePage),
            new SidebarItem("Track title", "", Tile("#FF9F0A"), TitlePage)
        };

        // The preview: a mock taskbar with the real widget view on it.
        _previewView = new TaskbarWidgetView(settings, media, visualizer, preview: true) { Height = TaskbarWidgetWindow.WidgetHeight };
        _previewView.AppearanceChanged += (_, _) => UpdatePreview();
        _previewBackground.SetResourceReference(Border.BackgroundProperty, "TaskbarBackgroundBrush");
        PreviewCanvas.Children.Add(_previewBackground);
        PreviewCanvas.Children.Add(_iconLayer);
        PreviewCanvas.Children.Add(_previewView);

        _settings.PropertyChanged += Settings_PropertyChanged;
        Closed += (_, _) => _settings.PropertyChanged -= Settings_PropertyChanged;
        Activated += (_, _) =>
        {
            StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
            if (SyncTaskbarEdge()) UpdatePreview();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        // The window opens with a fade and a small grow, and closes the other way round (see OnClosing).
        if (Motion.Enabled) Frame.Opacity = 0;
        Loaded += (_, _) => Motion.Enter(Frame, scale: 0.96, milliseconds: Motion.Slow);

        VersionText.Text = Version();
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        Sidebar.SelectedIndex = 0;
        SyncColorControls();
        SyncTaskbarEdge();
        UpdatePreview();
    }

    // Everything that depends on which edge the taskbar is docked to: on a taskbar at the side of the screen the
    // positions are top, centre and bottom, the widget has no title and the preview stands upright. Returns true when
    // that changed.
    private bool SyncTaskbarEdge() => ApplyTaskbarEdge(WindowsTaskbarInfo.IsVertical);

    private bool ApplyTaskbarEdge(bool vertical)
    {
        if (_vertical == vertical) return false;
        _vertical = vertical;

        var chosen = _settings.Alignment;
        AlignmentList.ItemsSource = new[]
        {
            new Option(WidgetAlignment.Auto, "Auto"), new Option(WidgetAlignment.Left, vertical ? "Top" : "Left"),
            new Option(WidgetAlignment.Center, "Center"), new Option(WidgetAlignment.Right, vertical ? "Bottom" : "Right")
        };
        AlignmentList.SelectedValue = chosen;

        AlignmentRow.Description = vertical
            ? "Auto follows your Windows taskbar icon alignment: with centred icons the widget goes to the top edge, otherwise it sits by the notification area."
            : "Auto follows your Windows taskbar icon alignment: with centred icons the widget goes to the left edge, with left-aligned icons it sits by the notification area.";
        WidthRow.Description = vertical ? "Length of the visualizer along the taskbar." : string.Empty;
        HeightRow.Description = vertical ? "Depth of the visualizer across the taskbar." : string.Empty;
        ShowTitleRow.Description = vertical
            ? "Display what is playing to the left of the visualizer. A taskbar at the side of the screen has no room for it."
            : "Display what is playing to the left of the visualizer.";
        _previewView.SetOrientation(vertical ? Orientation.Vertical : Orientation.Horizontal, growFromEnd: false);
        PreviewViewbox.MaxHeight = vertical ? VerticalPreviewMaxHeight : double.PositiveInfinity;
        return true;
    }

    private static Brush Tile(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static string Version()
    {
        var version = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return $"Version {version?.Split('+')[0] ?? "1.0.0"}";
    }

    // Navigation

    private void Sidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Sidebar.SelectedItem is not SidebarItem selected) return;
        var previewWasShown = PreviewCard.Visibility == Visibility.Visible;
        foreach (var item in Sidebar.Items.OfType<SidebarItem>())
            item.Page.Visibility = ReferenceEquals(item, selected) ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected.Title;
        // The preview is about the widget; the General page has nothing to show there.
        var showPreview = !ReferenceEquals(selected.Page, GeneralPage);
        PreviewCard.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;

        // A new page eases in from below and the title follows from the side; the first page is just there.
        var changed = _page is not null && !ReferenceEquals(_page, selected.Page);
        _page = selected.Page;
        if (!changed || !IsLoaded) return;
        Motion.Enter(selected.Page, dy: 10);
        Motion.Enter(PageTitle, dx: 8);
        if (showPreview && !previewWasShown) Motion.Enter(PreviewCard, dy: -8);
    }

    // Window chrome

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Closing plays the opening backwards (a fade and a small shrink), then really closes. It is skipped when the app
    // is exiting, when the window is minimised and when animations are off.
    protected override void OnClosing(CancelEventArgs e)
    {
        var exiting = Application.Current is App { IsExiting: true };
        if (!_closing && !exiting && IsVisible && WindowState != WindowState.Minimized && Motion.Enabled)
        {
            _closing = true;
            e.Cancel = true;
            Motion.Leave(Frame, Close, scale: 0.97, milliseconds: Motion.Quick);
            return;
        }
        base.OnClosing(e);
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    // General

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        // The switch has already flipped; undo it when Windows refuses the change.
        if (!StartupRegistration.SetEnabled(StartupSwitch.IsChecked == true))
            StartupSwitch.IsChecked = !StartupSwitch.IsChecked;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _settings.ResetToDefaults();

    // Colour

    private void BuildSwatches()
    {
        foreach (var hex in SwatchColors)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("MacSwatchStyle"), Background = Tile(hex),
                ToolTip = hex, DataContext = hex
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Colour {hex}");
            swatch.Click += (_, _) =>
            {
                _settings.CustomColor = hex;
                _settings.ColorMode = VisualizerColorMode.Custom;
            };
            _swatches[hex] = swatch;
            Swatches.Children.Add(swatch);
        }
    }

    private void SyncColorControls()
    {
        var custom = _settings.ColorMode == VisualizerColorMode.Custom;
        Motion.SetRevealed(CustomColorPanel, custom, animate: IsLoaded);
        foreach (var (hex, button) in _swatches)
            button.Tag = string.Equals(hex, _settings.CustomColor, StringComparison.OrdinalIgnoreCase) ? "selected" : null;
        if (!HexBox.IsKeyboardFocused) HexBox.Text = _settings.CustomColor;
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitHex();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    // A typed colour is accepted when it is a valid hex colour; otherwise the box goes back to the current one.
    private void CommitHex()
    {
        var text = HexBox.Text.Trim();
        if (text.Length > 0 && !text.StartsWith('#')) text = "#" + text;
        var before = _settings.CustomColor;
        _settings.CustomColor = text;
        if (_settings.CustomColor != before) _settings.ColorMode = VisualizerColorMode.Custom;
        HexBox.Text = _settings.CustomColor;
    }

    // Live updates

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SyncColorControls();
        UpdatePreview();
    }

    // Places the real widget view on a mock taskbar using the same maths as the real widget host.
    private void UpdatePreview()
    {
        var vertical = _vertical == true;
        var centered = WindowsTaskbarInfo.IconsCentered;
        var length = vertical ? VerticalPreviewLength : PreviewLength;
        var taskbar = vertical ? new Rect(0, 0, PreviewThickness, length) : new Rect(0, 0, length, PreviewThickness);
        ResizePreview(taskbar.Size);

        var buttons = new List<Rect>();
        _iconLayer.Children.Clear();
        var count = vertical ? VerticalIconCount : TaskbarIconCount;
        // A taskbar at the side of the screen starts its icons at the top; a horizontal one may centre them.
        var firstOffset = centered && !vertical ? (length - count * PreviewIcon) / 2 : 0;
        for (var i = 0; i < count; i++)
        {
            var slot = vertical
                ? new Rect(0, i * PreviewIcon, PreviewThickness, PreviewIcon)
                : new Rect(firstOffset + i * PreviewIcon, 0, PreviewIcon, PreviewThickness);
            buttons.Add(slot);
            AddIcon(slot, start: i == 0);
        }
        var tray = vertical
            ? new Rect(0, length - VerticalTrayLength, PreviewThickness, VerticalTrayLength)
            : new Rect(length - HorizontalTrayLength, 0, HorizontalTrayLength, PreviewThickness);
        AddTray(tray, vertical);

        var minimumLength = 2 * _settings.WidgetPadding + _settings.VisualizerWidth;
        var wished = Math.Max(minimumLength, _previewView.MeasureDesiredLength());
        var spot = TaskbarDocking.Calculate(taskbar, tray, buttons,
            new Size(wished, TaskbarWidgetWindow.WidgetHeight), minimumLength, 1, PreviewGap, PreviewPadding,
            _settings.Alignment, centered, _settings.EdgeOffset);

        if (spot is { } rect)
        {
            _previewView.Visibility = Visibility.Visible;
            Canvas.SetLeft(_previewView, rect.X);
            Canvas.SetTop(_previewView, rect.Y);
            _previewView.Width = rect.Width;
            _previewView.Height = rect.Height;
            PreviewNote.Text = vertical
                ? "Your taskbar is at the side of the screen, so the widget shows just the visualizer, turned to run along it."
                : $"Windows places your taskbar icons {(centered ? "in the centre" : "on the left")}.";
        }
        else
        {
            _previewView.Visibility = Visibility.Collapsed;
            PreviewNote.Text = "There is no free space for the widget with these settings. Try another alignment or a narrower widget.";
        }
    }

    private void ResizePreview(Size size)
    {
        PreviewCanvas.Width = _iconLayer.Width = _previewBackground.Width = size.Width;
        PreviewCanvas.Height = _iconLayer.Height = _previewBackground.Height = size.Height;
    }

    private void AddIcon(Rect slot, bool start)
    {
        var icon = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(start ? 4 : 6), Opacity = start ? 0.9 : 0.35 };
        icon.SetResourceReference(Border.BackgroundProperty, start ? "MacAccentBrush" : "TaskbarTextBrush");
        Canvas.SetLeft(icon, slot.X + (slot.Width - 22) / 2);
        Canvas.SetTop(icon, slot.Y + (slot.Height - 22) / 2);
        _iconLayer.Children.Add(icon);
    }

    // Three dots and a clock: along the strip on a horizontal taskbar, stacked on a vertical one.
    private void AddTray(Rect tray, bool vertical)
    {
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Opacity = 0.35 };
            dot.SetResourceReference(Shape.FillProperty, "TaskbarTextBrush");
            Canvas.SetLeft(dot, vertical ? tray.X + (tray.Width - 10) / 2 : tray.X + 12 + i * 18);
            Canvas.SetTop(dot, vertical ? tray.Y + 8 + i * 14 : tray.Y + (tray.Height - 10) / 2);
            _iconLayer.Children.Add(dot);
        }
        var clock = new Border { Width = 28, Height = 14, CornerRadius = new CornerRadius(3), Opacity = 0.35 };
        clock.SetResourceReference(Border.BackgroundProperty, "TaskbarTextBrush");
        Canvas.SetLeft(clock, vertical ? tray.X + (tray.Width - 28) / 2 : tray.Right - 40);
        Canvas.SetTop(clock, vertical ? tray.Bottom - 22 : tray.Y + (tray.Height - 14) / 2);
        _iconLayer.Children.Add(clock);
    }
}
