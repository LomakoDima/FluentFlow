using System.Windows;
using System.Windows.Input;
using FluentFlow.Services;

namespace FluentFlow.Controls;

public partial class TaskbarWidgetWindow : Window
{
    // The taskbar height the widget is designed for (a standard Windows 11 taskbar is 48 px at 100%).
    public const double WidgetHeight = 36;

    private readonly TaskbarWidgetView _view;

    public TaskbarWidgetWindow(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer)
    {
        InitializeComponent();
        _view = new TaskbarWidgetView(settings, media, visualizer);
        _view.AppearanceChanged += (_, _) => AppearanceChanged?.Invoke(this, EventArgs.Empty);
        Surface.Child = _view;
    }

    public event EventHandler? Clicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    // Raised when the settings or the playing track changed the widget's wished width.
    public event EventHandler? AppearanceChanged;

    public double MeasureDesiredWidth() => _view.MeasureDesiredWidth();

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
