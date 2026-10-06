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

    // The preview taskbar is a schematic 860 DIP wide (a real one is much wider), scaled down to fit the window.
    private const double PreviewWidth = 860, PreviewHeight = 48, PreviewIcon = 44, PreviewGap = 4, PreviewPadding = 2;
    private const double TaskbarIconCount = 7;

    private readonly AppSettings _settings;
    private readonly TaskbarWidgetView _previewView;
    private readonly Canvas _iconLayer = new() { Width = PreviewWidth, Height = PreviewHeight, IsHitTestVisible = false };
    private readonly Dictionary<string, Button> _swatches = [];

    public SettingsWindow(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = settings;

        AlignmentList.ItemsSource = new[]
        {
            new Option(WidgetAlignment.Auto, "Auto"), new Option(WidgetAlignment.Left, "Left"),
            new Option(WidgetAlignment.Center, "Center"), new Option(WidgetAlignment.Right, "Right")
        };
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
        var background = new Border
        {
            Width = PreviewWidth, Height = PreviewHeight, CornerRadius = new CornerRadius(8), IsHitTestVisible = false
        };
        background.SetResourceReference(Border.BackgroundProperty, "TaskbarBackgroundBrush");
        PreviewCanvas.Children.Add(background);
        PreviewCanvas.Children.Add(_iconLayer);
        PreviewCanvas.Children.Add(_previewView);

        _settings.PropertyChanged += Settings_PropertyChanged;
        Closed += (_, _) => _settings.PropertyChanged -= Settings_PropertyChanged;
        Activated += (_, _) => StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        VersionText.Text = Version();
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        Sidebar.SelectedIndex = 0;
        SyncColorControls();
        UpdatePreview();
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
        foreach (var item in Sidebar.Items.OfType<SidebarItem>())
            item.Page.Visibility = ReferenceEquals(item, selected) ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected.Title;
        // The preview is about the widget; the General page has nothing to show there.
        PreviewCard.Visibility = ReferenceEquals(selected.Page, GeneralPage) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Window chrome

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
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
        CustomColorRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        CustomColorDivider.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
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
        var centered = WindowsTaskbarInfo.IconsCentered;
        var buttons = new List<Rect>();
        _iconLayer.Children.Clear();
        var firstX = centered ? (PreviewWidth - TaskbarIconCount * PreviewIcon) / 2 : 0;
        for (var i = 0; i < TaskbarIconCount; i++)
        {
            var slot = new Rect(firstX + i * PreviewIcon, 0, PreviewIcon, PreviewHeight);
            buttons.Add(slot);
            AddIcon(slot, start: i == 0);
        }
        var tray = new Rect(PreviewWidth - 110, 0, 110, PreviewHeight);
        AddTray(tray);

        var minimumWidth = 2 * _settings.WidgetPadding + _settings.VisualizerWidth;
        var wished = Math.Max(minimumWidth, _previewView.MeasureDesiredWidth());
        var spot = TaskbarDocking.Calculate(new Rect(0, 0, PreviewWidth, PreviewHeight), tray, buttons,
            new Size(wished, TaskbarWidgetWindow.WidgetHeight), minimumWidth, 1, PreviewGap, PreviewPadding,
            _settings.Alignment, centered, _settings.EdgeOffset);

        if (spot is { } rect)
        {
            _previewView.Visibility = Visibility.Visible;
            Canvas.SetLeft(_previewView, rect.X);
            Canvas.SetTop(_previewView, rect.Y);
            _previewView.Width = rect.Width;
            _previewView.Height = rect.Height;
            PreviewNote.Text = $"Windows places your taskbar icons {(centered ? "in the centre" : "on the left")}.";
        }
        else
        {
            _previewView.Visibility = Visibility.Collapsed;
            PreviewNote.Text = "There is no free space for the widget with these settings. Try another alignment or a narrower widget.";
        }
    }

    private void AddIcon(Rect slot, bool start)
    {
        var icon = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(start ? 4 : 6), Opacity = start ? 0.9 : 0.35 };
        icon.SetResourceReference(Border.BackgroundProperty, start ? "MacAccentBrush" : "TaskbarTextBrush");
        Canvas.SetLeft(icon, slot.X + (slot.Width - 22) / 2);
        Canvas.SetTop(icon, (PreviewHeight - 22) / 2);
        _iconLayer.Children.Add(icon);
    }

    private void AddTray(Rect tray)
    {
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Opacity = 0.35 };
            dot.SetResourceReference(Shape.FillProperty, "TaskbarTextBrush");
            Canvas.SetLeft(dot, tray.X + 12 + i * 18);
            Canvas.SetTop(dot, (PreviewHeight - 10) / 2);
            _iconLayer.Children.Add(dot);
        }
        var clock = new Border { Width = 28, Height = 14, CornerRadius = new CornerRadius(3), Opacity = 0.35 };
        clock.SetResourceReference(Border.BackgroundProperty, "TaskbarTextBrush");
        Canvas.SetLeft(clock, tray.Right - 40);
        Canvas.SetTop(clock, (PreviewHeight - 14) / 2);
        _iconLayer.Children.Add(clock);
    }
}
