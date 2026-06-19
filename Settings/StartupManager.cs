using System.IO;
using Microsoft.Win32;

namespace ShakeToBigCursor.Settings;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsShakeToFindCursor";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Contains(ResolveLaunchTarget(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null)
            {
                return;
            }

            if (enabled)
            {
                var target = ResolveLaunchTarget();
                if (!string.IsNullOrEmpty(target))
                {
                    key.SetValue(ValueName, $"\"{target}\"");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup registration is best-effort and should never prevent the app from running.
        }
    }

    private static string ResolveLaunchTarget()
    {
        try
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "WindowsShakeToFindCursor.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            return Environment.ProcessPath ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
