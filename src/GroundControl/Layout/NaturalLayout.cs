using System;

namespace GroundControl.Layout;

/// <summary>
/// "Natural" declumping layout, in the spirit of KDE's Present Windows effect.
/// Each window starts at its real on-screen position; overlapping windows are pushed
/// apart until none overlap; finally the whole arrangement is uniformly scaled to fit
/// the work area.
///
/// Separation happens along the line joining each overlapping pair's centers, so windows
/// fan out into two dimensions instead of collapsing onto a single axis. (The older
/// "axis of least penetration" rule always separated wide, stacked windows vertically —
/// their vertical overlap is smaller than their horizontal overlap — which piled
/// everything into a column down the center.) A tiny outward seed from the screen center
/// gives concentric windows a well-defined direction to spread toward their nearest
/// corner, so the layout uses the full width of the screen.
///
/// Because positions and sizes are only ever translated/uniformly-scaled, windows keep
/// their relative arrangement and relative size — so they barely travel and a big window
/// stays bigger than a small one (unlike a uniform grid).
/// </summary>
public static class NaturalLayout
{
    /// <summary>Outward nudge (px) applied per window before declumping, so concentric
    /// windows fan toward their corner instead of resolving onto one axis. Negligible
    /// next to real window separations; decisive only when windows start coincident.</summary>
    private const double SeedRadius = 12.0;
    private const double Eps = 1e-6;
    private const double GoldenAngle = 2.39996322972865332; // ~137.5°, evenly spaces directions

    public static RectD[] Compute(double areaW, double areaH, RectD[] real, double margin, double spacing)
    {
        int n = real.Length;
        if (n == 0) return Array.Empty<RectD>();
        if (n == 1) return new[] { FitCentered(real[0], areaW, areaH, margin) };

        var t = (RectD[])real.Clone();

        double cx = areaW / 2.0, cy = areaH / 2.0;

        // ---- Seed a small outward fan from the screen center. ----
        // A window that already sits off-center is nudged further toward its own corner
        // (the "outermost corner from the center" metric); windows stacked dead-center
        // are fanned by an even golden-angle spread so they don't share a direction.
        for (int i = 0; i < n; i++)
        {
            double dx = t[i].CenterX - cx, dy = t[i].CenterY - cy;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double ux, uy;
            if (len > 1.0) { ux = dx / len; uy = dy / len; }
            else { ux = Math.Cos(i * GoldenAngle); uy = Math.Sin(i * GoldenAngle); }
            t[i] = t[i] with { X = t[i].X + ux * SeedRadius, Y = t[i].Y + uy * SeedRadius };
        }

        // ---- Push overlapping windows apart along the line between their centers. ----
        const int maxIterations = 400;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool moved = false;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double overlapX = Math.Min(t[i].Right, t[j].Right) - Math.Max(t[i].X, t[j].X) + spacing;
                double overlapY = Math.Min(t[i].Bottom, t[j].Bottom) - Math.Max(t[i].Y, t[j].Y) + spacing;
                if (overlapX <= 0 || overlapY <= 0) continue;

                moved = true;

                // Direction = from j's center toward i's center, so the pair separates
                // along the way they already lean. Coincident centers fall back to an
                // outward-from-screen-center direction (then to +X as a last resort).
                double dx = t[i].CenterX - t[j].CenterX;
                double dy = t[i].CenterY - t[j].CenterY;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < Eps)
                {
                    dx = t[i].CenterX - cx; dy = t[i].CenterY - cy;
                    len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < Eps) { dx = 1; dy = 0; len = 1; }
                }
                double ux = dx / len, uy = dy / len;

                // Minimum distance to slide along (ux,uy) so the boxes clear on whichever
                // axis frees first. Reduces to the old per-axis push when (ux,uy) is a unit axis.
                double sX = Math.Abs(ux) > Eps ? overlapX / Math.Abs(ux) : double.PositiveInfinity;
                double sY = Math.Abs(uy) > Eps ? overlapY / Math.Abs(uy) : double.PositiveInfinity;
                double half = Math.Min(sX, sY) / 2.0;

                t[i] = t[i] with { X = t[i].X + ux * half, Y = t[i].Y + uy * half };
                t[j] = t[j] with { X = t[j].X - ux * half, Y = t[j].Y - uy * half };
            }
            if (!moved) break;
        }

        // ---- Uniformly scale the bounding box to fit the area (never enlarge past 1.0). ----
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var r in t)
        {
            minX = Math.Min(minX, r.X);
            minY = Math.Min(minY, r.Y);
            maxX = Math.Max(maxX, r.Right);
            maxY = Math.Max(maxY, r.Bottom);
        }

        double boundsW = Math.Max(1, maxX - minX);
        double boundsH = Math.Max(1, maxY - minY);
        double availW = Math.Max(1, areaW - 2 * margin);
        double availH = Math.Max(1, areaH - 2 * margin);

        double scale = Math.Min(Math.Min(availW / boundsW, availH / boundsH), 1.0);
        if (!(scale > 0) || double.IsInfinity(scale)) scale = 1.0;

        double scaledW = boundsW * scale, scaledH = boundsH * scale;
        double offsetX = margin + (availW - scaledW) / 2.0;
        double offsetY = margin + (availH - scaledH) / 2.0;

        var result = new RectD[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = new RectD(
                offsetX + (t[i].X - minX) * scale,
                offsetY + (t[i].Y - minY) * scale,
                t[i].W * scale,
                t[i].H * scale);
        }
        return result;
    }

    private static RectD FitCentered(RectD r, double areaW, double areaH, double margin)
    {
        double availW = Math.Max(1, areaW - 2 * margin);
        double availH = Math.Max(1, areaH - 2 * margin);
        double scale = Math.Min(Math.Min(availW / r.W, availH / r.H), 1.0);
        double w = r.W * scale, h = r.H * scale;
        return new RectD((areaW - w) / 2.0, (areaH - h) / 2.0, w, h);
    }
}
