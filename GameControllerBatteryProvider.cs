using Windows.Gaming.Input;
using Windows.System.Power;
namespace BatteryMonitor;

internal static class GameControllerBatteryProvider
{
    public static List<Peripheral> Scan()
    {
        var devices = new List<Peripheral>();
        foreach (var pad in RawGameController.RawGameControllers)
        {
            try
            {
                var report = pad.TryGetBatteryReport();
                if (report is null || report.Status == BatteryStatus.NotPresent) continue;
                if (report.FullChargeCapacityInMilliwattHours is not int full || full <= 0 ||
                    report.RemainingCapacityInMilliwattHours is not int remaining || remaining < 0 || remaining > full) continue;
                int percent = (int)Math.Round(remaining * 100.0 / full);
                var name = string.IsNullOrWhiteSpace(pad.DisplayName) ? "Геймпад" : pad.DisplayName;
                devices.Add(new Peripheral(name + " (Windows GameInput)", percent, true,
                    "Windows.Gaming.Input: remaining / full charge capacity", "pad:" + pad.NonRoamableId, name));
            }
            catch { /* No supported report on this controller. */ }
        }
        return devices;
    }
}
