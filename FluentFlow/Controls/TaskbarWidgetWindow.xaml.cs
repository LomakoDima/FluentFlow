using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentFlow.Services;

namespace FluentFlow.Controls;

public partial class TaskbarWidgetWindow : Window
{
    // The taskbar thickness the widget is designed for (a standard Windows 11 taskbar is 48 px at 100%): its height on a
    // taskbar at the top or bottom, its width on one at the left or right edge.
    public const double WidgetHeight = 36;

    private readonly TaskbarWidgetView _view;

    public TaskbarWidgetWindow(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer)
    {
        InitializeComponent();
        _view = new TaskbarWidgetView(settings, media, visualizer);
        _view.AppearanceChanged += (_, _) => AppearanceChanged?.Invoke(this, EventArgs.Empty);

        // The hover plate sits behind the view and fades in and out; the widget itself fades in whenever it appears.
        var plate = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)), CornerRadius = new CornerRadius(6),
            Opacity = 0, IsHitTestVisible = false
        };
        Surface.Child = new Grid { Children = { plate, _view } };
        Surface.MouseEnter += (_, _) => Motion.FadeTo(plate, 1);
        Surface.MouseLeave += (_, _) => Motion.FadeTo(plate, 0, Motion.Normal);
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) Motion.Enter(Surface, scale: 0.94, milliseconds: Motion.Slow);
        };
    }

    public event EventHandler? Clicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    // Raised when the settings or the playing track changed the widget's wished width.
    public event EventHandler? AppearanceChanged;

    public void SetOrientation(Orientation orientation, bool growFromEnd) => _view.SetOrientation(orientation, growFromEnd);

    public double MeasureDesiredLength() => _view.MeasureDesiredLength();

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
    private void Open_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // The registry is the source of truth: Task Manager can switch the entry off behind our back.
    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => StartupItem.IsChecked = StartupRegistration.IsEnabled;

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        // IsCheckable has already flipped the check; undo it when Windows refuses the change.
        if (!StartupRegistration.SetEnabled(StartupItem.IsChecked)) StartupItem.IsChecked = !StartupItem.IsChecked;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
