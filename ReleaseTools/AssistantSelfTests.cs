namespace BatteryMonitor;

// Test-only localization shim. The production assembly excludes ReleaseTools.
internal static class UiPreferences
{
    internal static string T(string text) => text;
}

internal static class AssistantSelfTests
{
    internal static void Run()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Peripheral D(int? p, string key = "test-a", string name = "Test device") => new(name, p, true, "", key);
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception("Assistant self-test failed: " + message);
        }
        IReadOnlyList<AssistantNotice> Sample(BatteryAssistant a, int? p, int minutes, bool complete = true) =>
            a.Observe(new[] { D(p) }, start.AddMinutes(minutes), complete);

        var a = new BatteryAssistant();
        Check(Sample(a, 21, 0).Count == 0, "initial normal reading");
        Check(Sample(a, 20, 1).Single().Mood == DomovoiMood.BatterySign, "20% warning");
        Check(Sample(a, 19, 2).Count == 0, "no repeated warning");
        Check(Sample(a, 10, 3).Single().Mood == DomovoiMood.BatteryCriticalSign, "10% warning");
        Check(Sample(a, 5, 4).Single().Mood == DomovoiMood.BatteryEmptySign, "5% warning");
        Check(Sample(a, 0, 5).Count == 0, "no duplicate critical alert");
        Sample(a, 25, 6);
        Check(Sample(a, 20, 7).Any(n => n.Urgent), "hysteresis reset");
        Check(Sample(new BatteryAssistant(), null, 0).Single().Kind == AssistantKind.Compatibility, "unknown reading");
        Check(Sample(new BatteryAssistant(), 101, 0).Single().Kind == AssistantKind.Compatibility, "out-of-range reading");
        Check(new BatteryAssistant().Observe(new[] { D(15, name: "Logitech G435 (USB receiver)") }, start, true)
            .Single().Kind == AssistantKind.Compatibility, "G435 percentage not inferred");

        var b = new BatteryAssistant();
        Sample(b, 70, 0);
        Check(b.Observe(Array.Empty<Peripheral>(), start.AddMinutes(1), true).Count == 0, "missing once");
        Check(b.Observe(Array.Empty<Peripheral>(), start.AddMinutes(2), false).Count == 0, "failed scan is not disconnect");
        Check(b.Observe(Array.Empty<Peripheral>(), start.AddMinutes(3), true).Count == 0, "missing count reset");
        Check(b.Observe(Array.Empty<Peripheral>(), start.AddMinutes(4), true).Single().Mood == DomovoiMood.SignalLost, "missing twice");
        Check(Sample(b, 70, 5).Single().Mood == DomovoiMood.DeviceFound, "reconnect");

        var c = new BatteryAssistant();
        Sample(c, 80, 0);
        Check(Sample(c, 81, 1).Count == 0, "single rise is insufficient");
        Check(Sample(c, 82, 2).Single().Mood == DomovoiMood.ChargeRising, "rising charge");
        Check(Sample(c, 100, 3).Single().Mood == DomovoiMood.ChargeFull, "full charge");
        Check(Sample(c, 100, 4).Count == 0, "full charge deduplicated");

        var history = new BatteryAssistant();
        for (int i = 0; i < 5; i++) Sample(history, 100 - 2 * i, i * 8);
        Check(history.Estimate("test-a", start.AddMinutes(32)) != null, "sufficient discharge history");
        Sample(history, 93, 33);
        Check(history.Estimate("test-a", start.AddMinutes(33)) == null, "rise resets forecast");
        Sample(history, null, 34);
        Check(history.Estimate("test-a", start.AddMinutes(34)) == null, "unknown resets forecast");

        var pair = new BatteryAssistant();
        var notices = pair.Observe(new[] { D(20, "a"), D(5, "b") }, start, true);
        Check(notices.Count == 2 && pair.Latest?.DeviceKey == "b", "distinct devices; urgent priority");
        for (int i = 0; i < 30; i++) pair.Record(BatteryAssistant.Update("test-" + i));
        Check(pair.Recent.Count == 20, "bounded history");

        var scenes = new[] {
            new AssistantNotice("Battery Monitor", AssistantKind.Low, AssistantDestination.Devices, "", Percent:20),
            new AssistantNotice("Battery Monitor", AssistantKind.Critical, AssistantDestination.Devices, "", Percent:10),
            new AssistantNotice("Battery Monitor", AssistantKind.Critical, AssistantDestination.Devices, "", Percent:5),
            new AssistantNotice("Battery Monitor", AssistantKind.Rising, AssistantDestination.Devices, ""),
            new AssistantNotice("Battery Monitor", AssistantKind.Full, AssistantDestination.Devices, ""),
            new AssistantNotice("Battery Monitor", AssistantKind.Found, AssistantDestination.Devices, ""),
            new AssistantNotice("Battery Monitor", AssistantKind.Missing, AssistantDestination.Devices, ""),
            new AssistantNotice("Battery Monitor", AssistantKind.Compatibility, AssistantDestination.Compatibility, ""),
            BatteryAssistant.Update("1.0.0"),
            new AssistantNotice("Battery Monitor", AssistantKind.Info, AssistantDestination.Appearance, "")
        };
        Check(scenes.Select(n => n.Mood).Distinct().Count() == scenes.Length, "unique assistant scenes");
        Console.WriteLine("Assistant logic tests passed.");
    }
}
