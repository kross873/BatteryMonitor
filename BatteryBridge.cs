using Microsoft.Win32;
namespace BatteryMonitor;
internal static class BatteryBridge
{
    // Legacy single-device QWORD is preserved for Windhawk 0.3.1.
    // V2 is an atomic string: UTC epoch seconds, then one line per selected device: percent TAB label.
    public static void Publish(int? percent) => Write(percent, Array.Empty<Peripheral>());
    public static void PublishDevices(IReadOnlyList<Peripheral> devices) => Write(devices.FirstOrDefault()?.BatteryPercent, devices);
    private static void Write(int? percent, IReadOnlyList<Peripheral> devices)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\BatteryMonitor\NativeWidget");
            if (key is null) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long packed = percent is >= 0 and <= 100 ? now * 128 + percent.Value : 0;
            key.SetValue("MouseReading", packed, RegistryValueKind.QWord);
            var lines = new List<string> { now.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            foreach (var d in devices.Take(3))
            {
                if (d.BatteryPercent is not int p || p < 0 || p > 100) continue;
                string label = new string(d.Label.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Take(14).ToArray()).Trim();
                if (label.Length == 0) label = "Device";
                lines.Add($"{p}\t{label}");
            }
            key.SetValue("DeviceReadingsV2", string.Join("\n", lines), RegistryValueKind.String);
        }
        catch { /* The tray remains usable if publishing fails. */ }
    }
}
