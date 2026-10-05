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
        private const string InstanceName = @"Local\FluentFlow.SingleInstance";
        private const string ActivateName = @"Local\FluentFlow.Activate";

        // How long to wait for a taskbar widget before showing the player as a plain window instead.
        private static readonly TimeSpan ManualLaunchWait = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan AutostartWait = TimeSpan.FromSeconds(60); // Explorer may still be starting
        private static readonly TimeSpan LostWidgetWait = TimeSpan.FromSeconds(15); // e.g. Explorer restarting

        private Mutex? _instance;
        private EventWaitHandle? _activate;
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
            // A second launch (double click, or Windows plus the user) must not stack a second widget on the first.
            if (!AcquireSingleInstance())
            {
                Shutdown();
                return;
            }

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

            _widget = new TaskbarWidgetHost(_visualizer, Dispatcher);
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
                _instance = new Mutex(true, InstanceName, out var created);
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting(ActivateName).Set(); }
                    catch (Exception exception) { Debug.WriteLine($"Could not signal the running instance: {exception.Message}"); }
                    return false;
                }

                _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
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

        // "--theme light" or "--theme dark" pins the flyout theme for testing; otherwise Windows decides.
        private static bool? ParseThemeOverride(string[] args)
        {
            var index = Array.FindIndex(args, arg => arg.Equals("--theme", StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= args.Length) return null;
            return args[index + 1].ToLowerInvariant() switch
            {
                "light" => true,
                "dark" => false,
                _ => null
            };
        }
    }
}
