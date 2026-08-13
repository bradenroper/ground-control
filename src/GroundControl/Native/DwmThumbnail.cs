using System;

namespace GroundControl.Native;

/// <summary>
/// A live, GPU-composited thumbnail of a source window, drawn into a rectangle on a
/// destination window. This is the same mechanism Alt+Tab / Task View use: it never
/// touches the source window, so there is no resize/reflow jank.
/// </summary>
public sealed class DwmThumbnail : IDisposable
{
    private IntPtr _thumb;

    public IntPtr Source { get; }
    public NativeMethods.SIZE SourceSize { get; private set; }

    private DwmThumbnail(IntPtr thumb, IntPtr source)
    {
        _thumb = thumb;
        Source = source;
    }

    /// <summary>Registers a thumbnail of <paramref name="source"/> onto <paramref name="destination"/>.</summary>
    public static DwmThumbnail? Register(IntPtr destination, IntPtr source)
    {
        if (NativeMethods.DwmRegisterThumbnail(destination, source, out var thumb) != 0)
            return null;

        var t = new DwmThumbnail(thumb, source);
        if (NativeMethods.DwmQueryThumbnailSourceSize(thumb, out var size) == 0)
            t.SourceSize = size;
        return t;
    }

    /// <summary>Positions/sizes the thumbnail. Coordinates are physical pixels relative to the destination window's client area.</summary>
    public void SetDestination(int left, int top, int right, int bottom, byte opacity = 255)
    {
        if (_thumb == IntPtr.Zero) return;

        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION
                    | NativeMethods.DWM_TNP_VISIBLE
                    | NativeMethods.DWM_TNP_OPACITY,
            rcDestination = new NativeMethods.RECT(left, top, right, bottom),
            opacity = opacity,
            fVisible = 1,
            fSourceClientAreaOnly = 0
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    public void Dispose()
    {
        if (_thumb != IntPtr.Zero)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }
    }
}
