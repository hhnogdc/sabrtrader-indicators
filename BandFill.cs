using System.Collections.Generic;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Shared band-fill renderer: shades the vertical ribbon between an
/// <c>upper</c> and <c>lower</c> series across the visible bars. Used by every
/// band/channel overlay (VWAP σ bands, Anchored VWAP bands) so the fill geometry
/// — including the NaN-break handling and the polygon winding — lives in exactly
/// one place. <see cref="double.NaN"/> on either edge (warm-up / out-of-session /
/// disabled band) breaks the ribbon into separate polygons so the fill shows a
/// clean gap, mirroring how the line plots break on NaN.
/// </summary>
internal static class BandFill
{
    public static ChartColor WithAlpha(ChartColor c, byte a) => new(c.R, c.G, c.B, a);

    /// <summary>
    /// Fill between <paramref name="upper"/> and <paramref name="lower"/> over the
    /// viewport's visible bars. Both series are indexed newest-first (barsAgo) and
    /// must be aligned to <paramref name="total"/> (the plot's bar count). The fill
    /// edges pass through exactly the same price→pixel mapping the band LINES use, so
    /// the fill reaches the band lines with no gap.
    /// </summary>
    public static void FillBetween(
        IChartRenderer renderer,
        ChartViewport vp,
        ChunkedSeries<double> upper,
        ChunkedSeries<double> lower,
        int total,
        ChartColor color)
    {
        int first = System.Math.Max(0, vp.FirstVisibleBarIdx);
        int last = System.Math.Min(total - 1, vp.LastVisibleBarIdx);
        if (first >= last) return;

        var topPath = new List<(double X, double Y)>();
        var botPath = new List<(double X, double Y)>();
        for (int i = first; i <= last; i++)
        {
            int barsAgo = total - 1 - i;
            double u = upper[barsAgo];
            double l = lower[barsAgo];
            if (double.IsNaN(u) || double.IsNaN(l))
            {
                Flush(renderer, topPath, botPath, color);
                continue;
            }
            double x = vp.BarIndexToPixelX(i);
            topPath.Add((x, vp.PriceToPixelY(u)));
            botPath.Add((x, vp.PriceToPixelY(l)));
        }
        Flush(renderer, topPath, botPath, color);
    }

    private static void Flush(
        IChartRenderer renderer,
        List<(double X, double Y)> topPath,
        List<(double X, double Y)> botPath,
        ChartColor color)
    {
        if (topPath.Count >= 2)
        {
            // Polygon winds in one direction: top edge left→right, bottom edge right→left.
            int n = topPath.Count;
            var poly = new (double X, double Y)[n * 2];
            for (int i = 0; i < n; i++) poly[i] = topPath[i];
            for (int i = 0; i < n; i++) poly[n + i] = botPath[n - 1 - i];
            renderer.FillPolygon(poly, color);
        }
        topPath.Clear();
        botPath.Clear();
    }
}
