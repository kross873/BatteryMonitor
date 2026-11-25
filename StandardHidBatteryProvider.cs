using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace BatteryMonitor;

internal static partial class HidUsbScanner
{
    // USB HID Usage Tables: Generic Device Controls / Battery Strength (06:20).
    // Read only declared scalar battery feature values; normalize the documented logical range.
    // No writes, guessed report IDs, input-report interception or configuration commands.
    private static int? TryReadStandardBattery(SafeFileHandle handle, string path, IntPtr preparsed, HIDP_CAPS caps)
    {
        if (caps.FeatureReportByteLength is < 2 or > 1024 || caps.NumberFeatureValueCaps is 0 or > 256) return null;
        const int HidPFeature = 2;
        const int Success = 0x00110000;
        ushort count = caps.NumberFeatureValueCaps;
        var values = new HIDP_VALUE_CAPS[count];
        if (HidP_GetSpecificValueCaps(HidPFeature, 0x06, 0, 0x20, values, ref count, preparsed) != Success) return null;
        for (int i = 0; i < Math.Min(count, values.Length); i++)
        {
            var cap = values[i];
            bool matches = cap.IsRange != 0 ? cap.Usage <= 0x20 && cap.UsageMax >= 0x20 : cap.Usage == 0x20;
            if (cap.UsagePage != 6 || !matches || cap.IsAbsolute == 0 ||
                cap.LogicalMin >= cap.LogicalMax || cap.ReportCount != 1 || cap.BitSize is < 1 or > 32) continue;
            var report = new byte[caps.FeatureReportByteLength];
            report[0] = cap.ReportID;
            bool read = HidD_GetFeature(handle, report, report.Length);
            if (!read)
            {
                // Some drivers require a read-access handle. Never request write access here.
                using var reader = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (reader.IsInvalid) continue;
                Array.Clear(report);
                report[0] = cap.ReportID;
                read = HidD_GetFeature(reader, report, report.Length);
            }
            if (!read || report[0] != cap.ReportID) continue;
            if (HidP_GetUsageValue(HidPFeature, 0x06, cap.LinkCollection, 0x20, out uint value, preparsed, report, (uint)report.Length) == Success &&
                BatteryValue.FromLogicalRange(value, cap.LogicalMin, cap.LogicalMax, cap.BitSize) is int percent)
                return percent;
        }
        return null;
    }

    private static string? KnownBrand(ushort vid) => vid switch
    {
        0x046D => "Logitech", 0x1532 => "Razer", 0x1038 => "SteelSeries", 0x1B1C => "Corsair",
        0x09DA => "A4Tech / Bloody", 0x0951 => "HyperX / Kingston", 0x03F0 => "HP / HyperX",
        0x258A => "SINO WEALTH", 0x3434 => "Keychron", 0x2DC8 => "8BitDo", 0x1A2C => "China Resource Semico", 0x3151 => "AJAZZ", 0x054C => "Sony", 0x045E => "Microsoft", _ => null
    };

    private static string? ReadProductName(SafeFileHandle handle)
    {
        var value = new StringBuilder(128);
        return HidD_GetProductString(handle, value, value.Capacity * 2) && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString().Trim() : null;
    }

    // Native HIDP_VALUE_CAPS, fixed 72-byte structure (Windows hidpi.h).
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort Usage;
        [FieldOffset(58)] public ushort UsageMax;
    }
    [DllImport("hid.dll")] private static extern int HidP_GetSpecificValueCaps(int reportType, ushort usagePage, ushort linkCollection, ushort usage, [Out] HIDP_VALUE_CAPS[] caps, ref ushort count, IntPtr preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, [In] byte[] report, uint reportLength);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder buffer, int bytes);
}
