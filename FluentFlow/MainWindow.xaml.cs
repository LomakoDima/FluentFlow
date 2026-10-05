using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow;

public partial class MainWindow : Window
{
    private const double GapFromWidget = 8; // DIPs
    private bool _widgetMode;
    private bool _exiting;

    public MainWindow(MediaSessionService media, SystemVolumeService volume)
    {
        InitializeComponent();
        DataContext = media;
        VolumePanel.DataContext = volume;
        VolumeFlyoutPopup.PlacementTarget = VolumeControl;
    }

    // With a taskbar widget the window is a flyout: no taskbar button, closing only hides it, and it opens at the widget.
    public void UseWidgetMode()
    {
        _widgetMode = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinimizeButton.Visibility = Visibility.Collapsed;
    }

    // No usable taskbar widget: an ordinary window with a taskbar button, where closing quits the app.
    public void UsePlainMode()
    {
        _widgetMode = false;
        ShowInTaskbar = true;
        MinimizeButton.Visibility = Visibility.Visible;
    }

    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    public void ToggleNear(Rect widgetPixels, double scale)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        PlaceNear(widgetPixels, scale);
        Show();
        Activate();
    }

    // Opens above or below the widget, wherever the taskbar leaves room, inside the monitor's work area.
    private void PlaceNear(Rect widgetPixels, double scale)
    {
        if (widgetPixels.IsEmpty) return;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var size = new Size(Width * scale, Height * scale);
        var placement = FlyoutPlacement.Calculate(widgetPixels, size, MonitorWorkArea.Get(widgetPixels),
            GapFromWidget * scale, GapFromWidget * scale);
        SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(placement.Location.X), (int)Math.Round(placement.Location.Y),
            0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_widgetMode && !_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void Volume_Click(object sender, RoutedEventArgs e) => VolumeFlyoutPopup.Toggle();

    private async void Previous_Click(object sender, RoutedEventArgs e)
        => await ((MediaSessionService)DataContext).PreviousAsync();

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
        => await ((MediaSessionService)DataContext).PlayPauseAsync();

    private async void Next_Click(object sender, RoutedEventArgs e)
        => await ((MediaSessionService)DataContext).NextAsync();

    // Buttons mark their own clicks as handled, so this only sees presses on the window's background.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
