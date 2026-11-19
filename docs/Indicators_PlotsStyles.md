Indicators
Plots & styles
Every visible output is a plot: a series plus how to draw it. Lines, dashes, areas, histograms, step lines and indicator-side candles, all restylable at runtime.

On this page
Adding a plot
When to call AddPlot
Plot styles
Runtime restyling
Price-axis markers
Per-bar colours and plot flags
Indicator-side candles
Plot semantics
Adding a plot
A plot is one output line on the chart. An indicator can own several: an SMA has one, MACD has three (line, signal, histogram), Bollinger Bands have three (upper, middle, lower). You build a Plot (namespace SabrTrader.Pipeline.Plots) over your output series and register it with the protected AddPlot helper. The Plot constructor is (string name, ISeries<double> values, PlotStyle style, ChartColor color, double lineWidthPx).

adding plots
private readonly ChunkedSeries<double> _macd   = new();
private readonly ChunkedSeries<double> _signal = new();
private readonly ChunkedSeries<double> _hist   = new();

public MyMacd(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
{
    // Order matters: earlier plots render BEHIND later ones.
    AddPlot(new Plot("Histogram", _hist,   PlotStyle.Histogram, new ChartColor(120, 120, 120), 1.0));
    AddPlot(new Plot("MACD",      _macd,   PlotStyle.Line,      new ChartColor(33, 150, 243), 1.5));
    AddPlot(new Plot("Signal",    _signal, PlotStyle.Line,      new ChartColor(244, 67, 54),  1.5));
}
Draw order follows registration order: add the histogram first so the lines sit on top of it. The Plot constructor and its setters validate their arguments: a blank name or a lineWidthPx <= 0 throws immediately. IndicatorBase.TryGetPlot(name, out plot) finds a registered plot by its exact name.

When to call AddPlot
Call AddPlot from the constructor. The Plots list is append-only and its reference is stable for the indicator's lifetime: the chart render pass caches it and the settings dialog binds to a fixed set of rows. OnInit runs again on every recalculation, so a plot added there would be registered again each time. After construction you may change plot properties freely, but never add or remove plots.
Plot styles
PlotStyle selects the rendering primitive. NaN values are skipped: lines break, dots and bars are left out.

Style	Renders as
Line	Continuous line through consecutive non-NaN values. The default.
Dashed / Dotted	Same path as Line with a dash / dot stroke pattern.
Area	Filled area between the value and the zero baseline.
Histogram	Vertical bars from zero to the value (positive up, negative down).
Dots	Discrete dots at each bar; no connecting line.
Step	Holds each value as a horizontal segment until the next bar, then jumps. For levels that hold, then change (opening range, prior day).
Candle	Per-bar OHLC candles. Requires the plot to implement IOhlcPlot (see below).
Runtime restyling
IPlot is mutable on purpose. The settings dialog recolours and restyles plots at runtime without reconstructing the indicator; the chart reads the properties again on the next render pass. A property change costs a repaint, not a recalculation.

mutating a plot
IPlot signal = Plots[2];
signal.Color       = new ChartColor(255, 152, 0);
signal.Style       = PlotStyle.Dashed;
signal.LineWidthPx = 2.0;
signal.Visible     = false;     // hide without removing the plot
The settings dialog shows a style row per plot (colour, style, width, visibility) that writes these properties directly and persists them with the chart, so a plot's colour needs no separate parameter. Declare a ChartColor parameter for colours the indicator uses elsewhere, such as custom rendering or the per-bar colours below.

Price-axis markers
Each plot can paint a small filled label on the price axis at its most recent value via IPlot.ShowPriceMarker (default true). The indicator-level ShowPriceMarkers property (inherited from IndicatorBase) is a master gate: when false, no marker of this indicator paints, whatever the per-plot setting.

Per-bar colours and plot flags
To colour a line bar by bar (a slope-coloured moving average, for example), give the plot a ChunkedSeries<ChartColor> through PerBarColors and append one colour per bar in step with the values. Each segment takes the colour of the bar it ends on; a bar with no entry falls back to Color. It applies to Line, Dashed, Dotted and Dots. The series is output, not a setting: the workspace saves the plot's style but not this series, and a recalculation clears it together with the values.

per-bar colours
private readonly ChunkedSeries<double>     _ma     = new();
private readonly ChunkedSeries<ChartColor> _colors = new();

// constructor
AddPlot(new Plot("MA", _ma, PlotStyle.Line, new ChartColor(33, 150, 243), 2.0)
{
    PerBarColors = _colors,
});

// per bar, after appending to _ma
_colors.Append(_ma.Count > 1 && _ma[0] >= _ma[1] ? UpColor : DownColor);
Plot property	Default	Use
Visible	true	Hide the plot on the chart without removing it. The settings dialog's show/hide toggle.
IncludeInDataWindow	true	Set false for an internal series that should never appear in the data box. Independent of Visible: a hidden plot can still show its value there.
IsUserConfigurable	true	Set false for a series the chart never paints (a +1/-1 signal used for the data box, export and chaining), so the settings dialog shows no style row for it.
IsCustomRendered	false	Set true when your own custom render draws the plot. The standard plot pass skips it; your render code honours Visible and Style itself.
Owner / OwnerName	set by AddPlot	The owning indicator and its type name, used by the legend and diagnostics. You do not set them.
Indicator-side candles
Some indicators are naturally per-bar OHLC rather than a single value. Cumulative Delta, for example, draws an open/high/low/close candle of the running delta. PlotStyle.Candle needs a plot that implements IOhlcPlot, which carries the four parallel series. OhlcPlot implements it and sets its style to Candle itself.

OHLC plot
// OhlcPlot exposes OpenValues / HighValues / LowValues + Values (the close),
// all aligned by barsAgo, plus bull/bear colours and a body-fill ratio.
// bullishColor + bearishColor are required ctor arguments; wickWidthPx and
// bodyFillRatio are optional (default 1.0 and 0.7).
var ohlc = new OhlcPlot("CumDelta", _open, _high, _low, _close,
    bullishColor: new ChartColor(38, 166, 154),
    bearishColor: new ChartColor(239, 83, 80),
    bodyFillRatio: 0.7);
AddPlot(ohlc);
Use PlotStyle.Candle only with an IOhlcPlot. The renderer needs all four series and reports an error for a single-series plot with that style.
Plot semantics
PlotSemantics (namespace SabrTrader.Pipeline.Indicators) declares a plot's machine-readable meaning, so an automated consumer such as the AI assistant can interpret RSI = 71.6 without recognising the indicator by name. Set it on the Plot when you add it.

declaring semantics
AddPlot(new Plot("RSI", _rsi, PlotStyle.Line, new ChartColor(156, 39, 176), 1.5)
{
    Semantics = new PlotSemantics(
        Kind: PlotKind.Oscillator,
        RangeMin: 0, RangeMax: 100,
        Unit: PlotValueUnit.Percent,
        BullishDirection: PlotBullishDirection.Up,
        Reference: "30/70 oversold/overbought"),
});
Kind is one of Unspecified, Overlay, Oscillator, Histogram, Signal, Band. Unit is one of Unspecified, Price, Percent, Ratio, Ticks, Raw. BullishDirection is None, Up or Down. Leave RangeMin / RangeMax null for an unbounded value. A plot that declares nothing reports PlotSemantics.Default (semantics unknown).

