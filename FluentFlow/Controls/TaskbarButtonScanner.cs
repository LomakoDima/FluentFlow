using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;

namespace FluentFlow.Controls;

// Reads the on-screen rectangles of the taskbar's buttons through UI Automation. The Windows 11 taskbar draws its
// icons itself, so there is no Win32 way to learn where they end. Automation calls go to Explorer and can be slow
// or stall, so this only ever runs on a worker thread; a failed or missing scan simply means "no constraint".
internal sealed class TaskbarButtonScanner
{
    private const string TrayClassPrefix = "SystemTray.";
    private const double SameTolerance = 0.5; // px; UIA reports doubles, a sub-pixel wobble is not a change
    private volatile IReadOnlyList<Rect> _buttons = [];
    private IntPtr _taskbar;
    private int _running;
    private int _pending;

    // The most recent result, in physical screen pixels. Empty until the first scan succeeds.
    public IReadOnlyList<Rect> Buttons => _buttons;

    // Raised on a worker thread when a scan found the buttons somewhere else than the previous one did.
    public event Action? Changed;

    // Starts a scan. A request made while one is running is not dropped: that scan is followed by one more, so the
    // result always reflects the taskbar as it was after the last request (the taskbar animates new buttons in).
    public void RequestScan(IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero) return;
        Volatile.Write(ref _taskbar, taskbar);
        Volatile.Write(ref _pending, 1);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _ = Task.Run(Run);
    }

    public void Reset() => _buttons = [];

    private void Run()
    {
        do
        {
            while (Interlocked.Exchange(ref _pending, 0) == 1) ScanOnce(Volatile.Read(ref _taskbar));
            Volatile.Write(ref _running, 0);
            // A request that arrived between the last check and the reset above saw "running" and left it to us.
        } while (Volatile.Read(ref _pending) == 1 && Interlocked.CompareExchange(ref _running, 1, 0) == 0);
    }

    private void ScanOnce(IntPtr taskbar)
    {
        try
        {
            var found = Scan(taskbar);
            if (Same(found, _buttons)) return;
            _buttons = found;
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            // Explorer restarting or an element vanishing mid-scan is normal; keep the previous result.
            Debug.WriteLine($"Taskbar scan failed: {exception.Message}");
        }
    }

    private static bool Same(IReadOnlyList<Rect> a, IReadOnlyList<Rect> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (Math.Abs(a[i].X - b[i].X) > SameTolerance || Math.Abs(a[i].Y - b[i].Y) > SameTolerance
                || Math.Abs(a[i].Width - b[i].Width) > SameTolerance || Math.Abs(a[i].Height - b[i].Height) > SameTolerance)
                return false;
        }
        return true;
    }

    private static List<Rect> Scan(IntPtr taskbar)
    {
        var root = AutomationElement.FromHandle(taskbar);
        var buttons = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var result = new List<Rect>();
        foreach (AutomationElement button in buttons)
        {
            // The notification area's own buttons are not "before the tray": the docking maths handles the tray itself.
            if (button.Current.ClassName.StartsWith(TrayClassPrefix, StringComparison.Ordinal)) continue;
            var bounds = button.Current.BoundingRectangle;
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) result.Add(bounds);
        }
        return result;
    }
}
