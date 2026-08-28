using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// BWMA — Bill Williams Moving Average.
///
/// A recursive smoother seeded with the first input:
/// <c>BWMA[0] = value[0]</c>, then
/// <c>BWMA[i] = (1 − 1/Period)·BWMA[i−1] + value[i]/Period</c>.
/// This is algebraically an EMA-of-the-Wilder-kind with α = 1/Period,
/// seeded directly from price (no SMA warm-up), so every bar has a value.
/// Default Period=10, Blue overlay.
/// </summary>
[IndicatorDescription("Bill Williams Moving Average: a recursive smoother BWMA = (1 - 1/Period)*prev + value/Period, seeded with the first input. Equivalent to a Wilder-style EMA with alpha = 1/Period; defined from bar 0.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Bwma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _value;
    private bool _hasValue;

    public Bwma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("BWMA", _output, PlotStyle.Line, PlotColors.Blue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Smoothing period — α = 1/Period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        _value = 0;
        _hasValue = false;
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

    private void Consume(double v)
    {
        if (!_hasValue)
        {
            _value = v;
            _hasValue = true;
            _output.Append(_value);
            return;
        }
        _value = (1.0 - 1.0 / Period) * _value + v / Period;
        _output.Append(_value);
    }
}
