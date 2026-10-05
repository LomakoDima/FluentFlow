using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// Content of the volume flyout. Its DataContext is the SystemVolumeService, so it shows real Windows state.
public partial class VolumeFlyout : UserControl
{
    private const double WheelStep = 0.02; // Windows changes the volume by 2% per wheel notch.
    private double _wheelRemainder;

    public VolumeFlyout() => InitializeComponent();

    private SystemVolumeService? Service => DataContext as SystemVolumeService;

    private void Mute_Click(object sender, RoutedEventArgs e) => Service?.ToggleMute();

    // Wheel over the flyout moves the volume. Trackpads send many small deltas, so they accumulate.
    private void MainView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Service is not { HasDevice: true } service) return;
        e.Handled = true;
        _wheelRemainder += e.Delta;
        var notches = (int)(_wheelRemainder / 120);
        if (notches == 0) return;
        _wheelRemainder -= notches * 120;
        // At 100% the value can't change, so the unmute that normally rides on a change is done explicitly.
        if (notches > 0 && service.IsMuted) service.ToggleMute();
        service.Volume += notches * WheelStep;
    }
}
