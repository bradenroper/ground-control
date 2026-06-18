using System;

namespace MissionControl.Native;

/// <summary>A top-level window that is a candidate for the overlay grid.</summary>
public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required NativeMethods.RECT Rect { get; init; }

    public int Width => Rect.Width;
    public int Height => Rect.Height;
    public double Aspect => Height > 0 ? (double)Width / Height : 1.0;
}
