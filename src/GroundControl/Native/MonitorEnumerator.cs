using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GroundControl.Native;

public static class MonitorEnumerator
{
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                var bounds = rect;
                bool primary = false;
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    bounds = mi.rcMonitor;
                    primary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                }

                double scale = 1.0;
                if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                    scale = dpiX / 96.0;

                monitors.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    Bounds = bounds,
                    IsPrimary = primary,
                    DpiScale = scale
                });
                return true;
            }, IntPtr.Zero);

        return monitors;
    }
}
