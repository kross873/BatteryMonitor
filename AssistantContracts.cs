namespace BatteryMonitor;

// Shared snapshot and animation contracts, kept free of Windows UI dependencies
// so the release self-tests use the same definitions as the application.
internal enum DomovoiMood { Idle, Held, Walking, Dizzy, Happy, Sleepy, Worried, Petting, Wave, Dance, Stretch, Annoyed, Sitting, Tea, Coding, Perched, GitHubSign, TikTokSign, TelegramSign, BatterySign, BatteryCriticalSign, BatteryEmptySign, ChargeRising, ChargeFull, DeviceFound, SignalLost, CompatibilityInspect, UpdateDelivery, WidgetTune, Guide }

internal sealed record Peripheral(string Name, int? BatteryPercent, bool Connected, string Detail, string? Id = null, string? ShortName = null)
{
    public string Key => Id ?? Name;
    public string Label => ShortName ?? (Name.StartsWith("AJAZZ", StringComparison.OrdinalIgnoreCase) ? "AJAZZ" : Name);
}
