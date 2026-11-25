using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
namespace BatteryMonitor;

internal static class BluetoothBatteryScanner
{
    private const string ClassicBluetoothProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string BluetoothLeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private static readonly string[] Properties =
    [
        "System.Devices.Aep.BatteryLife",
        "System.Devices.BatteryLife",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.ProtocolId"
    ];

    public static async Task<List<Peripheral>> ScanAsync()
    {
        var all = new Dictionary<string, Peripheral>(StringComparer.OrdinalIgnoreCase);
        using var scanTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        foreach (var protocol in new[] { ClassicBluetoothProtocol, BluetoothLeProtocol })
        {
            var selector = $"System.Devices.Aep.ProtocolId:=\"{protocol}\"";
            DeviceInformationCollection found;
            try { found = await DeviceInformation.FindAllAsync(selector, Properties, DeviceInformationKind.AssociationEndpoint).AsTask(scanTimeout.Token); }
            catch
            {
                if (scanTimeout.IsCancellationRequested) break;
                try { found = await DeviceInformation.FindAllAsync(selector, Properties.Where(p => p != "System.Devices.BatteryLife").ToArray(), DeviceInformationKind.AssociationEndpoint).AsTask(scanTimeout.Token); }
                catch { if (scanTimeout.IsCancellationRequested) break; else continue; }
            }
            foreach (var info in found)
            {
                var name = string.IsNullOrWhiteSpace(info.Name) ? "Unnamed Bluetooth device" : info.Name.Trim();
                var battery = ReadPercent(info.Properties.TryGetValue("System.Devices.Aep.BatteryLife", out var raw) ? raw : null);
                battery ??= ReadPercent(info.Properties.TryGetValue("System.Devices.BatteryLife", out var deviceBattery) ? deviceBattery : null);
                var connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is bool b && b;
                if (!connected) continue;
                var address = info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) ? a as string : null;
                var key = "bt:" + (string.IsNullOrWhiteSpace(address) ? info.Id : address.Replace(":", "").Replace("-", ""));
                if (!battery.HasValue && protocol == BluetoothLeProtocol && info.Pairing.IsPaired && !scanTimeout.IsCancellationRequested)
                    battery = await TryReadGattAsync(info.Id, scanTimeout.Token);
                if (!all.ContainsKey(key) || (!all[key].BatteryPercent.HasValue && battery.HasValue))
                    all[key] = new Peripheral(name, battery, true, "Bluetooth: Windows / стандартный Battery Service", key, name);
            }
        }
        return all.Values.Where(d => d.Connected).ToList();
    }

    // Only read the standard Battery Level characteristic on an already paired, connected BLE device.
    private static async Task<int?> TryReadGattAsync(string id, CancellationToken scanToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(scanToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var ct = timeout.Token;
            using var device = await BluetoothLEDevice.FromIdAsync(id).AsTask(ct);
            if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(ct);
            try
            {
                if (services.Status != GattCommunicationStatus.Success) return null;
                foreach (var service in services.Services)
                {
                    var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(ct);
                    if (chars.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in chars.Characteristics)
                    {
                        if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read)) continue;
                        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
                        if (result.Status != GattCommunicationStatus.Success || result.Value.Length != 1) continue;
                        using var reader = DataReader.FromBuffer(result.Value);
                        byte value = reader.ReadByte();
                        if (value <= 100) return value;
                    }
                }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
        }
        catch { /* Unsupported, unavailable or timed out: keep unknown. */ }
        return null;
    }

    private static int? ReadPercent(object? value)
    {
        if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong)) return null;
        try
        {
            var n = Convert.ToInt32(value);
            return n is >= 0 and <= 100 ? n : null;
        }
        catch { return null; }
    }
}

