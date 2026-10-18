using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FluentFlow.Services;

public static class WindowsTaskbarInfo
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int Windows11FirstBuild = 22000;

    // The "Taskbar alignment" setting (Settings > Personalization > Taskbar > Taskbar behaviors). The value is only
    // written once the user has changed it; until then Windows 11 centres the icons and Windows 10 never does.
    public static bool IconsCentered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
                if (key?.GetValue("TaskbarAl") is int alignment) return alignment == 1;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not read the taskbar alignment: {exception.Message}");
            }
            return Environment.OSVersion.Version.Build >= Windows11FirstBuild;
        }
    }

    // True when the taskbar is docked at the left or right edge of the screen (taller than it is wide).
    public static bool IsVertical
    {
        get
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            return taskbar != IntPtr.Zero && GetWindowRect(taskbar, out var r) && r.Bottom - r.Top > r.Right - r.Left;
        }
    }

    // The taskbar follows the "system" theme, which is separate from the app theme.
    public static bool SystemUsesLightTheme
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("SystemUsesLightTheme") is int light) return light == 1;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not read the system theme: {exception.Message}");
            }
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
