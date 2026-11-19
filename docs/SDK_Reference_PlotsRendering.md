Reference
Plots & rendering
The plot outputs an indicator publishes (lines, OHLC candles, threshold levels), the platform-neutral colour type and its named palette, the low-level drawing surface for custom render passes, and the viewport that maps bars/prices to pixels. Namespaces: SabrTrader.Pipeline.Plots, SabrTrader.Pipeline.Indicators.Builtin (PlotColors), SabrTrader.Pipeline.Charts.Rendering and SabrTrader.Pipeline.Charts.Viewport.

On this page
IPlot · Plot · PlotStyle · PlotColors · PlotDashPattern
IOhlcPlot · OhlcPlot
IIndicatorLine · IndicatorLine
ChartColor
IChartRenderer + capability interfaces
CandleStyle
ChartPanelKey · ChartViewport
Chart trader panels
Plots (SabrTrader.Pipeline.Plots)
An indicator exposes its renderable outputs as IPlot objects (one per output column). The chart layer reads them and draws; the indicator owns the underlying series. All plot/line types are mutable so the settings dialog can recolour and restyle at runtime without reconstructing the indicator.

IPlot
One renderable output of an indicator. Values[0] is the newest value; double.NaN marks "no value at this bar" (warmup) and is skipped by the renderer.

Member	Description
string Name { get; set; }	Legend label (e.g. "SMA(20)", "Upper Band").
ISeries<double> Values { get; }	The values to render. Same indexing as ISeries<T>; NaN breaks the line / omits the dot or bar.
PlotStyle Style { get; set; }	Rendering style: line, area, histogram and so on. See PlotStyle.
ChartColor Color { get; set; }	Stroke / fill colour.
double LineWidthPx { get; set; }	Stroke width in pixels (also area-border + histogram-edge width).
bool Visible { get; set; }	When false the plot stays in the output list but the chart skips it (show/hide toggle).
bool ShowPriceMarker { get; set; }	When true the chart paints a coloured price-axis pill at the plot's most recent value. Default true.
bool IncludeInDataWindow { get; set; }	When false the plot is omitted from the chart data window. Default true (interface default).
bool IsCustomRendered { get; set; }	When true the host skips the default stroke/fill for this plot (you paint it in OnCustomRender). Default false.
bool IsUserConfigurable { get; set; }	When false the settings dialog omits this plot's row entirely. Use it for a plot the chart never paints: an internal signal or scratch series that exists only for the data box, CSV export and indicator chaining (a +1/-1 trend flag on a price panel), where colour, style and show/hide would change nothing on screen. To hide a plot that is drawn, use Visible; a custom-rendered plot stays configurable and honours Visible in its own pass. Default true (interface default).
ISeries<ChartColor>? PerBarColors { get; }	Optional per-bar stroke colours. Default null (uniform Color). Concrete Plot exposes a setter.
string? OwnerName { get; }	Friendly id of the owning indicator. Default impl returns null.
IIndicator? Owner { get; }	Back-reference to the owning indicator (renderer reads its master price-marker gate). Default impl returns null.
PlotSemantics Semantics { get; }	Machine-readable meaning of the value (overlay vs oscillator, range, unit). Default impl returns PlotSemantics.Default.
Default-implemented members. OwnerName, Owner, Semantics, IncludeInDataWindow, IsCustomRendered, IsUserConfigurable and PerBarColors have interface default impls, so a custom IPlot only has to provide the core members. IndicatorBase.AddPlot stamps the owner fields at registration time.

Plot
The default IPlot implementation (sealed class). Construct one per output column in the indicator's constructor or OnInit and add it with IndicatorBase.AddPlot(...). Setters validate eagerly: a blank name or non-positive LineWidthPx throws immediately.

Plot.cs
public Plot(string name, ISeries<double> values, PlotStyle style, ChartColor color, double lineWidthPx);

// Mutable: Name, Style, Color, LineWidthPx, Visible, ShowPriceMarker,
//          IncludeInDataWindow, IsCustomRendered, IsUserConfigurable,
//          PerBarColors, OwnerName, Owner, Semantics
// Read-only: Values  (set once in the ctor; IS the indicator's output series)
Member	Notes
string Name { get; set; }	Throws ArgumentException if set to null/whitespace.
string? OwnerName { get; set; }	Settable; AddPlot stamps owner identity.
IIndicator? Owner { get; set; }	Settable back-pointer to the owning indicator.
ISeries<double> Values { get; }	Set in the ctor; never null.
PlotStyle Style { get; set; }	Any value.
ChartColor Color { get; set; }	Any value.
ISeries<ChartColor>? PerBarColors { get; set; }	Per-bar stroke colours; null uses Color for every bar.
double LineWidthPx { get; set; }	Throws ArgumentOutOfRangeException if ≤ 0.
bool Visible { get; set; }	Defaults to true.
bool ShowPriceMarker { get; set; }	Defaults to true.
PlotSemantics Semantics { get; set; }	Defaults to PlotSemantics.Default.
PlotStyle
How a plot's values are rendered. The chart's plot render pass picks the primitive sequence from this enum.

Value	Meaning
Line	Continuous line through consecutive non-NaN values. The default.
Dashed	Dashed line: the same path as Line with a dash pattern.
Dotted	Dotted line: short dash, longer gap.
Area	Filled area between the value and the zero baseline.
Histogram	Vertical bars from zero to the value (positive up, negative down).
Dots	Discrete dots at each bar position; no connecting line.
Step	Step line: holds each value as a horizontal segment until the next bar, then jumps. Suits OR / IB bounds, prior-day levels.
Candle	Indicator-side OHLC candles. The plot MUST implement IOhlcPlot so the renderer has all four series.
DashDot	Dash-dot line: the same path as Line stroked dash, gap, dot, gap. Value 8.
DashDotDot	Dash-dot-dot line: the same path as Line stroked dash, gap, dot, gap, dot, gap. Value 9.
PlotColors
A static class of named ChartColor constants (public static readonly fields) in SabrTrader.Pipeline.Indicators.Builtin: the platform's own palette, so an indicator picks a default colour that matches the built-ins instead of hard-coding a hex value. The names follow the HTML colour names.

PlotColors
using SabrTrader.Pipeline.Indicators.Builtin;   // PlotColors
using SabrTrader.Pipeline.Plots;

AddPlot(new Plot("Signal", _signal, PlotStyle.Line, PlotColors.DodgerBlue, lineWidthPx: 2));
Members: Aqua, Black, Blue, BlueViolet, Brown, Chocolate, CornflowerBlue, Crimson, Cyan, DarkCyan, DarkGoldenrod, DarkGray, DarkGreen, DarkOrange, DarkRed, DarkSeaGreen, DeepSkyBlue, DimGray, DodgerBlue, ForestGreen, Gold, Goldenrod, Gray, Green, HotPink, IndianRed, Indigo, LightBlue, LightGray, Lime, LimeGreen, Magenta, Maroon, MediumOrchid, MediumPurple, MediumSeaGreen, Navy, Olive, Orange, OrangeRed, Pink, PowderBlue, Purple, Red, RoyalBlue, SaddleBrown, Sienna, Silver, SlateBlue, SteelBlue, Tan, Teal, Transparent, White, Yellow.

PlotDashPattern
The repeating stroke pattern a patterned PlotStyle or LineStyle paints with, as alternating on / off lengths in pixels: IntervalsPx[0] painted, [1] skipped, [2] painted, and so on. An indicator that strokes its own lines in a CUSTOM render pass (pivot lines, a developing trace, a level) resolves the style through For, so the settings dialog's style picker means the same thing there as it does in the chart's standard plot pass. CustomLineRenderer strokes a pattern on any IChartRenderer. The struct is immutable and thread-safe: the intervals are copied on construction. default is the empty pattern (PeriodPx 0), which strokes solid.

PlotDashPattern.cs
public readonly struct PlotDashPattern : IEquatable<PlotDashPattern>
{
    // Alternating on / off lengths in pixels. Throws ArgumentException when IsValid is false.
    public PlotDashPattern(params ReadOnlySpan<double> intervalsPx);

    public ReadOnlySpan<double> IntervalsPx { get; }   // starts with an on length; empty for default
    public double PeriodPx { get; }                    // length of one full repetition; 0 for default

    public static PlotDashPattern Dashed { get; }       // 6 px on / 4 px off
    public static PlotDashPattern Dotted { get; }       // 2 px on / 4 px off
    public static PlotDashPattern DashDot { get; }      // 6 on / 4 off / 2 on / 4 off
    public static PlotDashPattern DashDotDot { get; }   // 6 on / 4 off / 2 on / 4 off / 2 on / 4 off

    // null when the style strokes solid. Dots shares Dotted's pattern for hand-drawn long
    // lines; the standard plot pass keeps them distinct (Dots is a marker at each bar).
    public static PlotDashPattern? For(PlotStyle style);
    public static PlotDashPattern? For(LineStyle style);

    // True for an even number (at least two) of finite, non-negative lengths with a positive total.
    public static bool IsValid(ReadOnlySpan<double> intervalsPx);
}
IsValid is the one rule for a strokeable pattern: the constructor applies it, and so does a renderer that accepts raw intervals (IPolylineRenderer.DrawDashedPolyline). Two patterns are equal when their intervals are equal. Build a pattern of your own from its lengths:

Custom pattern
// 8 px on, 3 px off, 2 px on, 3 px off; PeriodPx is 16.
var longDashDot = new PlotDashPattern(8, 3, 2, 3);
CustomLineRenderer.DrawStyledLine(ctx.Renderer, x0, y, x1, y, color, 1.5, longDashDot);
IOhlcPlot
A specialised IPlot carrying per-bar OHLC data so the renderer can draw candlesticks. The plot's Values series IS the close series (keeps legend / price-marker / crosshair working). The parallel open/high/low series share the same indexing; any NaN at a bar skips that candle.

Member (adds to IPlot)	Description
ISeries<double> OpenValues { get; }	Open per bar. Aligned with Values.
ISeries<double> HighValues { get; }	High per bar. Aligned with Values.
ISeries<double> LowValues { get; }	Low per bar. Aligned with Values.
ChartColor BullishColor { get; set; }	Body + wick colour for bullish bars (close >= open).
ChartColor BearishColor { get; set; }	Body + wick colour for bearish bars (close < open).
double BodyFillRatio { get; set; }	Body width as a fraction of BarSpacingPx. Typical 0.7; 0 = wick-only. Wicks are always 1px.
OhlcPlot
The default IOhlcPlot implementation (sealed class), for indicator-side candles such as a cumulative delta study. The ctor sets Style to PlotStyle.Candle; Color maps to BullishColor, so code that reads a single colour gets a sensible one. LineWidthPx is the wick width.

OhlcPlot.cs
public OhlcPlot(
    string name,
    ISeries<double> openValues,
    ISeries<double> highValues,
    ISeries<double> lowValues,
    ISeries<double> closeValues,   // becomes Values (the close series)
    ChartColor bullishColor,
    ChartColor bearishColor,
    double wickWidthPx = 1.0,        // becomes LineWidthPx; must be > 0
    double bodyFillRatio = 0.7);     // must be in [0, 1]
IIndicatorLine
A horizontal threshold line on an indicator's panel: a fixed Y value with no time component (RSI 30/70, Stochastic 20/80, ADX 20/40; the equivalent of NinjaTrader's AddLine()).

Member	Description
string Name { get; set; }	Legend label (e.g. "Upper", "Lower").
double Value { get; set; }	Constant Y-value the line renders at.
ChartColor Color { get; set; }	Stroke colour.
double LineWidthPx { get; set; }	Stroke width in pixels.
PlotStyle Style { get; set; }	Solid / dashed / dotted / dash-dot / dash-dot-dot.
bool Visible { get; set; }	When false, the renderer skips this line.
bool ParticipatesInAutoScale { get; set; }	When true (default), the value folds into the subpanel's Y auto-scale (right for bounded oscillators). When false, the line is drawn and clipped but leaves the scale alone (right for ADX/ADXR).
bool IsParameterDriven { get; }	True when the line's Value (and, where the indicator sets it, Visible) comes from one of the indicator's own parameters, re-applied on every OnInit. The settings dialog shows the level read-only for such a line and the workspace skips persisting it, because the parameter already carries it; colour, width and style stay editable. Interface default false.
IndicatorLine
The default IIndicatorLine implementation (sealed class). Add one per threshold via IndicatorBase.AddLine(...).

IndicatorLine.cs
public IndicatorLine(
    string name, double value, ChartColor color,
    double lineWidthPx = 1.0,
    PlotStyle style = PlotStyle.Line,
    bool participatesInAutoScale = true);

public IndicatorLine(
    string name, double value, ChartColor color,
    double lineWidthPx, PlotStyle style,
    bool participatesInAutoScale, bool isParameterDriven);
// name null/whitespace -> ArgumentException; lineWidthPx <= 0 -> ArgumentOutOfRangeException
The second ctor sets IsParameterDriven; every argument is required so a call never binds ambiguously. IndicatorBase.AddLine has matching overloads.

ChartColor
Platform-neutral RGBA colour (8-bit per channel), declared as a readonly record struct. Each renderer converts it to its own native colour type.

ChartColor.cs
public readonly record struct ChartColor(byte R, byte G, byte B, byte A = 255);
Member	Description
byte R, G, B, A	Record positional members. A defaults to 255 (opaque).
static ChartColor Black	new(0, 0, 0).
static ChartColor White	new(255, 255, 255).
static ChartColor Transparent	new(0, 0, 0, 0).
static ChartColor FromArgb(byte a, byte r, byte g, byte b)	Build from ARGB byte order.
static ChartColor FromArgb(uint argb)	Build from a 32-bit packed ARGB value (A=high byte, B=low byte); the inverse of Argb. ChartColor.FromArgb(0xFF1E90FF) is opaque DodgerBlue.
uint Argb { get; }	32-bit packed ARGB (A=high byte, B=low byte).
Rendering (SabrTrader.Pipeline.Charts.Rendering)
The low-level surface a custom render pass writes into, plus the style + geometry value types. Coordinates are already in pixel space (origin top-left, X right, Y down); the renderer knows nothing about bars or prices. Every frame is bracketed by BeginFrame / EndFrame and runs on the render thread, so a renderer implementation is single-threaded.

IChartRenderer
The core 2D drawing surface (IChartRenderer : IDisposable). Several methods are default-implemented convenience wrappers over the primitives.

Member	Description
void BeginFrame(int canvasWidthPx, int canvasHeightPx, ChartColor background)	Open a frame; set size + clear with the background. Pair with EndFrame.
void DrawCandle(double centerX, double bodyTopY, double bodyBottomY, double wickTopY, double wickBottomY, double bodyWidthPx, bool bullish, CandleStyle style)	Draw one candle (all coords already in pixels).
void DrawLine(double x0, double y0, double x1, double y1, ChartColor color, double widthPx)	Stroke a line. Gridlines, ticks, crosshair. An overload takes a trailing SabrTrader.Pipeline.Drawing.LineStyle style for solid / dashed / dotted / dash-dot / dash-dot-dot strokes.
void FillRect(double x, double y, double widthPx, double heightPx, ChartColor color)	Filled rectangle.
void DrawRoundedRectOutline(double x, double y, double widthPx, double heightPx, double radiusPx, ChartColor color, double strokeWidthPx)	Default impl falls back to four straight edges; path-backed renderers override for true rounded corners.
void FillPolygon(ReadOnlySpan<(double X, double Y)> points, ChartColor color)	Filled, auto-closed polygon. Area plots, zone overlays, cluster fills.
void PushClip(double x, double y, double widthPx, double heightPx)	Push a rectangular clip; nested pushes intersect.
void PopClip()	Pop the topmost clip. Pair 1:1 with PushClip.
void DrawText(double x, double y, string text, ChartColor color, double sizePx, string? fontFamily = null, bool bold = false, bool italic = false)	Draw text anchored at its baseline.
void DrawTextCentered(double centerX, double baselineY, string text, ChartColor color, double sizePx, string? fontFamily = null, bool bold = false, bool italic = false)	Default impl measures then offsets; renderers with native centring override.
void DrawTextRotated(double x, double y, double angleDegrees, string text, ChartColor color, double sizePx, string? fontFamily = null, bool bold = false, bool italic = false, bool centered = false)	Default impl ignores rotation (draws horizontal); transformable renderers override.
(double WidthPx, double HeightPx) MeasureText(string text, double sizePx, string? fontFamily = null, bool bold = false, bool italic = false)	Real glyph metrics. Required by label centring + pill sizing.
double FontAscentPx(double sizePx, string? fontFamily = null, bool bold = false, bool italic = false)	Default impl approximates ascent as 80% of measured height; override for exact placement.
void EndFrame()	Finalize the frame (flush to the surface).
Renderer capability interfaces
Optional capabilities a concrete IChartRenderer MAY also implement. Detect each with a single is check on the renderer you already hold, and fall back to the core primitive when the check fails.

Interface	Member(s)	Fallback
IPolylineRenderer	void DrawPolyline(ReadOnlySpan<(double X, double Y)> points, ChartColor color, double widthPx) · void DrawDashedPolyline(points, color, widthPx, ReadOnlySpan<double> intervalsPx): the same open path stroked with a repeating pattern of alternating on / off lengths in pixels, measured continuously along the whole path, so the rhythm carries across vertices. Pass PlotDashPattern.IntervalsPx for a named style. Throws ArgumentException when the intervals fail PlotDashPattern.IsValid. · void DrawDashedPolyline(... , double dashLengthPx, double gapLengthPx): the two-interval form, default-implemented over the one above.	Per-segment DrawLine.
IPaintedCandleRenderer	void DrawCandle(double centerX, double bodyTopY, double bodyBottomY, double wickTopY, double wickBottomY, double bodyWidthPx, bool bullish, CandleStyle style, ChartColor overrideColor)	Standard DrawCandle.
IEllipseRenderer	void FillEllipse(double centerX, double centerY, double radiusX, double radiusY, ChartColor color)	FillPolygon with an n-gon.
IGradientRenderer	void FillLinearGradient(ReadOnlySpan<(double X, double Y)> polygon, double startX, double startY, ChartColor startColor, double endX, double endY, ChartColor endColor)	Flat FillPolygon.
ISphereRenderer	void FillSphere(double centerX, double centerY, double radiusPx, ChartColor color) · void FillSphereShadow(double centerX, double centerY, double radiusPx, ChartColor color, double blurPx)	FillEllipse / FillPolygon.
IHeatmapRasterRenderer	void DrawArgbBitmap(uint[] argbPixels, int widthPx, int heightPx, double destX, double destY) · void DrawArgbBitmapRegion(uint[] argbPixels, int bufferWidthPx, int bufferHeightPx, int srcX, int srcY, int srcWidthPx, int srcHeightPx, double destX, double destY) · void DrawPremultipliedArgbBitmap(uint[] premultipliedPixels, int widthPx, int heightPx, double destX, double destY) (plus an overload taking a trailing long contentVersion)	Per-cell FillRect.
CustomLineRenderer (static class, same namespace). DrawSegment(IChartRenderer renderer, double x0, double y0, double x1, double y1, ChartColor color, double widthPx, PlotStyle style) strokes one short segment honouring a PlotStyle (solid for Line and every non-patterned style, otherwise the pattern PlotDashPattern.For gives it) over the core DrawLine primitive. LineStyle ToLineStyle(PlotStyle style) maps a plot style to the SabrTrader.Pipeline.Drawing.LineStyle that the DrawLine overload takes, for one long line (a price line, a pivot): Dashed, Dotted, DashDot and DashDotDot map across, Dots maps to Dotted, and every other style maps to Solid.

Member	Description
static void DrawStyledLine(IChartRenderer renderer, double x0, double y0, double x1, double y1, ChartColor color, double widthPx, LineStyle style)	Strokes one straight line with the pattern PlotDashPattern.For(style) gives, solid for LineStyle.Solid. Use it for one long line (a level, a pivot, a price line) whose style comes from a LineStyle setting.
static void DrawStyledLine(IChartRenderer renderer, double x0, double y0, double x1, double y1, ChartColor color, double widthPx, PlotDashPattern? pattern)	Strokes one straight line with pattern, or solid when it is null. A renderer with the IPolylineRenderer capability strokes it as one dashed path; any other renderer gets the same geometry as solid DrawLine pieces.
static void DrawSegment(IChartRenderer renderer, double x0, double y0, double x1, double y1, ChartColor color, double widthPx, PlotStyle style)	Draws one bar-to-bar segment in a plot's style. The pattern restarts at the segment's start, which matches the chart's own line path for plots coloured per bar. A zero-length segment draws nothing.
static double DrawSegment(IChartRenderer renderer, double x0, double y0, double x1, double y1, ChartColor color, double widthPx, in PlotDashPattern pattern, double phasePx)	Walks pattern along one segment, starting phasePx pixels into the pattern, and emits one solid DrawLine per painted interval. Returns the phase at the segment's end: feed it into the next segment to keep the rhythm continuous across bar boundaries while the colour changes per segment. An empty (default) pattern strokes the segment solid. It is sized for short hops; for one long line prefer DrawStyledLine.
All four are stateless, allocate nothing and are safe from any render thread. They stroke the fixed plot pattern (Dashed is always 6 px on, 4 px off). The IChartRenderer.DrawLine overload that takes a LineStyle is the drawing-tool stroke: its dash and dot lengths scale with the stroke width.

Per-bar coloured dashed line
// One continuous rhythm across segments whose colour changes per bar:
if (PlotDashPattern.For(plot.Style) is { } dash)
{
    double phase = 0;
    for (int i = first + 1; i <= last; i++)
        phase = CustomLineRenderer.DrawSegment(renderer, x[i - 1], y[i - 1], x[i], y[i],
                                               colorOf(i), plot.LineWidthPx, in dash, phase);
}
Capability detection
// Renderer you already hold from the render context:
if (renderer is IPolylineRenderer poly)
    poly.DrawPolyline(points, color, 1.5);
else
    for (int i = 1; i < points.Length; i++)
        renderer.DrawLine(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, color, 1.5);
CandleStyle
User-tunable visual style for candle rendering (sealed class, all init-only). Defaults match the conventional green-up / red-down theme.

Member	Default
ChartColor BullishBodyColor { get; init; }	new(0x26, 0xa6, 0x9a) (teal-green)
ChartColor BearishBodyColor { get; init; }	new(0xef, 0x53, 0x50) (red)
ChartColor BullishWickColor / BearishWickColor { get; init; }	match the body colours
ChartColor BullishBorderColor / BearishBorderColor { get; init; }	darker body shades
bool HollowBullish { get; init; }	false (filled both ways)
double BodyFillRatio { get; init; }	0.7 (fraction of bar spacing used by the body)
double BorderWidthPx { get; init; }	1.0
double WickWidthPx { get; init; }	1.0
Viewport (SabrTrader.Pipeline.Charts.Viewport)
Pure-math description of what's visible plus the transforms between (bar index, price) and (pixel x, pixel y). Bar index 0 is the oldest visible bar; higher price = lower pixel Y.

ChartPanelKey
A readonly struct identifying one panel of one data series: ChartPanelKey(int seriesIndex, string panelId) with SeriesIndex (0 = primary series) and PanelId ("main" for the price panel, otherwise the subpanel id). IsMain is true for any series' main panel; the constant MainPanelName = "main" and the static Main (series 0, main panel, equal to default) cover the common case. ToCanonicalString() / FromCanonicalString(string?) convert to and from the stored form: the bare panel id for series 0, "s{N}:{PanelId}" for series N; null or empty parses to Main. Use the canonical string when you save anything panel-scoped.

ChartViewport
Immutable (sealed class): rescaling and resizing return new instances, so render code never observes a partially updated state. The ctor validates every argument (positive canvas, non-negative margins, at least one visible bar, maxPrice > minPrice).

ChartViewport.cs
public ChartViewport(
    int canvasWidthPx, int canvasHeightPx,
    int rightMarginPx, int topMarginPx, int bottomMarginPx,
    int firstVisibleBarIdx, int lastVisibleBarIdx,
    double minPrice, double maxPrice,
    int rightInsetPx = 0, int topInsetPx = 0, int bottomInsetPx = 0);
Member	Description
int CanvasWidthPx / CanvasHeightPx	Full canvas size.
int RightMarginPx / TopMarginPx / BottomMarginPx	Axis-label margins.
int FirstVisibleBarIdx / LastVisibleBarIdx	Inclusive visible bar window.
double MinPrice / MaxPrice	Visible price range.
int RightInsetPx / TopInsetPx / BottomInsetPx	Empty content strips that never contain a candle (clamped so the plot keeps ≥ 1px).
int PlotWidthPx / PlotHeightPx	Canvas minus margins.
int BarAreaWidthPx	Plot width minus the right inset (bars lay out across this).
int PriceAreaHeightPx / PriceAreaTopPx	Vertical band the price range maps across, and its top pixel Y.
int VisibleBarCount	Last - First + 1.
double BarSpacingPx	Pixels per bar (bar-center to bar-center), over the bar area.
double BarIndexToPixelX(int barIdx)	Bar index → centre pixel X.
int PixelXToBarIndex(double pixelX)	Inverse; round-to-nearest for hit-testing.
double BarPositionToPixelX(double barPosition)	Fractional bar position → pixel X. A whole number is that bar's centre (the same as BarIndexToPixelX); 10.5 is halfway between the centres of bars 10 and 11. Drawing anchors use it so they can sit between bars.
double PixelXToBarPosition(double pixelX)	Inverse of BarPositionToPixelX: the fractional bar position under a pixel X, with no rounding to a bar.
double PriceToPixelY(double price)	Price → pixel Y (linear).
double PixelYToPrice(double pixelY)	Inverse; extrapolates into the reserved strips.
ChartViewport WithPriceRange(double minPrice, double maxPrice)	New instance with a replaced price range.
ChartViewport WithCanvasSize(int canvasWidthPx, int canvasHeightPx)	New instance with a replaced canvas size.
Indicators get a margin-free viewport. Before a custom render pass, the host collapses the axis and right-inset strips so the whole viewport is the drawable bar area. Measure to PlotWidthPx and the platform margins are already out of the way, while bars still map to candle-exact pixels.
Chart trader panels (SabrTrader.Pipeline.Panels)
The declarative controls an indicator or strategy contributes as a chart trader tab (IChartPanelProvider, ChartPanel, PanelSurface and the ChartPanel* element types) are listed in the Indicators reference.

