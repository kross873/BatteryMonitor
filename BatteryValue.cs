namespace BatteryMonitor;

// HID Usage Tables 9.2: Battery Strength is the fraction of the declared logical range.
// HidP_GetUsageValue returns an unsigned, unscaled value; sign-extend when necessary.
internal static class BatteryValue
{
    internal static int? FromLogicalRange(uint raw, int min, int max, int bits)
    {
        if (bits is < 1 or > 32 || min >= max) return null;
        long limit = 1L << bits;
        if (raw >= limit) return null;
        bool signed = min < 0;
        if (signed ? min < -limit / 2 || max >= limit / 2 : max >= limit) return null;
        long value = raw;
        if (signed && value >= limit / 2) value -= limit;
        if (value < min || value > max) return null; // Includes out-of-range HID null states.
        return (int)Math.Round((value - (long)min) * 100.0 / (max - (long)min), MidpointRounding.AwayFromZero);
    }

    internal static int? FromWindowsByte(byte value) => value <= 100 ? value : null;
}
