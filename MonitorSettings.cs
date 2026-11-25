using Microsoft.Win32;

namespace BatteryMonitor;

internal sealed record MonitorSettings(int RefreshSeconds, bool ShowNativeWidget, bool TrayOnly = false)
{
    private const string KeyPath = @"Software\BatteryMonitor\Settings";
    public static readonly int[] Intervals = [30, 60, 120];

    public static MonitorSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            int seconds = key?.GetValue("RefreshSeconds") is int n && Intervals.Contains(n) ? n : 60;
            bool show = key?.GetValue("ShowNativeWidget") is not int v || v != 0;
            bool trayOnly = key?.GetValue("TrayOnly") is int mode && mode != 0;
            return new MonitorSettings(seconds, show, trayOnly);
        }
        catch { return new MonitorSettings(60, true); }
    }

    public void Save()
    {
        if (!Intervals.Contains(RefreshSeconds)) throw new ArgumentOutOfRangeException(nameof(RefreshSeconds));
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath) ?? throw new InvalidOperationException("Не удалось сохранить настройки.");
        key.SetValue("RefreshSeconds", RefreshSeconds, RegistryValueKind.DWord);
        key.SetValue("TrayOnly", TrayOnly ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ShowNativeWidget", ShowNativeWidget ? 1 : 0, RegistryValueKind.DWord);
    }
}
