using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentFlow.Controls;

// Tells the taskbar widget the moment the taskbar is likely to have changed, so it can step aside before it covers a
// new button instead of finding out at the next poll. Two kinds of change matter:
//  - a window that gets a taskbar button appears or goes away (shown/hidden, or cloaked/uncloaked: UWP apps and
//    virtual desktop switches cloak windows instead of hiding them): ButtonsMayHaveChanged;
//  - the taskbar or one of its own parts (the notification area) moves or resizes: TaskbarMoved.
// The hooks are out-of-context WinEvent hooks, so the callbacks arrive on the thread that installed them (the UI
// thread) through its message loop, and they only cost a few window-style reads per event.
internal sealed class TaskbarChangeWatcher : IDisposable
{
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectCloaked = 0x8017;
    private const uint EventObjectUncloaked = 0x8018;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private const uint GaRoot = 2;
    private const uint GwOwner = 4;

    // Kept in a field: the native side only holds a pointer, and a collected delegate would crash the next callback.
    private readonly WinEventProc _callback;
    private readonly List<IntPtr> _hooks = [];
    private IntPtr _taskbarHook;
    private IntPtr _taskbar;
    private bool _disposed;

    public TaskbarChangeWatcher()
    {
        _callback = OnWinEvent;
        Hook(EventObjectShow, EventObjectHide, 0);
        Hook(EventObjectCloaked, EventObjectUncloaked, 0);
    }

    public event EventHandler? ButtonsMayHaveChanged;
    public event EventHandler? TaskbarMoved;

    // Follows a (new) taskbar: location changes are only watched in Explorer, which also owns the taskbar's children.
    public void Watch(IntPtr taskbar)
    {
        if (_disposed || taskbar == _taskbar) return;
        if (_taskbarHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_taskbarHook);
            _hooks.Remove(_taskbarHook);
            _taskbarHook = IntPtr.Zero;
        }
        _taskbar = taskbar;
        if (taskbar == IntPtr.Zero) return;
        NativeMethods.GetWindowThreadProcessId(taskbar, out var explorer);
        if (explorer != 0) _taskbarHook = Hook(EventObjectLocationChange, EventObjectLocationChange, explorer);
    }

    private IntPtr Hook(uint first, uint last, uint process)
    {
        var hook = NativeMethods.SetWinEventHook(first, last, IntPtr.Zero, _callback, process, 0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        if (hook == IntPtr.Zero) Debug.WriteLine($"WinEvent hook 0x{first:X} unavailable");
        else _hooks.Add(hook);
        return hook;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed || hwnd == IntPtr.Zero || idObject != ObjIdWindow || idChild != ChildIdSelf) return;
        try
        {
            if (eventType == EventObjectLocationChange)
            {
                if (_taskbar != IntPtr.Zero && (hwnd == _taskbar || NativeMethods.GetParent(hwnd) == _taskbar))
                    TaskbarMoved?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (MayHaveTaskbarButton(hwnd)) ButtonsMayHaveChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Taskbar change event failed: {exception.Message}");
        }
    }

    // The shell's own rule, roughly: a top-level window that is not a tool window, and either has no owner or asks for
    // a button anyway. Tooltips, menus, carets and child windows (by far most of the events) fail it at once.
    private static bool MayHaveTaskbarButton(IntPtr hwnd)
    {
        if (NativeMethods.GetAncestor(hwnd, GaRoot) != hwnd) return false;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((exStyle & WsExAppWindow) != 0) return true;
        if ((exStyle & WsExToolWindow) != 0) return false;
        return NativeMethods.GetWindow(hwnd, GwOwner) == IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        _taskbarHook = IntPtr.Zero;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
            uint idProcess, uint idThread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    }
}
