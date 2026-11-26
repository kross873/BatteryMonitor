using static BatteryMonitor.UiPreferences;
using Microsoft.Win32;

namespace BatteryMonitor;

internal enum AssistantKind { Low, Critical, Full, Rising, Found, Missing, Update, Compatibility, Info }
internal enum AssistantDestination { Devices, Appearance, Updates, Compatibility }

// Module/destination contract: a future suite can route notices to other modules.
internal sealed record AssistantNotice(string Module, AssistantKind Kind, AssistantDestination Destination,
    string Message, string? DeviceKey = null, int? Percent = null)
{
    internal bool Urgent => Kind is AssistantKind.Low or AssistantKind.Critical;
    internal DomovoiMood Mood => Kind switch
    {
        AssistantKind.Low or AssistantKind.Critical when Percent is <= 5 => DomovoiMood.BatteryEmptySign,
        AssistantKind.Low or AssistantKind.Critical when Percent is <= 10 => DomovoiMood.BatteryCriticalSign,
        AssistantKind.Low or AssistantKind.Critical when Percent.HasValue => DomovoiMood.BatterySign,
        AssistantKind.Low or AssistantKind.Critical => DomovoiMood.Worried,
        AssistantKind.Full => DomovoiMood.ChargeFull,
        AssistantKind.Rising => DomovoiMood.ChargeRising,
        AssistantKind.Found => DomovoiMood.DeviceFound,
        AssistantKind.Missing => DomovoiMood.SignalLost,
        AssistantKind.Update => DomovoiMood.UpdateDelivery,
        AssistantKind.Compatibility => DomovoiMood.CompatibilityInspect,
        AssistantKind.Info when Destination == AssistantDestination.Appearance => DomovoiMood.WidgetTune,
        _ => DomovoiMood.Guide
    };
}

internal static class AssistantPreferences
{
    private const string Key = @"Software\BatteryMonitor\Domovoi";
    internal static bool Read(string name)
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(name) is int n && n != 0; }
        catch { return false; }
    }
    internal static void Write(string name, bool value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key) ?? throw new InvalidOperationException("Cannot save assistant preferences.");
        k.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
    internal static bool Quiet => Read("AssistantQuiet");
    internal static bool Desktop => Read("AssistantDesktop");
}

// Receives completed snapshots only. Never polls devices itself or invents battery values.
// History is bounded, in memory, and invalidated by missing/unknown measurements.
internal sealed class BatteryAssistant
{
    private sealed class DeviceState
    {
        internal Peripheral Device;
        internal int Missing, AlertBand, RisingStreak;
        internal int? Previous;
        internal bool RiseReported, FullReported, UnknownReported;
        internal DateTimeOffset LastRiseNotice = DateTimeOffset.MinValue;
        internal List<(DateTimeOffset Time, int Value)> Samples = new();
        internal DeviceState(Peripheral device) { Device = device; }
    }
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.Ordinal);
    private bool _initialized;
    private readonly List<AssistantNotice> _recent = new();
    internal IReadOnlyList<AssistantNotice> Recent => _recent;
    internal AssistantNotice? Latest => _recent.LastOrDefault();
    internal void Record(AssistantNotice notice)
    {
        _recent.Add(notice);
        if (_recent.Count > 20) _recent.RemoveAt(0);
    }
    internal static AssistantNotice Update(string version) => new("Battery Monitor", AssistantKind.Update,
        AssistantDestination.Updates, T("Доступна версия:") + " " + version);

    private static string Name(Peripheral d) => d.Name.StartsWith("AJAZZ wireless mouse", StringComparison.Ordinal) ? "AJAZZ AJ179 APEX" : T(d.Name);
    private static int? Valid(Peripheral d) => d.Connected && d.BatteryPercent is >= 0 and <= 100 &&
        !d.Name.Contains("G435", StringComparison.OrdinalIgnoreCase) ? d.BatteryPercent : null;

    internal void InvalidateHistory()
    {
        foreach (var state in _states.Values)
        {
            state.Samples.Clear(); state.Previous = null; state.RisingStreak = 0; state.Missing = 0;
        }
    }

    internal IReadOnlyList<AssistantNotice> Observe(IReadOnlyList<Peripheral> snapshot, DateTimeOffset now, bool complete)
    {
        if (!complete) { InvalidateHistory(); return Array.Empty<AssistantNotice>(); }
        var events = new List<AssistantNotice>();
        var current = snapshot.Where(d => d.Connected)
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(d => Valid(d).HasValue).First()).ToDictionary(d => d.Key, StringComparer.Ordinal);
        foreach (var key in _states.Keys.ToArray())
        {
            if (current.ContainsKey(key)) continue;
            var old = _states[key];
            old.Samples.Clear(); old.Previous = null; old.RisingStreak = 0;
            if (++old.Missing < 2) continue;
            events.Add(new("Battery Monitor", AssistantKind.Missing, AssistantDestination.Devices,
                Name(old.Device) + ": " + T("Нет сигнала в двух проверках."), key));
            _states.Remove(key);
        }
        foreach (var d in current.Values)
        {
            if (!_states.TryGetValue(d.Key, out var state))
            {
                if (_states.Count >= 256) continue;
                _states.Add(d.Key, state = new DeviceState(d));
                if (_initialized) events.Add(new("Battery Monitor", AssistantKind.Found, AssistantDestination.Devices,
                    Name(d) + ": " + T("Устройство обнаружено."), d.Key));
            }
            state.Device = d; state.Missing = 0;
            int? value = Valid(d);
            if (!value.HasValue)
            {
                state.Samples.Clear(); state.Previous = null; state.RisingStreak = 0;
                if (!state.UnknownReported)
                {
                    state.UnknownReported = true;
                    events.Add(new("Battery Monitor", AssistantKind.Compatibility, AssistantDestination.Compatibility,
                        Name(d) + ": " + T("Заряд недоступен. Проверьте совместимость."), d.Key));
                }
                continue;
            }
            int p = value.Value;
            state.UnknownReported = false;
            if (p <= 95) state.FullReported = false;
            int band = p <= 5 ? 3 : p <= 10 ? 2 : p <= 20 ? 1 : 0;
            // Hysteresis: do not repeat a warning for a 19/21% fluctuation.
            if (p >= 25) state.AlertBand = 0;
            if (band > state.AlertBand)
            {
                state.AlertBand = band;
                events.Add(new("Battery Monitor", band >= 2 ? AssistantKind.Critical : AssistantKind.Low,
                    AssistantDestination.Devices, Name(d) + " — " + p + "%. " +
                    T(band == 3 ? "Заряд почти исчерпан!" : band == 2 ? "Критически низкий заряд!" : "Пора зарядить устройство."), d.Key, p));
            }
            if (state.Previous is int previous)
            {
                if (p > previous)
                {
                    state.RisingStreak++;
                    state.Samples.Clear();
                    if (p == 100 && !state.FullReported)
                    {
                        events.Add(new("Battery Monitor", AssistantKind.Full, AssistantDestination.Devices,
                            Name(d) + ": " + T("Показание достигло 100%."), d.Key, p));
                        state.FullReported = true; state.RiseReported = true; state.LastRiseNotice = now;
                    }
                    else if (p < 100 && state.RisingStreak >= 2 && !state.RiseReported && now - state.LastRiseNotice >= TimeSpan.FromMinutes(30))
                    {
                        events.Add(new("Battery Monitor", AssistantKind.Rising, AssistantDestination.Devices,
                            Name(d) + ": " + T("Заряд по показаниям растёт."), d.Key, p));
                        state.RiseReported = true; state.LastRiseNotice = now;
                    }
                }
                else if (p < previous) { state.RisingStreak = 0; state.RiseReported = false; }
            }
            state.Previous = p;
            var samples = state.Samples;
            if (samples.Count > 0 && (now < samples[^1].Time || now - samples[^1].Time > TimeSpan.FromMinutes(15))) samples.Clear();
            samples.RemoveAll(s => now - s.Time > TimeSpan.FromHours(6));
            if (samples.Count == 0 || now - samples[^1].Time >= TimeSpan.FromMinutes(2) || samples[^1].Value != p)
                samples.Add((now, p));
            if (samples.Count > 256) samples.RemoveAt(0);
        }
        _initialized = true;
        // Low-charge alerts are most important; newest visible notice is the highest priority.
        foreach (var e in events.OrderBy(e => e.Urgent ? 1 : 0).ThenByDescending(e => e.Percent ?? 100)) Record(e);
        return events;
    }

    internal string? Estimate(string key, DateTimeOffset now)
    {
        if (!_states.TryGetValue(key, out var state) || state.Missing != 0 || state.Previous is not int p || p == 0) return null;
        var s = state.Samples;
        if (s.Count < 5 || now - s[^1].Time > TimeSpan.FromMinutes(15)) return null;
        double hours = (s[^1].Time - s[0].Time).TotalHours;
        int drop = s[0].Value - s[^1].Value;
        if (hours < .5 || drop < 5 || s.Zip(s.Skip(1), (a,b) => b.Value > a.Value).Any(rise => rise)) return null;
        double remaining = p * hours / drop;
        if (!double.IsFinite(remaining) || remaining < 0 || remaining > 168) return null;
        string result = T("Оценка при текущем расходе:") + " ~" + remaining.ToString("0.0") + " " + T("ч.");
        // Compare two non-overlapping portions of the same uninterrupted discharge.
        int mid = s.Count / 2;
        double oldHours = (s[mid - 1].Time - s[0].Time).TotalHours;
        double newHours = (s[^1].Time - s[mid].Time).TotalHours;
        int oldDrop = s[0].Value - s[mid - 1].Value;
        int newDrop = s[mid].Value - s[^1].Value;
        if (oldHours >= .5 && newHours >= .5 && oldDrop >= 5 && newDrop >= 5 &&
            newDrop / newHours >= 2 * oldDrop / oldHours)
            result += " · " + T("Расход вырос относительно начала наблюдения.");
        return result;
    }
    internal string Summary(IReadOnlyList<Peripheral> devices)
    {
        var lines = new List<string> { "Battery Monitor", T("Заряд устройств и прогноз"), "" };
        foreach (var d in devices.Where(d => d.Connected).Take(30))
        {
            lines.Add(Name(d) + ": " + (Valid(d) is int p ? p + "%" : T("Недоступен")));
            if (Estimate(d.Key, DateTimeOffset.Now) is string forecast) lines.Add("  " + forecast);
        }
        if (!devices.Any(d => d.Connected)) lines.Add(T("Нет данных об устройствах"));
        lines.Add(""); lines.Add(T("Прогноз приблизительный; история только за текущий запуск."));
        lines.Add(""); lines.Add(T("Последние события:"));
        foreach (var item in _recent.TakeLast(8).Reverse()) lines.Add("• " + item.Message);
        return string.Join(Environment.NewLine, lines);
    }
}
