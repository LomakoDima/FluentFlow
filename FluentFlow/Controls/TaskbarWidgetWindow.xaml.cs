using System.Windows;
using System.Windows.Input;
using FluentFlow.Services;

namespace FluentFlow.Controls;

public partial class TaskbarWidgetWindow : Window
{
    public const double WidgetWidth = 128;
    public const double WidgetHeight = 36;

    public TaskbarWidgetWindow(AudioVisualizerService visualizer)
    {
        InitializeComponent();
        Visualizer.Service = visualizer;
    }

    public event EventHandler? Clicked;
    public event EventHandler? ExitRequested;

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
    private void Open_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
    // The registry is the source of truth: Task Manager can switch the entry off behind our back.
    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => StartupItem.IsChecked = StartupRegistration.IsEnabled;

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        // IsCheckable has already flipped the check; undo it when Windows refuses the change.
        if (!StartupRegistration.SetEnabled(StartupItem.IsChecked)) StartupItem.IsChecked = !StartupItem.IsChecked;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
