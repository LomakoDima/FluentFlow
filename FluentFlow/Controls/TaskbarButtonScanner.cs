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
    private volatile IReadOnlyList<Rect> _buttons = [];
    private int _running;

    // The most recent result, in physical screen pixels. Empty until the first scan succeeds.
    public IReadOnlyList<Rect> Buttons => _buttons;

    // Starts a scan unless one is still running.
    public void RequestScan(IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero || Interlocked.Exchange(ref _running, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                _buttons = Scan(taskbar);
            }
            catch (Exception exception)
            {
                // Explorer restarting or an element vanishing mid-scan is normal; keep the previous result.
                Debug.WriteLine($"Taskbar scan failed: {exception.Message}");
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }

    public void Reset() => _buttons = [];

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
