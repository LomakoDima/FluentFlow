using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
    private const double VerticalPadding = 2;
    private const double MinimumWidth = 72;
    private const int ScanEveryTicks = 3;
    private readonly AudioVisualizerService _visualizer;
    private readonly TaskbarButtonScanner _scanner = new();
    private readonly DispatcherTimer _timer;
    private TaskbarWidgetWindow? _window;
    private IntPtr _tray;
    private IntPtr _hwnd;
    private (int X, int Y, int Width, int Height) _lastBounds;
    private bool _shown;
    private bool _attached;
    private int _ticks;
    private bool _disposed;

    public TaskbarWidgetHost(AudioVisualizerService visualizer, Dispatcher dispatcher)
    {
        _visualizer = visualizer;
        // Polling is what keeps this robust: taskbar size, tray width, DPI and Explorer restarts have no common event.
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _timer.Stop();
    }

    public event EventHandler? Clicked;
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
            window = new TaskbarWidgetWindow(_visualizer);
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
            _lastBounds = default;
            _shown = false;
            _scanner.Reset();
            _ticks = 0;
            window.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
            window.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
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

    // Docks the widget next to the notification area, vertically centred, above the taskbar's content layer.
    // Returns false (and hides the widget) when there is no usable spot at the moment.
    private bool Layout()
    {
        var notify = NativeMethods.FindWindowEx(_tray, IntPtr.Zero, "TrayNotifyWnd", null);
        Rect? target = null;
        if (NativeMethods.GetWindowRect(_tray, out var taskbar) && notify != IntPtr.Zero
            && NativeMethods.GetWindowRect(notify, out var tray))
        {
            target = TaskbarDocking.Calculate(ToRect(taskbar), ToRect(tray), _scanner.Buttons,
                new Size(TaskbarWidgetWindow.WidgetWidth, TaskbarWidgetWindow.WidgetHeight),
                MinimumWidth, Scale, GapFromNeighbours, VerticalPadding);
        }

        if (target is not { } rect)
        {
            if (_shown) NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpHideWindow);
            _shown = false;
            return false;
        }

        var origin = new NativePoint { X = (int)rect.X, Y = (int)rect.Y };
        NativeMethods.ScreenToClient(_tray, ref origin);
        var bounds = (origin.X, origin.Y, (int)rect.Width, (int)rect.Height);

        // Re-assert the top of the taskbar's child z-order when something (e.g. a fresh composition bridge) covers us.
        var covered = NativeMethods.GetWindow(_tray, NativeMethods.GwChild) != _hwnd;
        if (bounds == _lastBounds && !covered && _shown) return true;

        if (!_shown) _window!.Show();
        // No SWP_NOZORDER here: it would silently turn HWND_TOP into a no-op.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTop, origin.X, origin.Y, bounds.Item3, bounds.Item4,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _lastBounds = bounds;
        _shown = true;
        return true;
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
        DropWindow();
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
