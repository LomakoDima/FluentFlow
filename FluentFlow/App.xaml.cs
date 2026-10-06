using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private string _instanceName = @"Local\FluentFlow.SingleInstance";
        private string _activateName = @"Local\FluentFlow.Activate";

        // How long to wait for a taskbar widget before showing the player as a plain window instead.
        private static readonly TimeSpan ManualLaunchWait = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan AutostartWait = TimeSpan.FromSeconds(60); // Explorer may still be starting
        private static readonly TimeSpan LostWidgetWait = TimeSpan.FromSeconds(15); // e.g. Explorer restarting

        private Mutex? _instance;
        private EventWaitHandle? _activate;
        private AppSettings? _settings;
        private SettingsAutoSaver? _settingsSaver;
        private SettingsWindow? _settingsWindow;
        private SystemThemeService? _theme;
        private MediaSessionService? _media;
        private AudioVisualizerService? _visualizer;
        private SystemVolumeService? _volume;
        private TaskbarWidgetHost? _widget;
        private MainWindow? _window;
        private DispatcherTimer? _fallbackTimer;
        private bool _autostart;

        protected override void OnStartup(StartupEventArgs e)
        {
            // "--profile name": separate settings and a separate "already running" lock, for side-by-side copies and tests.
            var profile = ParseValue(e.Args, "--profile");
            if (!string.IsNullOrWhiteSpace(profile))
            {
                _instanceName += "." + profile;
                _activateName += "." + profile;
            }

            // A second launch (double click, or Windows plus the user) must not stack a second widget on the first.
            if (!AcquireSingleInstance())
            {
                Shutdown();
                return;
            }

            _settings = AppSettings.Load(AppSettings.PathForProfile(profile));
            _settingsSaver = new SettingsAutoSaver(_settings, Dispatcher);
            _autostart = e.Args.Contains(StartupRegistration.AutostartArgument, StringComparer.OrdinalIgnoreCase);
            // The flyout brushes must exist before the first window is created.
            _theme = new SystemThemeService(Dispatcher, ParseThemeOverride(e.Args));
            _theme.Apply();
            base.OnStartup(e);

            // The services outlive the window: the taskbar widget keeps running while the player window is hidden.
            _media = new MediaSessionService(Dispatcher);
            _visualizer = new AudioVisualizerService(Dispatcher);
            _volume = new SystemVolumeService(Dispatcher);
            _window = new MainWindow(_media, _volume);
            _window.SettingsRequested += (_, _) => ShowSettings();
            MainWindow = _window;
            // Plain mode closes for real; widget mode only ever closes through ExitApp. Either way the app ends.
            _window.Closed += (_, _) => Shutdown();
            StartServices();

            // "--no-widget": run as an ordinary window (for taskbars the widget doesn't suit, and for testing that path).
            if (e.Args.Contains("--no-widget", StringComparer.OrdinalIgnoreCase))
            {
                _window.UsePlainMode();
                _window.Show();
                return;
            }

            _widget = new TaskbarWidgetHost(_settings, _media, _visualizer, Dispatcher);
            _widget.SettingsRequested += (_, _) => ShowSettings();
            _widget.Clicked += (_, _) => _window.ToggleNear(_widget.ScreenBounds, _widget.Scale);
            _widget.ExitRequested += (_, _) => ExitApp();
            _widget.AttachedChanged += (_, _) => OnWidgetAttachedChanged();
            _fallbackTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = _autostart ? AutostartWait : ManualLaunchWait };
            _fallbackTimer.Tick += (_, _) => ShowAsPlainWindow();
            _fallbackTimer.Start();
            _widget.Start();
        }

        // The widget exists: the player becomes a flyout opened from it. If it goes away (Explorer restart, no room,
        // no taskbar) the player must stay reachable, so after a grace period it turns back into a normal window.
        private void OnWidgetAttachedChanged()
        {
            if (_widget is null || _window is null) return;
            if (_widget.IsAttached)
            {
                _fallbackTimer?.Stop();
                _window.UseWidgetMode();
            }
            else if (_fallbackTimer is { IsEnabled: false })
            {
                _fallbackTimer.Interval = LostWidgetWait;
                _fallbackTimer.Start();
            }
        }

        private void ShowAsPlainWindow()
        {
            _fallbackTimer?.Stop();
            if (_widget is null || _window is null || _widget.IsAttached) return;
            _window.UsePlainMode();
            if (!_window.IsVisible) _window.Show();
        }

        // Second launch: bring the first instance's player up instead of starting another copy.
        private void ShowFromAnotherLaunch()
        {
            if (_window is null) return;
            if (_widget is { IsAttached: true })
            {
                if (!_window.IsVisible) _window.ToggleNear(_widget.ScreenBounds, _widget.Scale);
                else _window.Activate();
                return;
            }

            // Plain window (no widget, or none right now): just make sure it is on screen.
            if (_widget is not null) _window.UsePlainMode();
            if (!_window.IsVisible) _window.Show();
            _window.Activate();
        }

        private bool AcquireSingleInstance()
        {
            try
            {
                _instance = new Mutex(true, _instanceName, out var created);
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting(_activateName).Set(); }
                    catch (Exception exception) { Debug.WriteLine($"Could not signal the running instance: {exception.Message}"); }
                    return false;
                }

                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, _activateName);
                var activate = _activate;
                var listener = new Thread(() =>
                {
                    try
                    {
                        while (activate.WaitOne())
                            Dispatcher.BeginInvoke(ShowFromAnotherLaunch);
                    }
                    catch (ObjectDisposedException) { } // the app is exiting
                })
                { IsBackground = true, Name = "FluentFlow activation listener" };
                listener.Start();
                return true;
            }
            catch (Exception exception)
            {
                // If the guard itself fails, running a possible second copy beats not running at all.
                Debug.WriteLine($"Single-instance guard unavailable: {exception.Message}");
                return true;
            }
        }

        // One settings window at a time: a second request brings the open one forward.
        private void ShowSettings()
        {
            if (_settings is null || _media is null || _visualizer is null) return;
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(_settings, _media, _visualizer);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
            }
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }

        private async void StartServices()
        {
            try
            {
                _volume!.Initialize();
                await Task.WhenAll(_media!.InitializeAsync(), _visualizer!.StartAsync());
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Service start failed: {exception}");
            }
        }

        private void ExitApp()
        {
            _window?.CloseForExit();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _fallbackTimer?.Stop();
            _settingsSaver?.Dispose();
            _widget?.Dispose();
            _media?.Dispose();
            _volume?.Dispose();
            // Not awaited on the UI thread: audio shutdown must not be able to hang the exit.
            _visualizer?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            _theme?.Dispose();
            _activate?.Dispose();
            _instance?.Dispose();
            base.OnExit(e);
        }

        private static string? ParseValue(string[] args, string name)
        {
            var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index < 0 || index + 1 >= args.Length ? null : args[index + 1];
        }

        // "--theme light" or "--theme dark" pins the flyout theme for testing; otherwise Windows decides.
        private static bool? ParseThemeOverride(string[] args)
        {
            return ParseValue(args, "--theme")?.ToLowerInvariant() switch
            {
                "light" => true,
                "dark" => false,
                _ => null
            };
        }
    }
}
