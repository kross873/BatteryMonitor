using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace BatteryMonitor;

internal static partial class HidUsbScanner
{
    private static int? TryReadAjazz(SafeFileHandle metadataHandle, string path, ushort featureLen, StringBuilder sb)
    {
        // On this receiver family a passive GET_FEATURE 0x05 returns 05 00 00 00...
        // until the dock is prompted to refresh its wireless link with a 0xF7 poll.
        // Documented source: Aiacos/ajazz-control-center/docs/protocols/mouse/aj_series_battery.md
        // Poll is sent *only* to the exact control collection on VID 3151 / PID 5007.
        if (featureLen < 8 || featureLen > 256)
        {
            sb.AppendLine("AJAZZ: unexpected feature report length; did not send poll.");
            return null;
        }

        var pollOk = false;
        var pollError = 0;
        var poll = new byte[featureLen];
        poll[0] = 0x00; // unnumbered report
        poll[1] = 0xF7; // telemetry refresh, zero payload; NOT configuration

        // Acquire an interface handle with permission to issue the documented poll.
        // If another app owns it exclusively, don't disturb anything: report unknown.
        using (var controlHandle = CreateFileW(path, GenericRead | GenericWrite,
                   FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
        {
            if (!controlHandle.IsInvalid)
            {
                pollOk = HidD_SetFeature(controlHandle, poll, poll.Length);
                pollError = pollOk ? 0 : Marshal.GetLastWin32Error();
            }
            else pollError = Marshal.GetLastWin32Error();
        }
        if (!pollOk)
        {
            // Some Windows HID interfaces grant write-only access but not read+write.
            using var writeHandle = CreateFileW(path, GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (!writeHandle.IsInvalid)
            {
                pollOk = HidD_SetFeature(writeHandle, poll, poll.Length);
                pollError = pollOk ? 0 : Marshal.GetLastWin32Error();
            }
            else pollError = Marshal.GetLastWin32Error();
        }
        if (!pollOk)
        {
            sb.AppendLine($"AJAZZ F7 status poll: unavailable (Windows error {pollError}).");
            sb.AppendLine("AJAZZ: battery left unknown; no other commands attempted.");
            return null;
        }
        sb.AppendLine("AJAZZ F7 status poll: sent successfully.");

        // Allow the 2.4GHz dock to refresh its telemetry (vendor reference: ~30ms).
        Thread.Sleep(45);
        var frame = new byte[featureLen];
        frame[0] = 0x05;
        var ok = HidD_GetFeature(metadataHandle, frame, frame.Length);
        var error = ok ? 0 : Marshal.GetLastWin32Error();
        if (!ok)
        {
            using var readHandle = CreateFileW(path, GenericRead,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (!readHandle.IsInvalid)
            {
                frame = new byte[featureLen];
                frame[0] = 0x05;
                ok = HidD_GetFeature(readHandle, frame, frame.Length);
                error = ok ? 0 : Marshal.GetLastWin32Error();
            }
            else error = Marshal.GetLastWin32Error();
        }
        if (!ok)
        {
            sb.AppendLine($"AJAZZ GET_FEATURE 0x05: unavailable (Windows error {error}).");
            return null;
        }
        sb.AppendLine($"AJAZZ GET_FEATURE 0x05: header={Convert.ToHexString(frame.AsSpan(0, Math.Min(frame.Length, 8)))}");
        // Windows: 05 00 00 <charge> <flags...>. Reject link-not-ready and reconnect garbage.
        if (frame[0] != 0x05 || frame[1] != 0 || frame[2] != 0 || frame[3] is < 1 or > 100)
        {
            sb.AppendLine("AJAZZ: invalid/unready telemetry frame; battery unknown.");
            return null;
        }
        sb.AppendLine($"AJAZZ: battery = {frame[3]}% (reported by USB receiver).");
        return frame[3];
    }

}
