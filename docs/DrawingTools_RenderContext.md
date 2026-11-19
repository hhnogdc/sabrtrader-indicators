Drawing tools
The render context
The single object passed to both OnRender and HitTest that turns bar-timeline anchors into screen pixels, so your tool code never does the projection maths itself.

What it is
Both abstract methods receive an IDrawingToolRenderContext. It bundles everything a tool needs for one frame: the renderer, the viewport transforms, the plot-area rectangle, the current selection flag, and the time→pixel conversions that depend on the live bar timeline (which the chart owns, not the tool). All the plumbing lives here so your OnRender and HitTest stay almost trivial.

IDrawingToolRenderContext.cs
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.OrderFlow;
using SabrTrader.Pipeline.Plots;

namespace SabrTrader.Pipeline.Drawing;

public interface IDrawingToolRenderContext
{
    IChartRenderer Renderer { get; }          // DrawLine / FillRect / DrawText / FillPolygon / ...
    ChartViewport Viewport { get; }           // PriceToPixelY, BarIndexToPixelX, ...
    PlotAreaRect PlotArea { get; }            // pixel rect of the main candle pane
    bool IsSelected { get; }                  // true when THIS tool is selected

    double TimeToBarIndex(System.DateTime time);
    (double X, double Y) AnchorToPixel(DrawingAnchor anchor);   // the one you use most
    void DrawHandle(double x, double y, ChartColor? fill = null, double sizePx = 7);

    // Optional host-supplied data (default null / Contract; degrade gracefully when absent):
    IBarSeries? Bars => null;
    double? TickSize => null;
    double? PointValue => null;
    double? PipSize => null;
    QuantityUnit QuantityUnit => QuantityUnit.Contract;
    double? QuantityStep => null;
    IOrderFlowWindowSource? OrderFlowWindows => null;   // volume profile of a window: Open, then TryGetProfile / Paint
}
Anchors to pixels
The member you reach for most is AnchorToPixel, which projects one anchor to an (X, Y) pixel pair by combining the time→bar-index lookup with the viewport's bar→X and price→Y transforms. When only one axis matters, drop to Viewport directly: PriceToPixelY(price) gives the Y of a horizontal level. For the X of a vertical marker use ctx.AnchorToPixel(DrawingAnchor.TimeOnly(time)).X: it keeps the fractional bar position that TimeToBarIndex returns, which Viewport.BarIndexToPixelX(int) takes only as a whole bar. The viewport also exposes PixelYToPrice / PixelXToBarIndex for the inverse. To stay on the viewport with a fractional position, use Viewport.BarPositionToPixelX(double): a whole number is that bar's centre and 10.5 is halfway between the centres of bars 10 and 11. Viewport.PixelXToBarPosition(double) is its inverse and does not round to a bar.

OnRender.cs
public override void OnRender(IDrawingToolRenderContext ctx)
{
    // Two-anchor projection, both axes:
    var (x1, y1) = ctx.AnchorToPixel(Anchors[0]);
    var (x2, y2) = ctx.AnchorToPixel(Anchors[1]);
    DrawLine(ctx, x1, y1, x2, y2);   // base helper: Color, LineWidthPx, LineStyle

    // Single-axis: a horizontal level at one price across the whole pane:
    double y = ctx.Viewport.PriceToPixelY(Anchors[0].Price);
    DrawLine(ctx, ctx.PlotArea.X, y, ctx.PlotArea.Right, y);

    if (ctx.IsSelected) { ctx.DrawHandle(x1, y1); ctx.DrawHandle(x2, y2); }
}
The plot area
PlotArea is a PlotAreaRect: the pixel rectangle of the candle pane, excluding the axis strips. It exposes X, Y, Width, Height, plus convenience Right and Bottom. Use PlotArea.Right when a line should extend to the right edge of the pane (rays, Fibonacci levels), and the full rect to clamp drawing inside the panel.

The renderer
Renderer is the platform-neutral IChartRenderer drawing surface. Origin is top-left; X increases left→right, Y increases top→bottom. The primitives a tool typically uses:

Method	Draws
DrawLine(x0, y0, x1, y1, color, widthPx[, style])	A straight stroke; solid unless you pass a LineStyle. For the tool's own lines call the base's DrawLine(ctx, ...) helper, which passes the tool's LineStyle for you.
MeasureText(text, sizePx, ...)	Measures without drawing: returns (WidthPx, HeightPx). Use it for boxes and wrapping.
FillRect(x, y, w, h, color)	A filled rectangle (use alpha for translucency).
FillPolygon(points, color)	A filled, auto-closed polygon (arrows, zones).
DrawText(x, y, text, color, sizePx, ...)	Text anchored at its baseline.
DrawTextCentered(centerX, baselineY, ...)	Horizontally centred text.
PushClip(x, y, w, h) / PopClip()	Clip subsequent drawing to a rectangle (paired 1:1).
Draw handles only when selected. DrawHandle paints a small filled square (outlined by the stroke) at an anchor. Guard the calls with ctx.IsSelected so handles appear only while the tool is selected, the behaviour of every built-in tool.
Instrument-aware tools
Most tools are pure geometry and ignore the rest of the context. Tools that report tick counts or money values (measured risk/reward, position sizing) read TickSize and PointValue (a tick is worth TickSize × PointValue) and label quantities with QuantityUnit instead of assuming "contracts". Bars exposes the live IBarSeries for data-driven tools (anchored VWAP, statistical extensions). All of these default to null (or QuantityUnit.Contract) on headless / test shims, so a tool must degrade gracefully: omit a tick/money read-out rather than guess when the host has no instrument metadata.

