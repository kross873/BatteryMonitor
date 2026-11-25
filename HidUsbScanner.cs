using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BatteryMonitor;

// Enumerates HID interfaces. Only on VID 3151 / PID 5007, usage FFFF:0002,
// sends the documented non-configuration 0xF7 telemetry poll and then reads
// feature status report 0x05. It never sends profile, DPI, firmware or EEPROM
// commands. Other devices are detected using metadata and standard battery feature reads only.
internal static partial class HidUsbScanner
{
    internal sealed record Result(List<Peripheral> Devices, string Diagnostics);
    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint FileShareRead = 0x01;
    private const uint FileShareWrite = 0x02;
    private const uint OpenExisting = 3;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const int ErrorNoMoreItems = 259;

    internal static Result Scan()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Battery Monitor 0.10.1 / HID device discovery");
        sb.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Paths, serial numbers and MAC addresses are NOT included.");
        sb.AppendLine("Only AJAZZ VID_3151 PID_5007 usage FFFF:0002: SET_FEATURE 0xF7 telemetry poll, followed by GET_FEATURE 0x05. No configuration changes.");
        sb.AppendLine("G435 VID_3634 PID_4155: HID identification/metadata ONLY. No feature, output or input report transactions.");
        sb.AppendLine();
        var discovered = new List<Peripheral>();
        var ajazzReadDone = false;
        var headsetFound = false;
        var otherDevices = new Dictionary<string, Peripheral>(StringComparer.OrdinalIgnoreCase);
        var ajazzFound = false;

        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1))
        {
            sb.AppendLine($"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
            return new Result(discovered, sb.ToString());
        }
        try
        {
            for (uint index = 0; index < 512; index++)
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref iface))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorNoMoreItems) sb.AppendLine($"Enumeration stopped: {error}");
                    break;
                }

                SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                if (required < 8 || required > 65536) continue;
                var data = Marshal.AllocHGlobal((int)required);
                try
                {
                    // Unicode SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize is 8 on x64.
                    Marshal.WriteInt32(data, IntPtr.Size == 8 ? 8 : 6);
                    var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!SetupDiGetDeviceInterfaceDetailWithInfo(set, ref iface, data, required, out _, ref info)) continue;
                    var devicePath = Marshal.PtrToStringUni(IntPtr.Add(data, 4));
                    if (string.IsNullOrEmpty(devicePath)) continue;

                    // zero access is permitted for non-mutating HID metadata and feature reads.
                    using var handle = CreateFileW(devicePath, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (!HidD_GetAttributes(handle, ref attr)) continue;

                    var isAjazz = attr.VendorID == 0x3151 && attr.ProductID == 0x5007;
                    var isHeadset = attr.VendorID == 0x3634 && attr.ProductID == 0x4155;
                    var isDualSense = attr.VendorID == 0x054C && (attr.ProductID == 0x0CE6 || attr.ProductID == 0x0DF2);
                    var isDualShock = attr.VendorID == 0x054C && (attr.ProductID == 0x05C4 || attr.ProductID == 0x09CC);


                    ushort usagePage = 0, usage = 0, featureLength = 0, inputLength = 0, outputLength = 0;
                    int? standardPercent = null;
                    if (HidD_GetPreparsedData(handle, out var preparsed))
                    {
                        try
                        {
                            if (HidP_GetCaps(preparsed, out var caps) >= 0)
                            {
                                usagePage = caps.UsagePage;
                                usage = caps.Usage;
                                featureLength = caps.FeatureReportByteLength;
                                inputLength = caps.InputReportByteLength;
                                outputLength = caps.OutputReportByteLength;
                                if (!isAjazz && !isHeadset)
                                    standardPercent = TryReadStandardBattery(handle, preparsed, caps);
                            }
                        }
                        finally { HidD_FreePreparsedData(preparsed); }
                    }
                    var brand = KnownBrand(attr.VendorID);
                    bool inputPeripheral = usagePage == 1 && usage is 2 or 4 or 5 or 6 or 8;
                    if (!(isAjazz || isHeadset || isDualSense || isDualShock || inputPeripheral || brand != null || standardPercent.HasValue)) continue;
                    sb.AppendLine($"VID_{attr.VendorID:X4} PID_{attr.ProductID:X4} usage={usagePage:X4}:{usage:X4} input_len={inputLength} output_len={outputLength} feature_len={featureLength}");
                    if (isHeadset && !headsetFound)
                    {
                        headsetFound = true;
                        discovered.Add(new Peripheral("Logitech G435 (USB-приёмник)", null, true,
                            "USB receiver 3634:4155 present; this does NOT prove the headset is on. No safe battery-reading protocol known for this receiver."));
                    }
                    if (isAjazz)
                    {
                        ajazzFound = true;
                        // FFff:0002 is the control collection established for the 3151:5007 family.
                        if (usagePage == 0xFFFF && usage == 0x0002 && !ajazzReadDone)
                        {
                            ajazzReadDone = true;
                            var percent = TryReadAjazz(handle, devicePath, featureLength, sb);
                            discovered.Add(new Peripheral("AJAZZ wireless mouse (3151:5007)", percent, true,
                                percent.HasValue ? "AJAZZ F7 status poll + HID 0x05 (experimental)" : "No valid battery telemetry yet"));
                        }
                    }
                    if (!isAjazz && !isHeadset)
                    {
                        string id = $"hid:{attr.VendorID:X4}:{attr.ProductID:X4}";
                        var container = ReadContainerId(set, ref info);
                        if (container.HasValue) id += ":" + container.Value.ToString("N");
                        string name = isDualSense ? "Sony DualSense" : isDualShock ? "Sony DualShock 4" :
                            ReadProductName(handle) ?? brand ?? (usage == 2 ? "Мышь" : usage == 6 ? "Клавиатура" : "HID-устройство");
                        var candidate = new Peripheral($"{name} (HID {attr.VendorID:X4}:{attr.ProductID:X4})", standardPercent, true,
                            standardPercent.HasValue ? "HID: стандартный Battery Strength (0–100)" : "HID обнаружен; заряд не предоставлен. Может быть проводным или USB-приёмником.", id, name);
                        if (!otherDevices.TryGetValue(id, out var previous) || (!previous.BatteryPercent.HasValue && candidate.BatteryPercent.HasValue))
                            otherDevices[id] = candidate;
                        if (standardPercent.HasValue) sb.AppendLine($"Standard HID battery: {standardPercent}%");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Interface skipped: {ex.GetType().Name}");
                }
                finally { Marshal.FreeHGlobal(data); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        if (ajazzFound && !ajazzReadDone)
        {
            sb.AppendLine("AJAZZ 3151:5007 present, but FFff:0002 control collection not found.");
            discovered.Add(new Peripheral("AJAZZ wireless mouse (3151:5007)", null, true,
                "Vendor control collection not found"));
        }
        if (!ajazzFound) sb.AppendLine("AJAZZ candidate 3151:5007 not present in HID interfaces.");
        if (!headsetFound) sb.AppendLine("Headset candidate 3634:4155 not present in HID interfaces.");
        else sb.AppendLine("G435: no battery commands attempted; reported % cannot be derived from this receiver's HID caps.");
        discovered.AddRange(otherDevices.Values);
        return new Result(discovered, sb.ToString());
    }

    // DEVPKEY_Device_ContainerId groups collections of the same physical device.
    // If unsupported, fall back to VID/PID (same-model devices may then merge).
    private static Guid? ReadContainerId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var property = new DEVPROPKEY { fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2 };
        var bytes = new byte[16];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref property, out uint type, bytes, 16, out uint required, 0) || type != 13 || required != 16) return null;
        var id = new Guid(bytes);
        return id == Guid.Empty ? null : id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailWithInfo(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface, IntPtr data, uint size, out uint required, ref SP_DEVINFO_DATA info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint type, [Out] byte[] data, uint size, out uint required, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsedData);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(SafeFileHandle h, [In] byte[] reportBuffer, int reportBufferLength);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle h, [In, Out] byte[] reportBuffer, int reportBufferLength);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
