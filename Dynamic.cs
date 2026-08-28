using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// McGinley Dynamic — derived from Skender.Stock.Indicators (Apache-2.0).
///   dyn = prevDyn + (newVal − prevDyn) / (KFactor · LookbackPeriods · (newVal / prevDyn)^4)
/// Seeded at bar 0 with the first value; bar 0 itself emits NaN per Skender.
/// Defaults LookbackPeriods=14 (user-required; Skender has no default), KFactor=0.6.
/// Goldenrod, overlay.
/// </summary>
[IndicatorDescription("McGinley Dynamic: an adaptive moving average that auto-adjusts its smoothing to the speed of price (denominator scales with (price/prev)^4), reducing whipsaw vs. a fixed-period EMA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Dynamic : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _prevDyn;
    private double _prevVal;
    private bool _seeded;

    public Dynamic(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("McGinley Dynamic", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "LookbackPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriods { get; set; } = 14;

    [IndicatorParameter(DisplayName = "KFactor",
        Description = "Smoothing factor (> 0). Skender default 0.6.",
        MinValue = 0.001, MaxValue = double.MaxValue, Step = 0.1)]
    public double KFactor { get; set; } = 0.6;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LookbackPeriods < 1) throw new ArgumentOutOfRangeException(nameof(LookbackPeriods));
        if (KFactor <= 0) throw new ArgumentOutOfRangeException(nameof(KFactor));
        _seeded = false;
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
            _output.Append(double.NaN);
            _prevVal = v;
            _seeded = true;
            return;
        }
        // Skender: prevDyn defaults to prevVal when there's no prior dyn.
        double prev = double.IsNaN(_prevDyn) || _prevDyn == 0 ? _prevVal : _prevDyn;
        double dyn = prev + (v - prev) / (KFactor * LookbackPeriods * Math.Pow(v / prev, 4));
        _output.Append(dyn);
        _prevDyn = dyn;
        _prevVal = v;
    }
}
