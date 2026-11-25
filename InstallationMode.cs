using Microsoft.Win32;

namespace BatteryMonitor;

internal static class InstallationMode
{
    public static bool UsesWindhawk()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\BatteryMonitor\Settings");
            return string.Equals(key?.GetValue("InstallationMode") as string, "windhawk", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
