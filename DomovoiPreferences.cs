using Microsoft.Win32;

namespace BatteryMonitor;

// Independent of taskbar appearance and update settings.
internal static class DomovoiPreferences
{
    private const string Key = @"Software\BatteryMonitor\Domovoi";
    internal const int DefaultX = 9800, DefaultY = 9800;

    internal static bool LoadEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("Enabled") is not int value || value != 0;
        }
        catch { return true; }
    }

    internal static void SaveEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key)
            ?? throw new InvalidOperationException("Cannot save Domovoi preferences.");
        key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    internal static (int X, int Y) LoadPosition()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            int x = key?.GetValue("X") is int px ? px : DefaultX;
            int y = key?.GetValue("Y") is int py ? py : DefaultY;
            return (Math.Clamp(x, 0, 10000), Math.Clamp(y, 0, 10000));
        }
        catch { return (DefaultX, DefaultY); }
    }

    internal static void SavePosition(int x, int y)
    {
        // A failed cosmetic preference write must not interrupt dragging.
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key?.SetValue("X", Math.Clamp(x, 0, 10000), RegistryValueKind.DWord);
            key?.SetValue("Y", Math.Clamp(y, 0, 10000), RegistryValueKind.DWord);
        }
        catch { }
    }
}
