using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Plots;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Right-edge level label shared by the level-line indicators
/// (CamarillaPivots, FibonacciPivots, DynamicLevels, MarginZones, MaxLevels,
/// MurrayMath): draws <paramref name="text"/> right-aligned against the plot
/// area's right edge, vertically centred on <paramref name="price"/>.
/// (Formerly <c>Pivots.DrawLabel</c>; the helper survived the removal of the
/// generic Pivots indicator.)
/// </summary>
internal static class LevelLabel
{
    internal static void Draw(IChartRenderer renderer,
                              ChartViewport vp,
                              PlotAreaRect area,
                              string text, double price, ChartColor color, double sizePx)
    {
        double y = vp.PriceToPixelY(price);
        var (w, h) = renderer.MeasureText(text, sizePx);
        renderer.DrawText(area.Right - w - 4, y - h / 2, text, color, sizePx);
    }
}
