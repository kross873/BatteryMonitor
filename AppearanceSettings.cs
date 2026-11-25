using Microsoft.Win32;
using System.Globalization;

namespace BatteryMonitor;

// Preserve the registry location and format for migration of existing appearance preferences.
internal sealed record AppearanceSettings(string Style, string Icon, string ColorMode,
    string CustomColor, int FontSize, int IconSize, string Weight, int Gap)
{
    private const string KeyPath = @"Software\BatteryMonitor\NativeWidget";
    private const string ValueName = "AppearanceV1";
    public static readonly AppearanceSettings Default = new("native", "preset", "preset", "#66CCFF", 0, 0, "preset", 5);
    public static bool ValidColor(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
    public bool IsValid() =>
        new[] { "native", "minimal", "gaming", "battery" }.Contains(Style) &&
        new[] { "preset", "mouse", "battery", "none" }.Contains(Icon) &&
        new[] { "preset", "system", "charge", "cyan", "purple", "white", "custom" }.Contains(ColorMode) &&
        new[] { "preset", "normal", "semibold", "bold" }.Contains(Weight) && ValidColor(CustomColor) &&
        (FontSize == 0 || FontSize is >= 10 and <= 22) &&
        (IconSize == 0 || IconSize is >= 10 and <= 24) && Gap is >= 0 and <= 16;

    public static (bool Enabled, AppearanceSettings Settings) Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(ValueName) is string raw)
            {
                string[] f = raw.Split('|');
                if (f.Length == 9 && f[0] == "1" && int.TryParse(f[5], out int font) &&
                    int.TryParse(f[6], out int icon) && int.TryParse(f[8], out int gap))
                {
                    var result = new AppearanceSettings(f[1], f[2], f[3], f[4], font, icon, f[7], gap);
                    if (result.IsValid()) return (true, result);
                }
            }
        }
        catch { }
        return (false, Default);
    }

    public void Save(bool enabled)
    {
        if (enabled && !IsValid()) throw new ArgumentException("Проверьте размеры и HEX-цвет (#RRGGBB).");
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath) ?? throw new InvalidOperationException("Не удалось сохранить оформление.");
        if (!enabled) { key.DeleteValue(ValueName, false); return; }
        string raw = string.Join("|", "1", Style, Icon, ColorMode, CustomColor,
            FontSize.ToString(CultureInfo.InvariantCulture), IconSize.ToString(CultureInfo.InvariantCulture), Weight, Gap.ToString(CultureInfo.InvariantCulture));
        key.SetValue(ValueName, raw, RegistryValueKind.String);
    }
}
