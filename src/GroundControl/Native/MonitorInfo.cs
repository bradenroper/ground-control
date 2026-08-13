using System;

namespace GroundControl.Native;

/// <summary>A physical display: bounds are in virtual-desktop physical pixels.</summary>
public sealed class MonitorInfo
{
    public required IntPtr Handle { get; init; }
    public required NativeMethods.RECT Bounds { get; init; }
    public required bool IsPrimary { get; init; }
    public required double DpiScale { get; init; }   // 1.0 == 96 DPI / 100%
}
