using System.Text.Json;

namespace BatteryMonitor;

// Logitech G435 user's own LED reading, never inferred from USB or reported as a precise percentage.
// Data is kept in local application data only, and considered stale after 12 hours.
internal sealed record G435ManualReading(string Led, DateTimeOffset CheckedAt)
{
    public bool IsFresh =>
        DateTimeOffset.Now >= CheckedAt &&
        DateTimeOffset.Now - CheckedAt < TimeSpan.FromHours(12) &&
        Led is "green" or "red" or "flashing_red";

    public string RangeLabel => Led switch
    {
        "green" => "31–100%",
        "red" => "15–30%",
        "flashing_red" => "<15%",
        _ => "неизвестно"
    };
}

internal static class G435ManualBattery
{
    private static string FileName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatteryMonitor", "g435-led-note.json");

    public static G435ManualReading? Load()
    {
        try
        {
            if (!File.Exists(FileName)) return null;
            var value = JsonSerializer.Deserialize<G435ManualReading>(File.ReadAllText(FileName));
            return value is { IsFresh: true } ? value : null;
        }
        catch (Exception) { return null; }
    }

    public static void Save(G435ManualReading reading)
    {
        var directory = Path.GetDirectoryName(FileName)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FileName, JsonSerializer.Serialize(reading));
    }

    public static void Delete()
    {
        if (File.Exists(FileName)) File.Delete(FileName);
    }
}
