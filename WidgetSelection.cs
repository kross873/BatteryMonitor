using Microsoft.Win32;
namespace BatteryMonitor;

internal sealed record WidgetSelection(bool Auto, HashSet<string> Keys)
{
    private const string Path = @"Software\BatteryMonitor\Settings";
    public static WidgetSelection Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Path);
            bool auto = key?.GetValue("WidgetAuto") is not int value || value != 0;
            var keys = key?.GetValue("WidgetDevices") as string[] ?? Array.Empty<string>();
            return new WidgetSelection(auto, new HashSet<string>(keys.Take(3), StringComparer.OrdinalIgnoreCase));
        }
        catch { return new WidgetSelection(true, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); }
    }
    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path) ?? throw new InvalidOperationException("Не удалось сохранить выбор устройств.");
        key.SetValue("WidgetAuto", Auto ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("WidgetDevices", Keys.ToArray(), RegistryValueKind.MultiString);
    }
    public Peripheral[] Select(IReadOnlyList<Peripheral> devices) => devices
        .Where(d => d.Connected && d.BatteryPercent is >= 0 and <= 100 && (Auto || Keys.Contains(d.Key)))
        .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
        .OrderBy(d => d.Name.StartsWith("AJAZZ", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(d => d.Name).Take(3).ToArray();
}
