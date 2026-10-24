using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow;

// The settings window. Everything binds to AppSettings, which the taskbar widget also listens to, so every change shows
// up on the taskbar immediately. The stage at the top of the widget pages shows the same view on a model of the
// taskbar, placed with the same maths as the real widget: close up (the camera follows the widget around) and whole
// (with a frame on the part in view).
public partial class SettingsWindow : Window
{
    public sealed record Option(object Value, string Label, Brush? Swatch = null);
    public sealed record NavItem(string Title, string Glyph, string Description, Panel Page, bool ShowsStage);

    private static readonly string[] SwatchColors =
        ["#D7AC99", "#0A84FF", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158", "#64D2FF", "#FFFFFF"];

    // The model taskbar is a schematic 860 DIP long (a real one is much longer). On a taskbar at the side of the screen
    // it is a shorter strip standing upright.
    private const double PreviewLength = 860, PreviewThickness = 48, PreviewIcon = 44, PreviewGap = 4, PreviewPadding = 2;
    private const double HorizontalTrayLength = 110, VerticalPreviewLength = 330, VerticalTrayLength = 56;
    private const double TaskbarIconCount = 7, VerticalIconCount = 3;
    // The close-up: at most this much magnification, and the widget takes up at most this share of the stage.
    private const double StageZoom = 1.6, StageFill = 0.7;
    private const double OverviewLength = 260, VerticalOverviewLength = 104;

    private readonly AppSettings _settings;
    private readonly SystemThemeService _theme;
    private readonly TaskbarWidgetView _previewView;
    private readonly Canvas _iconLayer = new() { Width = PreviewLength, Height = PreviewThickness, IsHitTestVisible = false };
    private readonly Border _previewBackground = new() { Width = PreviewLength, Height = PreviewThickness, IsHitTestVisible = false };
    private readonly VisualBrush _overviewBrush;
    private readonly ScaleTransform _zoom = new();
    private readonly TranslateTransform _pan = new();
    private readonly TranslateTransform _viewfinderOffset = new();
    private readonly TranslateTransform _indicatorOffset = new();
    private readonly SolidColorBrush _customSwatch = new();
    private readonly SolidColorBrush _windowsSwatch = new();
    private readonly Dictionary<string, Button> _swatches = [];
    private readonly DispatcherTimer _resetDoneTimer;
    private Rect _widgetRect;
    private bool? _vertical; // the taskbar edge the texts and the preview currently describe
    private Panel? _page;    // the page on show, so a change of page can be told from the first one

    public SettingsWindow(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer,
        SystemThemeService theme)
    {
        InitializeComponent();
        _settings = settings;
        _theme = theme;
        DataContext = settings;

        ColorModeList.ItemsSource = new[]
        {
            new Option(VisualizerColorMode.Accent, "FluentFlow", (Brush)FindResource("AccentBrush")),
            new Option(VisualizerColorMode.Windows, "Windows", _windowsSwatch),
            new Option(VisualizerColorMode.Custom, "Custom", _customSwatch)
        };
        BuildSwatches();

        Nav.ItemsSource = new[]
        {
            new NavItem("General", "", "Startup, about FluentFlow, and starting over.", GeneralPage, false),
            new NavItem("Placement", "", "Where the widget sits on your taskbar.", PlacementPage, true),
            new NavItem("Visualizer", "", "The colour and shape of the bars.", VisualizerPage, true),
            new NavItem("Track title", "", "What the widget says about the music.", TitlePage, true)
        };

        // The stage: a model taskbar with the real widget view on it, filmed close up and shown whole.
        _previewView = new TaskbarWidgetView(settings, media, visualizer, preview: true) { Height = TaskbarWidgetWindow.WidgetHeight };
        _previewView.AppearanceChanged += (_, _) => UpdatePreview();
        _previewBackground.SetResourceReference(Border.BackgroundProperty, "TaskbarBackgroundBrush");
        _previewBackground.SetResourceReference(Border.BorderBrushProperty, "TaskbarEdgeBrush");
        PreviewCanvas.Children.Add(_previewBackground);
        PreviewCanvas.Children.Add(_iconLayer);
        PreviewCanvas.Children.Add(_previewView);
        Camera.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };
        _overviewBrush = new VisualBrush(PreviewCanvas) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
        Overview.Fill = _overviewBrush;
        Viewfinder.RenderTransform = _viewfinderOffset;
        NavIndicator.RenderTransform = _indicatorOffset;
        StageViewport.SizeChanged += (_, _) =>
        {
            StageViewport.Clip = new RectangleGeometry(new Rect(StageViewport.RenderSize), 9, 9);
            UpdateCamera(animate: false);
        };

        _resetDoneTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _resetDoneTimer.Tick += (_, _) =>
        {
            _resetDoneTimer.Stop();
            Motion.Leave(ResetDone, () => ResetDone.Visibility = Visibility.Collapsed, milliseconds: Motion.Normal);
        };

        _settings.PropertyChanged += Settings_PropertyChanged;
        _theme.Applied += Theme_Applied;
        Closed += (_, _) =>
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _theme.Applied -= Theme_Applied;
            _resetDoneTimer.Stop();
        };
        Activated += (_, _) =>
        {
            StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
            if (SyncTaskbarEdge()) UpdatePreview();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) =>
        {
            WindowBackdrop.HideSystemCaptionButtons(this);
            // Mica shows through wherever the window draws nothing; without it the solid background stays.
            if (WindowBackdrop.TryApplyMica(this, dark: !_theme.IsLight)) Background = Brushes.Transparent;
        };
        StateChanged += (_, _) => SyncWindowState();

        // Windows animates the window in; the content follows a beat behind.
        Loaded += (_, _) =>
        {
            MoveNavIndicator(animate: false);
            UpdateCamera(animate: false);
            Motion.Enter(Header, dy: 8, milliseconds: Motion.Slow);
            Motion.Enter(StageBlock, dy: 10, milliseconds: Motion.Slow, delay: 40);
            if (_page is not null) Motion.Enter(_page, dy: 12, milliseconds: Motion.Slow, delay: 80);
        };

        VersionText.Text = Version();
        AboutVersionText.Text = $"{Version()}  ·  MIT License";
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        SyncWindowsSwatch();
        Nav.SelectedIndex = 0;
        SyncColorControls();
        SyncTaskbarEdge();
        UpdatePreview();
    }

    // Everything that depends on which edge the taskbar is docked to: on a taskbar at the side of the screen the
    // positions are top, centre and bottom, the widget has no title and the model taskbar stands upright. Returns true
    // when that changed.
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
        WidthRow.Description = vertical ? "Length of the visualizer along the taskbar." : "How wide the visualizer is.";
        HeightRow.Description = vertical ? "Depth of the visualizer across the taskbar." : "How tall the bars can grow.";
        ShowTitleRow.Description = vertical
            ? "Display what is playing beside the visualizer. A taskbar at the side of the screen has no room for it."
            : "Display what is playing to the left of the visualizer.";
        _previewView.SetOrientation(vertical ? Orientation.Vertical : Orientation.Horizontal, growFromEnd: false);
        return true;
    }

    private static Brush Tile(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static string Version()
    {
        var version = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return $"Version {version?.Split('+')[0] ?? "1.0.0"}";
    }

    // Navigation

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem selected) return;
        foreach (var item in Nav.Items.OfType<NavItem>())
            item.Page.Visibility = ReferenceEquals(item, selected) ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected.Title;
        PageDescription.Text = selected.Description;
        // The stage is about the widget; the General page has nothing to show there.
        Motion.SetRevealed(StageBlock, selected.ShowsStage, animate: IsLoaded);
        MoveNavIndicator(animate: IsLoaded);

        // A new page rises into place under a header that fades across; the first page is just there.
        var changed = _page is not null && !ReferenceEquals(_page, selected.Page);
        _page = selected.Page;
        if (!changed || !IsLoaded) return;
        PageScroller.ScrollToTop();
        Motion.Enter(Header, dx: 6, milliseconds: Motion.Normal);
        Motion.Enter(selected.Page, dy: 12, milliseconds: Motion.Slow, delay: 30);
    }

    // The indicator glides to the active entry. Containers only exist after layout, so before that it waits.
    private void MoveNavIndicator(bool animate)
    {
        if (Nav.ItemContainerGenerator.ContainerFromItem(Nav.SelectedItem) is not FrameworkElement entry || entry.ActualHeight <= 0)
        {
            if (!IsLoaded) return;
            Dispatcher.BeginInvoke(() => MoveNavIndicator(animate: false), DispatcherPriority.Loaded);
            return;
        }
        var y = entry.TranslatePoint(new Point(0, 0), NavIndicatorLayer).Y + (entry.ActualHeight - NavIndicator.Height) / 2;
        var first = NavIndicator.Visibility != Visibility.Visible;
        NavIndicator.Visibility = Visibility.Visible;
        Motion.Glide(_indicatorOffset, TranslateTransform.YProperty, y, animate && !first, Motion.Slow);
    }

    // Window chrome

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // A maximised window with an app-drawn title bar reaches past the screen by its resize border; the content is kept
    // inside the screen, and the maximise button turns into restore.
    private void SyncWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? WindowBackdrop.MaximizedInset(this) : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void Theme_Applied(object? sender, EventArgs e)
    {
        WindowBackdrop.SetDarkMode(this, dark: !_theme.IsLight);
        SyncWindowsSwatch();
    }

    private void SyncWindowsSwatch()
    {
        if (TryFindResource("WindowsAccentBrush") is SolidColorBrush accent) _windowsSwatch.Color = accent.Color;
    }

    // General

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        // The switch has already flipped; undo it when Windows refuses the change.
        if (!StartupRegistration.SetEnabled(StartupSwitch.IsChecked == true))
            StartupSwitch.IsChecked = !StartupSwitch.IsChecked;
    }

    // Resetting asks first, inline, with the safe choice focused; afterwards a short note says it happened.
    private void ResetAsk_Click(object sender, RoutedEventArgs e)
    {
        Motion.SetRevealed(ResetConfirm, true, animate: true);
        ResetCancelButton.Focus();
    }

    private void ResetCancel_Click(object sender, RoutedEventArgs e)
    {
        Motion.SetRevealed(ResetConfirm, false, animate: true);
        ResetButton.Focus();
    }

    private void ResetConfirm_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        Motion.SetRevealed(ResetConfirm, false, animate: true);
        ResetButton.Focus();
        ResetDone.Visibility = Visibility.Visible;
        Motion.Enter(ResetDone, dx: 8);
        _resetDoneTimer.Stop();
        _resetDoneTimer.Start();
    }

    // Colour

    private void BuildSwatches()
    {
        foreach (var hex in SwatchColors)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SettingsSwatchStyle"), Background = Tile(hex),
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
        _customSwatch.Color = (Color)ColorConverter.ConvertFromString(_settings.CustomColor);
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

    // Places the real widget view on the model taskbar using the same maths as the real widget host.
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

        // Measured free of its last placement, or it could only ever ask for the size it already has.
        _previewView.Width = _previewView.Height = double.NaN;
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
            _widgetRect = rect;
            PreviewNote.Text = vertical
                ? "Your taskbar is at the side of the screen, so the widget shows just the visualizer, turned to run along it."
                : $"Windows places your taskbar icons {(centered ? "in the centre" : "on the left")}.";
        }
        else
        {
            _previewView.Visibility = Visibility.Collapsed;
            _widgetRect = taskbar;
            PreviewNote.Text = "There is no free space for the widget with these settings. Try another alignment or a narrower widget.";
        }
        UpdateCamera(animate: IsLoaded);
    }

    // Frames the widget: as close as StageZoom allows while it fills at most StageFill of the stage, the taskbar resting
    // on the stage's bottom edge (or standing in its middle when it is upright), and never past the taskbar's ends.
    // Moving the widget pans the camera after it.
    private void UpdateCamera(bool animate)
    {
        var width = StageViewport.ActualWidth;
        var height = StageViewport.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var vertical = _vertical == true;
        var model = new Size(PreviewCanvas.Width, PreviewCanvas.Height);
        var focus = _widgetRect.IsEmpty ? new Rect(model) : _widgetRect;
        double scale, x, y;
        if (!vertical)
        {
            scale = Math.Clamp(width * StageFill / Math.Max(1, focus.Width), 0.5, StageZoom);
            y = height - model.Height * scale;
            x = StageFraming.Follow(width, model.Width * scale, width / 2 - (focus.X + focus.Width / 2) * scale);
        }
        else
        {
            scale = Math.Clamp((height - 24) * StageFill / Math.Max(1, focus.Height), 0.4, StageZoom);
            x = (width - model.Width * scale) / 2;
            y = StageFraming.Follow(height, model.Height * scale, height / 2 - (focus.Y + focus.Height / 2) * scale);
        }

        Motion.Glide(_zoom, ScaleTransform.ScaleXProperty, scale, animate);
        Motion.Glide(_zoom, ScaleTransform.ScaleYProperty, scale, animate);
        Motion.Glide(_pan, TranslateTransform.XProperty, x, animate);
        Motion.Glide(_pan, TranslateTransform.YProperty, y, animate);
        UpdateOverview(new Rect(-x / scale, -y / scale, width / scale, height / scale), model, animate);
    }


    // The whole model taskbar in miniature, with a frame on the part the close-up shows.
    private void UpdateOverview(Rect inView, Size model, bool animate)
    {
        var vertical = _vertical == true;
        var factor = vertical ? VerticalOverviewLength / model.Height : OverviewLength / model.Width;
        Overview.Width = model.Width * factor;
        Overview.Height = model.Height * factor;
        _overviewBrush.Viewbox = new Rect(model);

        inView.Intersect(new Rect(model));
        if (inView.IsEmpty) return;
        Motion.Glide(_viewfinderOffset, TranslateTransform.XProperty, inView.X * factor, animate);
        Motion.Glide(_viewfinderOffset, TranslateTransform.YProperty, inView.Y * factor, animate);
        Motion.Glide(Viewfinder, WidthProperty, inView.Width * factor + 4, animate);
        Motion.Glide(Viewfinder, HeightProperty, inView.Height * factor + 4, animate);
    }

    private void ResizePreview(Size size)
    {
        // The hairline a real taskbar has along the edge it shares with the desktop.
        _previewBackground.BorderThickness = _vertical == true ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0);
        PreviewCanvas.Width = _iconLayer.Width = _previewBackground.Width = size.Width;
        PreviewCanvas.Height = _iconLayer.Height = _previewBackground.Height = size.Height;
    }

    private void AddIcon(Rect slot, bool start)
    {
        var icon = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(start ? 4 : 6), Opacity = start ? 0.9 : 0.35 };
        icon.SetResourceReference(Border.BackgroundProperty, start ? "SettingsAccentBrush" : "TaskbarTextBrush");
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
