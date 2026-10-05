using System.Runtime.InteropServices;
using System.Windows;

namespace FluentFlow.Controls;

internal static class MonitorWorkArea
{
    private const uint MonitorDefaultToNearest = 2;

    // The monitor nearest to a rectangle in physical pixels, minus the taskbar wherever it is docked.
    public static Rect Get(Rect pixels)
    {
        var rect = new NativeRect
        {
            Left = (int)Math.Floor(pixels.Left), Top = (int)Math.Floor(pixels.Top),
            Right = (int)Math.Ceiling(pixels.Right), Bottom = (int)Math.Ceiling(pixels.Bottom)
        };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
