using System.Windows.Threading;

namespace FluentFlow.Services;

// Saves the settings shortly after the last change. A slider drag changes a value dozens of times a second;
// waiting for a quiet moment writes the file once instead of on every step.
public sealed class SettingsAutoSaver : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;

    public SettingsAutoSaver(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Flush(), dispatcher);
        _timer.Stop();
        _settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _timer.Stop();
        _timer.Start();
    }

    // Writes now if a change is waiting.
    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _settings.Save();
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= Settings_PropertyChanged;
        Flush();
    }
}
