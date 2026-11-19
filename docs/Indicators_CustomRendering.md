Indicators
Custom rendering INTERFACE
When plots are not enough (footprint cells, volume-profile bars, zone shading, text chips, on-chart buttons), paint directly on the chart with an immediate-mode renderer.

On this page
When you need it
OnCustomRender
The render context
The renderer
Z-order & replacing the candles
Optional renderer capabilities
Other render & interaction hooks
When you need it
Plots cover the common case: one value per bar, drawn as a line or histogram. Custom rendering covers the rest: per-cell footprint grids, volume-profile columns, session shading, structural zones, text chips, countdown timers. You add it by implementing IChartCustomRender next to your normal indicator code. The lifecycle (OnInit, OnBarUpdate and so on) still runs, and the chart adds your paint callback to its render pass.

OnCustomRender
OnCustomRender(IIndicatorRenderContext ctx) is called once per frame on the UI thread. Read the pixel transforms and the renderer off the context and draw. This complete example shades the regular session behind the candles.

SessionShade.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;

[IndicatorDescription("Shades the regular-session region of each day behind the candles.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class SessionShade : IndicatorBase, IChartCustomRender
{
    private static readonly TimeSpan SessionOpen  = new(9, 30, 0);
    private static readonly TimeSpan SessionClose = new(16, 0, 0);

    [IndicatorParameter(DisplayName = "Shade", Group = "Display")]
    public ChartColor Shade { get; set; } = new(33, 150, 243, 24);

    public SessionShade(IIndicator? parent = null) : base(parent) { }

    // Paint behind the bars so the candles draw on top of the shading.
    public CustomRenderLayer Layer => CustomRenderLayer.BelowBars;

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var bars = ctx.Bars;
        if (bars is null) return;

        var vp   = ctx.Viewport;
        var area = ctx.PlotArea;
        ctx.Renderer.PushClip(area.X, area.Y, area.Width, area.Height);
        try
        {
            // Viewport indexes are absolute (0 = oldest bar); series reads are bars-ago.
            for (int i = vp.FirstVisibleBarIdx; i <= vp.LastVisibleBarIdx; i++)
            {
                if (!bars.TryGet(bars.Count - 1 - i, out var bar)) continue;

                // Bar.Start is already on the chart's display timeline.
                var t = bar.Start.TimeOfDay;
                if (t < SessionOpen || t >= SessionClose) continue;

                double cx = vp.BarIndexToPixelX(i);
                double w  = vp.BarSpacingPx;
                ctx.Renderer.FillRect(cx - w / 2, area.Y, w, area.Height, Shade);
            }
        }
        finally { ctx.Renderer.PopClip(); }
    }
}
This is a hot path. OnCustomRender runs up to about 60 times a second. Keep it allocation-free: precompute heavy state in OnBarUpdate and only read it here. Clip to ctx.PlotArea so your graphics stay out of the axis strips. The chart catches and logs an exception thrown from a render callback without breaking the frame.
The render context
IIndicatorRenderContext is created for each frame. Do not keep it past the callback.

Member	What it gives you
Renderer	The IChartRenderer: your drawing surface.
Viewport	A ChartViewport with PriceToPixelY / BarIndexToPixelX / BarPositionToPixelX (and the inverses PixelYToPrice / PixelXToBarIndex / PixelXToBarPosition), BarSpacingPx, and the visible bars FirstVisibleBarIdx to LastVisibleBarIdx. Bar indexes are absolute: 0 is the oldest loaded bar. The x axis is measured in slots one bar wide. With even spacing every bar has its own slot next to the previous one. When the chart's Bar spacing places intraday time bars by time, a second or minute without a trade keeps an empty slot, so neighbouring bars can be several slots apart. Place anything that belongs to a bar or a time with BarIndexToPixelX / BarPositionToPixelX, never from BarSpacingPx and FirstVisibleBarIdx yourself: those mappings know the slots and carry the chart's sub-bar glide (BarOffsetPx), so your drawing moves with the bars. BarSpacingPx is the width of one bar. To fill a background behind a bar, use GetBarCellX: it reaches to the next bar, so the empty slots after the bar are covered too. TryGetBarAtPixelX tells which bar is under a pixel and is false over an empty slot. Place screen content (a label at the plot's edge, a notice) from PlotArea; it stays still.
PlotArea	A PlotAreaRect (X, Y, Width, Height, plus Right / Bottom) for the pane, excluding the axis strips.
Bars	The primary bar series the chart is rendering. Null on a tab with no data.
Now	Wall-clock time at frame start, on the same timeline as the bar timestamps (Bar.End). Use it instead of DateTime.UtcNow for countdowns.
ContentTopPx / HeaderInsetPx	HeaderInsetPx is the height reserved at the top of the panel by the indicator legend; ContentTopPx is PlotArea.Y + HeaderInsetPx. Anchor top-aligned content (tables, status boxes) to ContentTopPx.
DisplayTimeZone	The chart's display zone, or null for UTC. Bars are already stamped; use Bar.Start / Bar.End for time-of-day. Convert an external UTC instant with ChartDisplayTime.FromUtc(utc, DisplayTimeZone).
SizeMultiplier	Coins per one raw volume unit for the chart's instrument, or null when its sizes are already whole (futures, equities, contract-quoted perps). Bar.Volume and Tick.Size are longs, so crypto sizes travel scaled to integers: multiply a raw volume by this before printing it, or a 0.42-BTC profile bin reads "42.0M".
Drawings	The user's drawing tools across all panels (filter by IDrawingTool.PanelId). Read-only: inspect anchors and tool kinds, but do not modify them.
BeginScope(category, detail)	Opens a nested performance scope (dispose it when done) so a complex renderer shows named sub-entries in the Task Manager.
The renderer
IChartRenderer is a platform-neutral 2D surface. Coordinates are pixels (origin top-left, X to the right, Y down); the viewport converts bars and prices to pixels. The host brackets every frame with BeginFrame / EndFrame; you only call the drawing primitives:

IChartRenderer primitives (excerpt)
void DrawLine(double x0, double y0, double x1, double y1, ChartColor color, double widthPx);
void DrawLine(double x0, double y0, double x1, double y1, ChartColor color, double widthPx, LineStyle style);
void FillRect(double x, double y, double widthPx, double heightPx, ChartColor color);
void FillPolygon(ReadOnlySpan<(double X, double Y)> points, ChartColor color);
void DrawRoundedRectOutline(double x, double y, double widthPx, double heightPx, double radiusPx, ChartColor color, double strokeWidthPx);
void DrawCandle(double centerX, double bodyTopY, double bodyBottomY, double wickTopY, double wickBottomY,
                double bodyWidthPx, bool bullish, CandleStyle style);
void DrawText(double x, double y, string text, ChartColor color, double sizePx,
              string? fontFamily = null, bool bold = false, bool italic = false);
void DrawTextCentered(double centerX, double baselineY, string text, ChartColor color, double sizePx, ...);
void DrawTextRotated(double x, double y, double angleDegrees, string text, ChartColor color, double sizePx, ...);
(double WidthPx, double HeightPx) MeasureText(string text, double sizePx, string? fontFamily = null, ...);
double FontAscentPx(double sizePx, string? fontFamily = null, bool bold = false, bool italic = false);
void PushClip(double x, double y, double widthPx, double heightPx);
void PopClip();
FillPolygon takes a span, so stack-allocate small polygons to avoid per-frame allocations. Pair PushClip / PopClip 1:1; nested clips intersect. To draw a dashed or dotted segment from a PlotStyle, use CustomLineRenderer.DrawSegment. For one long line (a level, a pivot) whose style comes from a LineStyle setting or a PlotDashPattern, use CustomLineRenderer.DrawStyledLine: CustomLineRenderer.DrawStyledLine(ctx.Renderer, x0, y, x1, y, color, 1.5, LineStyle.DashDot). Both stroke the same patterns as the chart's standard plot pass, so the style a user picks in the settings dialog looks the same in your custom pass. The full surface, including the style types (CandleStyle and others), is in the plots & rendering reference.

Z-order & replacing the candles
IChartCustomRender.Layer (a CustomRenderLayer) chooses where your paint sits. BelowBars draws before the candles, so the candles cover it (backdrops, shading, profiles). AboveBars, the default, draws on top (markers, labels). To take over bar drawing entirely (custom-coloured candles, heatmap cells), return true from SuppressDefaultBarRendering: the chart skips its default bar pass for that frame and you own the candles. The chart reads it every frame, so you can tie it to a user setting. When any active renderer returns true the bar pass is skipped.

An overlay that sits against the price axis (a volume profile docked on the right, a DOM-style ladder) returns its width in pixels from ReservedRightPx. The chart reserves the widest value on top of its own right margin, so candles and plots end at the overlay's left edge and keep the chart's price scale. All overlays share that one strip, sized to the widest request. The default 0 paints over the bars. The chart reads it once per frame on the UI thread, so return a cached number.

docking against the price axis
[IndicatorParameter(DisplayName = "Profile width (px)", Group = "Display", MinValue = 40, MaxValue = 400)]
public int ProfileWidthPx { get; set; } = 120;

public int ReservedRightPx => ProfileWidthPx;   // candles stop where the profile begins
Only recolouring candles? Implement IBarColorSource (see below) instead of suppressing and redrawing. It keeps the chart's fast candle path and asks you for an override colour per visible bar.
Optional renderer capabilities
Beyond the core primitives, a renderer can implement richer capability interfaces. Cast and check at runtime: the chart's renderer implements all of them, but another renderer (a test recorder, for example) may implement only the core.

capability cast
using SabrTrader.Pipeline.Charts.Rendering;

if (ctx.Renderer is IGradientRenderer gradient)
    gradient.FillLinearGradient(poly, startX, startY, startColor, endX, endY, endColor);
else
    ctx.Renderer.FillPolygon(poly, fallbackColor);   // solid fallback
Interface	Adds
IGradientRenderer	Linear-gradient-filled polygons (heatmap cells, fades).
IPolylineRenderer	One stroked open polyline through many points, solid or dashed (cheaper than N DrawLine calls).
IEllipseRenderer	Filled axis-aligned ellipses and circles.
ISphereRenderer	Shaded 3D-ball circles and their soft shadows (bubble glyphs).
IPaintedCandleRenderer	Draws one candle in an override colour (paint bars).
IHeatmapRasterRenderer	Blits an ARGB pixel buffer (or a region of it) onto the canvas, for dense heatmaps.
Namespace. IChartRenderer and the capability interfaces are in SabrTrader.Pipeline.Charts.Rendering; add using SabrTrader.Pipeline.Charts.Rendering; when you cast to one. ChartViewport is in SabrTrader.Pipeline.Charts.Viewport.
Other render & interaction hooks
An indicator can implement any combination of these companions to IChartCustomRender. All of them are called on the UI thread, so they can share plain fields with OnCustomRender.

Interface	What it does
IChartBackgroundRender	OnRenderBackground(ctx): an extra below-bars pass, for an indicator that paints both a backdrop and a foreground layer. It always runs below the bars, whatever Layer says.
IPricePanelOverlayRender	OnRenderPricePanel(ctx): paints on the main price panel, even from a subpanel indicator (Buy/Sell markers on the candles). The context's viewport maps prices on the price panel. PricePanelLayer (default AboveBars) sets its z-order.
IBarColorSource	PaintBarsEnabled + TryGetBarColor(barIndex, in bar, out color): recolours candles per bar on the fast path. barIndex is absolute (0 = oldest). When several sources are active, the last registered source that returns true wins.
IChartClickReceiver	OnChartClick(ChartClickContext ctx): receives a left-click in the plot area (X, Y in canvas pixels). Return true to consume it, for on-chart buttons.
IChartMouseMoveReceiver	OnChartMouseMove(ChartMouseMoveContext ctx): hover-driven decorations. ctx.IsInside is false (NaN position, ChartMouseMoveContext.Outside) when the mouse leaves the chart.
To paint in response to a tick or a timer, request a repaint and let the chart coalesce the requests. That is the next page.

