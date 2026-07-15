using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Zero-Lag EMA — 1-on-1 port of NT @ZLEMA.
/// Formula: bar &lt; lag → Value = Input. Otherwise
/// <c>Value = k·(2·Input[0] − Input[lag]) + (1−k)·prev</c>,
/// where k = 2/(Period+1), lag = ceil((Period−1)/2).
/// Default Period=14, Goldenrod, range [1, int.MaxValue].
/// </summary>
[IndicatorDescription("Zero-Lag EMA (Ehlers/Way). Standard EMA applied to a de-lagged input (2*price - price[lag]) where lag = ceil((Period-1)/2), removing most of the EMA's intrinsic delay.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Zlema : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _k, _oneMinusK;
    private int _lag;
    private int _barIdx;
    private double _prev;

    public Zlema(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ZLEMA", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _k = 2.0 / (Period + 1);
        _oneMinusK = 1.0 - _k;
        _lag = (int)Math.Ceiling((Period - 1) / 2.0);
        _barIdx = 0;
        _prev = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
            Consume(src, n - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        Consume(src, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        // barsAgoOfCurrent is the index of the bar being processed in NT-style barsAgo.
        // For OnDataLoaded we pass varying values, for live always 0.
        double current = src[barsAgoOfCurrent];
        if (_barIdx < _lag)
        {
            _prev = current;
            _output.Append(current);
            _barIdx++;
            return;
        }
        // src[barsAgoOfCurrent + _lag] is "lag bars before current".
        double lagged = src[barsAgoOfCurrent + _lag];
        _prev = _k * (2 * current - lagged) + _oneMinusK * _prev;
        _output.Append(_prev);
        _barIdx++;
    }
}
