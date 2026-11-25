using Microsoft.Win32;
namespace BatteryMonitor;
internal static class UpdatePreferences
{
    private const string Key = @"Software\BatteryMonitor\Settings";
    internal static bool AutoCheck
    {
        get { try { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("AutoCheckUpdates") is int v && v == 1; } catch { return false; } }
        set { using var key = Registry.CurrentUser.CreateSubKey(Key) ?? throw new InvalidOperationException("Cannot save update preferences."); key.SetValue("AutoCheckUpdates", value ? 1 : 0, RegistryValueKind.DWord); }
    }
}
