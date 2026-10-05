using System.Diagnostics;
using Microsoft.Win32;

namespace FluentFlow.Services;

// "Start with Windows" for the current user: a value under HKCU\...\Run, the same mechanism Task Manager's Startup tab
// shows. No elevation is needed and nothing outside the user's own registry hive is touched.
public static class StartupRegistration
{
    public const string AutostartArgument = "--autostart";
    private const string ValueName = "FluentFlow";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    // True when Windows will really start the app at sign-in: the Run value exists and was not switched off
    // in Task Manager (which records that separately under StartupApproved).
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                // First byte: 2 (or any even value) = enabled, odd = disabled by the user.
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not read the startup setting: {exception.Message}");
                return false;
            }
        }
    }

    // Returns false when the registry refused the change (policy, permissions); the caller keeps the old state.
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path)) return false;
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                // The argument tells the app it was started by Windows: the taskbar may not be ready yet.
                run.SetValue(ValueName, $"\"{path}\" {AutostartArgument}");
                // A stale "disabled" mark from Task Manager would make our new entry ineffective.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not change the startup setting: {exception.Message}");
            return false;
        }
    }
}
