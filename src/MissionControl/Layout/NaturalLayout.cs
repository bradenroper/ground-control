using System;

namespace MissionControl.Layout;

/// <summary>
/// "Natural" declumping layout, in the spirit of KDE's Present Windows effect.
/// Each window starts at its real on-screen position; overlapping windows are pushed
/// apart (minimum-translation resolution) until none overlap; finally the whole
/// arrangement is uniformly scaled to fit the work area.
///
/// Because positions and sizes are only ever translated/uniformly-scaled, windows keep
/// their relative arrangement and relative size — so they barely travel and a big window
/// stays bigger than a small one (unlike a uniform grid).
/// </summary>
public static class NaturalLayout
{
    public static RectD[] Compute(double areaW, double areaH, RectD[] real, double margin, double spacing)
    {
        int n = real.Length;
        if (n == 0) return Array.Empty<RectD>();
        if (n == 1) return new[] { FitCentered(real[0], areaW, areaH, margin) };

        var t = (RectD[])real.Clone();

        // ---- Push overlapping windows apart along the axis of least penetration. ----
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
                if (overlapX < overlapY)
                {
                    double push = overlapX / 2.0;
                    double sign = t[i].CenterX <= t[j].CenterX ? -1 : 1;
                    t[i] = t[i] with { X = t[i].X + sign * push };
                    t[j] = t[j] with { X = t[j].X - sign * push };
                }
                else
                {
                    double push = overlapY / 2.0;
                    double sign = t[i].CenterY <= t[j].CenterY ? -1 : 1;
                    t[i] = t[i] with { Y = t[i].Y + sign * push };
                    t[j] = t[j] with { Y = t[j].Y - sign * push };
                }
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
