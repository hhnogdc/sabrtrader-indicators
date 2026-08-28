using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// McGinley Dynamic — adaptive moving average by John R. McGinley.
/// md = md[1] + (price − md[1]) / (N · (price/md[1])⁴), seeded with price
/// on the first bar. The (price/md[1])⁴ term auto-accelerates the line in
/// fast markets and decelerates it in slow ones, so it hugs price more
/// faithfully than a fixed-period EMA and largely sidesteps whipsaw.
/// Defaults Period=14. DodgerBlue, overlay.
/// </summary>
[IndicatorDescription("McGinley Dynamic: a self-adjusting moving average that speeds up or slows down with the market via a (price/avg)^4 factor, tracking price more closely than a fixed-period EMA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class McGinleyDynamic : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    // Per-bar line colour (the original PlotBrushes): green when the line rises bar-over-bar,
    // red when it falls. Appended in lockstep with _output; the recalc reset clears
    // both together (IndicatorBase.ClearPlotSeries reflects over "PerBarColors").
    private readonly ChunkedSeries<ChartColor> _colors = new();
    private IndicatorOutputSeries? _wrap;
    private double _value;
    private bool _seeded;

    public McGinleyDynamic(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("McGinley", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0)
        { PerBarColors = _colors, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Smoothing length (N).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    // ─── 02. Colors ─────────────────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "Rising", Group = "02. Colors", Order = 1,
        Description = "Line colour on bars where the line is rising versus the prior bar.")]
    public ChartColor RisingColor { get; set; } = PlotColors.ForestGreen;

    [IndicatorParameter(DisplayName = "Falling", Group = "02. Colors", Order = 2,
        Description = "Line colour on bars where the line is falling versus the prior bar.")]
    public ChartColor FallingColor { get; set; } = PlotColors.Crimson;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _value = 0; _seeded = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double price)
    {
        if (!_seeded || _value == 0)
        {
            _value = price;
            _seeded = true;
            _output.Append(_value);
            _colors.Append(RisingColor);   // first bar: no prior — default to rising
            return;
        }
        double prev = _value;
        double ratio = price / _value;
        _value += (price - _value) / (Period * ratio * ratio * ratio * ratio);
        _output.Append(_value);
        _colors.Append(_value < prev ? FallingColor : RisingColor);
    }
}
