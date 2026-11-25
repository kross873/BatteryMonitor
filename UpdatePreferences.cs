using Microsoft.Win32;
using System.Globalization;
namespace BatteryMonitor;
internal static class UpdatePreferences
{
    private const string Key = @"Software\BatteryMonitor\Settings";
    internal static DateTimeOffset? LastSuccessfulCheck
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Key);
                return DateTimeOffset.TryParseExact(key?.GetValue("LastSuccessfulUpdateCheck") as string, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
            }
            catch { return null; }
        }
    }
    internal static void RecordSuccessfulCheck()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key?.SetValue("LastSuccessfulUpdateCheck", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        catch { /* A preference write must not prevent a verified update. */ }
    }
    internal static bool AutoCheck
    {
        get { try { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("AutoCheckUpdates") is int v && v == 1; } catch { return false; } }
        set { using var key = Registry.CurrentUser.CreateSubKey(Key) ?? throw new InvalidOperationException("Cannot save update preferences."); key.SetValue("AutoCheckUpdates", value ? 1 : 0, RegistryValueKind.DWord); }
    }
}
