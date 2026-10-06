using System.Diagnostics;
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
}
