using GroundControl.Layout;
using GroundControl.Native;

namespace GroundControl;

/// <summary>One window's presence in the overlay: its live thumbnail and the rects driving its animation.</summary>
public sealed class OverlayItem
{
    public required WindowInfo Window { get; init; }
    public required OverlayWindow Owner { get; init; }

    public DwmThumbnail? Thumb { get; set; }

    public RectD Start;        // monitor-local physical px — the real window footprint
    public RectD Target;       // monitor-local physical px — resting place in the layout
    public RectD Current;      // animated rect

    public RectD GlobalTarget; // virtual-desktop coords of Target, for cross-monitor spatial navigation
}
