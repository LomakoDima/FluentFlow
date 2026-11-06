using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// Keeps a TaskbarWidgetWindow attached to the Windows taskbar, docked next to the notification area.
// The widget is a child window of Shell_TrayWnd, which is how Windows 11 taskbar widgets are done: the taskbar's
// own content is drawn by a composition child window that covers it, so our window has to be a sibling above it.
// Because a child dies with its parent, an Explorer restart means building a fresh window, not reusing the old one.
public sealed class TaskbarWidgetHost : IDisposable
{
    private const double GapFromNeighbours = 4; // DIPs
    private const double CrossPadding = 2; // DIPs between the widget and the taskbar's long edges
    private const int ScanEveryTicks = 2; // the safety net; WinEvents (TaskbarChangeWatcher) do the quick reacting
    // A new taskbar button appears a moment after its window and then animates in, so one change is followed by a few
    // scans, quick at first (ms after the change).
    private static readonly int[] BurstDelays = [50, 150, 320, 650, 1100];
    // How long the widget takes to slide or resize to a new spot. Decelerate covers most of the way at the start, so
    // it gets out of a new button's way at once and only settles softly.
    private const int GlideMilliseconds = Motion.Slow;
    private readonly AppSettings _settings;
    private readonly MediaSessionService _media;
    private readonly AudioVisualizerService _visualizer;
    private readonly Dispatcher _dispatcher;
    private readonly TaskbarButtonScanner _scanner = new();
    private readonly TaskbarChangeWatcher _watcher = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _burstTimer;
    private TaskbarWidgetWindow? _window;
    private IntPtr _tray;
    private IntPtr _hwnd;
    private int _burstStep;
    private bool _layoutQueued;

    // Where the widget is going (_target) and where it is right now (_current), in the taskbar's client pixels.
    // They differ only while it glides from _glideFrom to _target.
    private Bounds _target;
    private Bounds _current;
    private Bounds _glideFrom;
    private readonly Stopwatch _glideClock = new();
    private bool _gliding;
    private bool _shown;
    private bool _attached;
    private int _ticks;
    private bool _disposed;

    public TaskbarWidgetHost(AppSettings settings, MediaSessionService media, AudioVisualizerService visualizer, Dispatcher dispatcher)
    {
        _settings = settings;
        _media = media;
        _visualizer = visualizer;
        _dispatcher = dispatcher;
        // A setting changed (alignment, size, ...): re-place the widget now instead of at the next poll.
        _settings.PropertyChanged += (_, _) => RequestLayout();
        // Polling is what keeps this robust: taskbar size, tray width, DPI and Explorer restarts have no common event.
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Tick(), dispatcher);
        _timer.Stop();
        _burstTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _burstTimer.Tick += (_, _) => BurstTick();
        // A scan that found the buttons moved re-places the widget right away instead of at the next poll.
        _scanner.Changed += () => _dispatcher.BeginInvoke(DispatcherPriority.Normal, RequestLayout);
        _watcher.ButtonsMayHaveChanged += (_, _) => StartBurst();
        _watcher.TaskbarMoved += (_, _) =>
        {
            RequestLayout();
            StartBurst();
        };
    }

    public event EventHandler? Clicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? AttachedChanged;

    // True while the widget is on the taskbar and visible.
    public bool IsAttached => _attached;

    // Physical pixels per DIP of the monitor that holds the taskbar.
    public double Scale => _tray == IntPtr.Zero ? 1 : NativeMethods.GetDpiForWindow(_tray) / 96.0;

    // The widget's rectangle on screen, in physical pixels.
    public Rect ScreenBounds
        => _hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out var r)
            ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
            : Rect.Empty;

    // Tries once right away, then keeps trying: the taskbar may not exist yet (logon) or may be restarting.
    public void Start()
    {
        if (_disposed) return;
        Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_disposed) return;
        try
        {
            var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            // Explorer restarted (new taskbar handle) or our window was destroyed with the old one.
            if (_window is not null && (tray != _tray || !NativeMethods.IsWindow(_hwnd))) DropWindow();
            if (tray == IntPtr.Zero) return;

            if (_window is null && !CreateWindow(tray)) return;
            if (_ticks++ % ScanEveryTicks == 0) _scanner.RequestScan(_tray);
            SetAttached(Layout());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Taskbar widget update failed: {exception.Message}");
            DropWindow();
        }
    }

    private bool CreateWindow(IntPtr tray)
    {
        TaskbarWidgetWindow? window = null;
        try
        {
            window = new TaskbarWidgetWindow(_settings, _media, _visualizer);
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // Per the SetParent contract: switch popup -> child *before* reparenting.
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
            style = (style & ~NativeMethods.WsPopup) | NativeMethods.WsChild | NativeMethods.WsClipSiblings;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(style));
            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle,
                new IntPtr(exStyle | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
            // SetParent returns the *previous* parent, which is NULL for a top-level window, so verify the result instead.
            NativeMethods.SetParent(hwnd, tray);
            if (NativeMethods.GetParent(hwnd) != tray) throw new InvalidOperationException("SetParent failed");

            _tray = tray;
            _hwnd = hwnd;
            _window = window;
            StopGlide();
            _target = _current = default;
            _shown = false;
            _watcher.Watch(tray);
            _scanner.Reset();
            _ticks = 0;
            window.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
            window.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            window.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
            window.AppearanceChanged += (_, _) => RequestLayout();
            window.Closed += (sender, _) => { if (ReferenceEquals(sender, _window)) ForgetWindow(); };
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Taskbar widget unavailable: {exception.Message}");
            DropWindow(window);
            return false;
        }
    }

    // Scans now and a few more times while the taskbar settles. A change during a burst starts it over, but never
    // pushes back a scan that is already due, so a stream of events cannot starve the scans.
    private void StartBurst()
    {
        if (_disposed || _window is null) return;
        var due = _burstTimer.IsEnabled && _burstStep <= 1;
        _burstStep = 0;
        if (due) return;
        _burstTimer.Stop();
        _burstTimer.Interval = TimeSpan.FromMilliseconds(BurstDelays[0]);
        _burstTimer.Start();
    }

    private void BurstTick()
    {
        if (_disposed || _window is null)
        {
            _burstTimer.Stop();
            return;
        }
        _scanner.RequestScan(_tray);
        if (++_burstStep >= BurstDelays.Length)
        {
            _burstTimer.Stop();
            return;
        }
        _burstTimer.Interval = TimeSpan.FromMilliseconds(BurstDelays[_burstStep] - BurstDelays[_burstStep - 1]);
    }

    private void RequestLayout()
    {
        if (_disposed || _layoutQueued) return;
        _layoutQueued = true;
        // Deferred so the view finishes updating itself first, and so a burst of requests is one layout. Normal
        // priority, not Background: the visualizer redraws every frame and would starve Background work, which made
        // the widget lag one change behind.
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _layoutQueued = false;
            if (_disposed || _window is null) return;
            try { SetAttached(Layout()); }
            catch (Exception exception) { Debug.WriteLine($"Taskbar widget layout failed: {exception.Message}"); }
        });
    }

    // Docks the widget on the taskbar, vertically centred, above the taskbar's content layer.
    // Returns false (and hides the widget) when there is no usable spot at the moment.
    private bool Layout()
    {
        var notify = NativeMethods.FindWindowEx(_tray, IntPtr.Zero, "TrayNotifyWnd", null);
        Rect? target = null;
        if (NativeMethods.GetWindowRect(_tray, out var taskbar) && notify != IntPtr.Zero
            && NativeMethods.GetWindowRect(notify, out var tray))
        {
            var taskbarRect = ToRect(taskbar);
            // A taskbar at the left or right edge runs top to bottom: the widget turns with it. Its "length" below is
            // then a height, and the bars grow away from the screen edge the taskbar is docked to.
            var vertical = TaskbarDocking.IsVertical(taskbarRect);
            var onFarEdge = vertical && IsOnFarSide(taskbarRect);
            _window!.SetOrientation(vertical ? Orientation.Vertical : Orientation.Horizontal, growFromEnd: onFarEdge);

            // The widget has to keep at least its visualizer; the track title is what gives way in a tight spot.
            var minimumLength = 2 * _settings.WidgetPadding + _settings.VisualizerWidth;
            var wishedLength = Math.Max(minimumLength, _window.MeasureDesiredLength());
            target = TaskbarDocking.Calculate(taskbarRect, ToRect(tray), _scanner.Buttons,
                new Size(wishedLength, TaskbarWidgetWindow.WidgetHeight), minimumLength, Scale, GapFromNeighbours,
                CrossPadding, _settings.Alignment, WindowsTaskbarInfo.IconsCentered, _settings.EdgeOffset);
        }

        if (target is not { } rect)
        {
            StopGlide();
            if (_shown) NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpHideWindow);
            _shown = false;
            return false;
        }

        var origin = new NativePoint { X = (int)rect.X, Y = (int)rect.Y };
        NativeMethods.ScreenToClient(_tray, ref origin);
        var bounds = new Bounds(origin.X, origin.Y, (int)rect.Width, (int)rect.Height);

        if (!_shown)
        {
            // Appearing: straight to its spot, where the window's own entrance (a fade and a little growth) plays.
            _target = bounds;
            _window!.Show();
            Place(bounds, raise: true);
            _shown = true;
            return true;
        }

        if (bounds != _target)
        {
            MoveTo(bounds);
            return true;
        }

        // Re-assert the top of the taskbar's child z-order when something (e.g. a fresh composition bridge) covers us.
        if (!_gliding && NativeMethods.GetWindow(_tray, NativeMethods.GwChild) != _hwnd) Place(_current, raise: true);
        return true;
    }

    // Slides and resizes to a new spot from wherever the widget is now, so a target that changes mid-way (a slider
    // being dragged, buttons still animating in) is followed without a jump. A spot that does not touch the current
    // one (another alignment, the taskbar turned) is not travelled to across the icons: the widget reappears there.
    private void MoveTo(Bounds bounds)
    {
        _target = bounds;
        if (!Motion.Enabled || !_current.Touches(bounds) || _current.IsVertical != bounds.IsVertical)
        {
            StopGlide();
            Place(bounds, raise: true);
            if (Motion.Enabled) _window!.PlayAppear();
            return;
        }

        _glideFrom = _current;
        _glideClock.Restart();
        if (_gliding) return;
        _gliding = true;
        CompositionTarget.Rendering += OnGlideFrame;
    }

    private void OnGlideFrame(object? sender, EventArgs e)
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            StopGlide();
            return;
        }
        var progress = Math.Min(1, _glideClock.Elapsed.TotalMilliseconds / GlideMilliseconds);
        if (progress >= 1)
        {
            StopGlide();
            Place(_target, raise: true);
            return;
        }
        var step = Bounds.Lerp(_glideFrom, _target, Motion.Decelerate.Ease(progress));
        if (step != _current) Place(step, raise: false);
    }

    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnGlideFrame;
    }

    // raise: also put the widget back on top of the taskbar's children. Left out for the frames of a glide, as a
    // z-order change makes each of them more expensive.
    private void Place(Bounds bounds, bool raise)
    {
        // With raise, no SWP_NOZORDER: it would silently turn HWND_TOP into a no-op.
        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | (raise ? 0 : NativeMethods.SwpNoZOrder);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTop, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
        _current = bounds;
    }

    // True when a vertical taskbar is on the right half of its monitor's work area (so the work area is to its left).
    private static bool IsOnFarSide(Rect taskbar)
    {
        var work = MonitorWorkArea.Get(taskbar);
        return taskbar.Left + taskbar.Width / 2 > work.Left + work.Width / 2;
    }

    private static Rect ToRect(NativeRect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private void SetAttached(bool attached)
    {
        if (_attached == attached) return;
        _attached = attached;
        AttachedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ForgetWindow()
    {
        StopGlide();
        _burstTimer.Stop();
        _window = null;
        _hwnd = IntPtr.Zero;
        _shown = false;
        SetAttached(false);
    }

    private void DropWindow(TaskbarWidgetWindow? window = null)
    {
        window ??= _window;
        ForgetWindow();
        try { window?.Close(); }
        catch (Exception exception) { Debug.WriteLine($"Taskbar widget cleanup: {exception.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _burstTimer.Stop();
        _watcher.Dispose();
        DropWindow();
    }

    // A rectangle in whole pixels, as SetWindowPos takes it.
    private readonly record struct Bounds(int X, int Y, int Width, int Height)
    {
        public bool IsVertical => Height > Width;

        // Overlapping or edge to edge.
        public bool Touches(Bounds other)
            => X <= other.X + other.Width && other.X <= X + Width && Y <= other.Y + other.Height && other.Y <= Y + Height;

        public static Bounds Lerp(Bounds from, Bounds to, double t)
        {
            static int Mix(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
            return new Bounds(Mix(from.X, to.X, t), Mix(from.Y, to.Y, t), Mix(from.Width, to.Width, t), Mix(from.Height, to.Height, t));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static class NativeMethods
    {
        public const int GwlStyle = -16;
        public const int GwlExStyle = -20;
        public const long WsPopup = 0x80000000L;
        public const long WsChild = 0x40000000L;
        public const long WsClipSiblings = 0x04000000L;
        public const long WsExToolWindow = 0x80;
        public const long WsExNoActivate = 0x08000000L;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;
        public const uint SwpHideWindow = 0x0080;
        public const uint GwChild = 5;
        public static readonly IntPtr HwndTop = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    }
}
