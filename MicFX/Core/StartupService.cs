using Microsoft.Win32;

namespace MicFX.Core;

public static class StartupService
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MicFX";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath ??
                    System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
