Indicators
Threshold lines
Fixed reference levels such as RSI 30/70, Stochastic 20/80 or ADX 20/40, drawn as horizontal strokes across your indicator's panel.

On this page
What a threshold line is
Adding lines
Bounded vs unbounded indicators
When to call AddLine
Levels driven by a parameter
What a threshold line is
A threshold line is a horizontal reference level at a fixed value, with no time component: the equivalent of NinjaTrader's AddLine(). It holds one value for every bar, so it is an IIndicatorLine (namespace SabrTrader.Pipeline.Plots) rather than a plot. The chart reads IIndicator.Lines alongside Plots and draws each line across the panel, and the user's edits (level, colour, style) are saved with the chart template.

Adding lines
Declare lines in the constructor with the protected AddLine helper. The convenience overload takes (name, value, color, lineWidthPx = 1.0, style = PlotStyle.Line, participatesInAutoScale = true) and creates an IndicatorLine for you; AddLine(IIndicatorLine line) registers a line you constructed yourself.

adding threshold lines
public MyRsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
{
    AddPlot(new Plot("RSI", _rsi, PlotStyle.Line, new ChartColor(156, 39, 176), 1.5));

    // Overbought / oversold reference bands. Dashed reads as "guide, not data".
    AddLine("Overbought", 70, new ChartColor(120, 120, 120), 1.0, PlotStyle.Dashed);
    AddLine("Oversold",   30, new ChartColor(120, 120, 120), 1.0, PlotStyle.Dashed);
}
Like plots, lines are mutable at runtime: Name, Value, Color, LineWidthPx, Style, Visible and ParticipatesInAutoScale are settable, which is how the settings dialog restyles them. Use Line, Dashed or Dotted for the style. A blank name or a width of zero or less throws.

Bounded vs unbounded indicators
participatesInAutoScale controls whether the line's value is included in the subpanel's Y-axis auto-scale:

true (default): right for bounded oscillators (RSI 30/70, Stochastic 20/80) whose data lives near the bands, so the bands always stay on screen.
false: right for unbounded indicators (ADX, ADXR), where forcing a 75 reference line into the scale would squash a typical reading of 10 to 30 into a sliver. The line is drawn (and clipped when out of range) and the scale follows the data alone.
an unbounded reference line
// ADX rarely exceeds 60; keep the 75 reference off the auto-scale.
AddLine("Strong trend", 75, new ChartColor(120, 120, 120),
        lineWidthPx: 1.0, style: PlotStyle.Dashed, participatesInAutoScale: false);
When to call AddLine
Call AddLine from the constructor. The Lines list is append-only and fixed for the indicator's lifetime: the chart caches it and the settings dialog binds to it. OnInit runs again on every parameter edit, so lines added there would be added again each time (2, then 4, then 6).
Line or plot. A line is a fixed reference the user reasons against; a plot is a value your indicator computes each bar. A level that moves with the market (a trailing band, a dynamic stop) is a plot.
Levels driven by a parameter
When a level comes from one of your parameters (a configurable overbought level, a chop-zone threshold), create the line in the constructor, keep it in a field, and set its Value in OnInit. Mark it parameter-driven with the seven-argument overload (or the matching IndicatorLine constructor): the settings dialog then shows the level read-only, since the parameter row is its editor, and the workspace saves the level only through that parameter. Colour, width and style stay editable.

a parameter-driven line
private readonly IndicatorLine _overbought =
    new("Overbought", 70, new ChartColor(120, 120, 120), 1.0, PlotStyle.Dashed,
        participatesInAutoScale: true, isParameterDriven: true);

[IndicatorParameter(DisplayName = "Overbought level", Group = "Parameters", Order = 1,
    MinValue = 50.0, MaxValue = 100.0, Step = 1.0)]
public double OverboughtLevel { get; set; } = 70;

public MyRsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
{
    AddPlot(new Plot("RSI", _rsi, PlotStyle.Line, new ChartColor(156, 39, 176), 1.5));
    AddLine(_overbought);
}

public override void OnInit(IIndicatorContext ctx)
{
    base.OnInit(ctx);
    _overbought.Value = OverboughtLevel;   // re-applied on every recalculation
}
