using System;
using System.Linq;

namespace MissionControl.Layout;

public readonly record struct RectD(double X, double Y, double W, double H);

/// <summary>
/// Packs N windows into a region as an aspect-preserving grid, choosing the column
/// count that maximizes total thumbnail area (an Exposé-style layout). Returns the
/// grid cell for each item; <see cref="FitAspect"/> then letterboxes the window
/// inside its cell.
/// </summary>
public static class GridLayout
{
    public static (RectD[] cells, int cols) Compute(double regionW, double regionH, double[] aspects, double gap)
    {
        int n = aspects.Length;
        if (n == 0) return (Array.Empty<RectD>(), 1);

        int bestCols = 1;
        double bestScore = double.NegativeInfinity;

        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (int)Math.Ceiling((double)n / cols);
            double cellW = (regionW - gap * (cols + 1)) / cols;
            double cellH = (regionH - gap * (rows + 1)) / rows;
            if (cellW <= 1 || cellH <= 1) continue;

            double score = 0;
            foreach (double a in aspects)
            {
                double w = cellW, h = w / a;
                if (h > cellH) { h = cellH; w = h * a; }
                score += w * h;
            }
            if (score > bestScore) { bestScore = score; bestCols = cols; }
        }

        int finalCols = bestCols;
        int finalRows = (int)Math.Ceiling((double)n / finalCols);
        double cw = (regionW - gap * (finalCols + 1)) / finalCols;
        double ch = (regionH - gap * (finalRows + 1)) / finalRows;

        var cells = new RectD[n];
        for (int i = 0; i < n; i++)
        {
            int r = i / finalCols;
            int c = i % finalCols;

            // Center the (possibly partial) last row horizontally.
            int itemsInRow = Math.Min(finalCols, n - r * finalCols);
            double rowWidth = itemsInRow * cw + (itemsInRow - 1) * gap;
            double xOffset = (regionW - rowWidth) / 2;

            double x = xOffset + c * (cw + gap);
            double y = gap + r * (ch + gap);
            cells[i] = new RectD(x, y, cw, ch);
        }
        return (cells, finalCols);
    }

    /// <summary>Letterboxes an aspect ratio inside a cell (centered), leaving an inset margin.</summary>
    public static RectD FitAspect(RectD cell, double aspect, double inset)
    {
        double maxW = Math.Max(1, cell.W - inset * 2);
        double maxH = Math.Max(1, cell.H - inset * 2);

        double w = maxW, h = w / aspect;
        if (h > maxH) { h = maxH; w = h * aspect; }

        double x = cell.X + (cell.W - w) / 2;
        double y = cell.Y + (cell.H - h) / 2;
        return new RectD(x, y, w, h);
    }
}
