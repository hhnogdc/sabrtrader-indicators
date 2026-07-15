using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Smoothed Moving Average (SMMA) — 1-on-1 port of Skender Smma.
/// Wilder's MA: SMMA[i] = (SMMA[i−1] · (P−1) + value[i]) / P.
/// Seeded with SMA(value, P) at bar P−1; bars 0..P−2 emit NaN per Skender.
/// (Mathematically identical to EMA with α = 1/P after seeding.)
/// Default Period=14, Goldenrod, overlay.
/// </summary>
[IndicatorDescription("Smoothed Moving Average (Wilder MA): SMMA = (prev * (Period-1) + value) / Period, seeded with an SMA. Equivalent to an EMA with alpha = 1/Period; reacts more slowly than SMA for the same period.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Smma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _seedSum;
    private double _value;
    private bool _seeded;
    private int _barIdx;

    public Smma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("SMMA", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _seedSum = 0;
        _value = 0;
        _seeded = false;
        _barIdx = 0;
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
        if (!_seeded)
        {
            _seedSum += v;
            if (_barIdx < Period - 1)
            {
                _output.Append(double.NaN);
                _barIdx++;
                return;
            }
            // bar Period-1: seed with SMA(P) and start outputting.
            _value = _seedSum / Period;
            _seeded = true;
            _output.Append(_value);
            _barIdx++;
            return;
        }
        _value = (_value * (Period - 1) + v) / Period;
        _output.Append(_value);
        _barIdx++;
    }
}
