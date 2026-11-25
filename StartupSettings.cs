using Microsoft.Win32;
namespace BatteryMonitor;
internal static class StartupSettings
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatteryMonitor";
    private static string Command => "\"" + (Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь к программе.")) + "\"";
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    public static bool IsConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath) ?? throw new InvalidOperationException("Не удалось открыть настройки автозапуска.");
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
