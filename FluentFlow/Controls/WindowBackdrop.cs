using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FluentFlow.Controls;

// The Windows 11 Mica material behind a window whose title bar is drawn by the app (WindowChrome with
// GlassFrameThickness -1). Mica takes its tint from the wallpaper and dims when the window is inactive; when Windows
// has transparency effects turned off it turns into a plain colour by itself. Older Windows has no Mica, and the
// caller keeps its solid background there.
internal static class WindowBackdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropMainWindow = 2; // Mica

    // The documented backdrop attribute arrived in Windows 11 22H2.
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    // Returns true when Mica is on, so the window can drop its own background.
    public static bool TryApplyMica(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetDarkMode(window, dark);
        if (!IsMicaSupported) return false;
        try
        {
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
            var backdrop = BackdropMainWindow;
            return DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // The frame, the Mica tint and the system's own window parts follow this, not the app's colours.
    public static void SetDarkMode(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { }
    }

    // With the frame extended over the whole window Windows would draw its own caption buttons under the app's. Without
    // the system menu style it draws none; minimising, maximising, snapping and the window animations all stay.
    public static void HideSystemCaptionButtons(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        SetWindowLongPtr(handle, GwlStyle, new IntPtr(style & ~WsSysMenu));
    }

    private const int GwlStyle = -16;
    private const long WsSysMenu = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    // How far a maximised window with an app-drawn frame reaches past the screen on each side (its sizing frame plus
    // padding), in DIPs, so the content can be kept inside.
    public static Thickness MaximizedInset(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var pixels = GetSystemMetricsForDpi(SmCxSizeFrame, (uint)Math.Round(dpi.PixelsPerInchX))
            + GetSystemMetricsForDpi(SmCxPaddedBorder, (uint)Math.Round(dpi.PixelsPerInchX));
        var inset = pixels / dpi.DpiScaleX;
        return new Thickness(inset);
    }

    private const int SmCxSizeFrame = 32, SmCxPaddedBorder = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
