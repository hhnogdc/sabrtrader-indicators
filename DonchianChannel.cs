using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Donchian Channel — 1-on-1 port of NT @DonchianChannel.
/// Upper = max(High, Period). Lower = min(Low, Period). Mean = (Upper+Lower)/2.
/// Defaults Period=14. Mean (Goldenrod), Upper/Lower (DodgerBlue). Overlay.
/// </summary>
[IndicatorDescription("Donchian Channel: rolling highest-High (upper) and lowest-Low (lower) over Period bars, with their midline. Classic breakout / volatility envelope.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DonchianChannel : IndicatorBase
{
    private readonly ChunkedSeries<double> _mean  = new();
    private readonly ChunkedSeries<double> _upper = new();
    private readonly ChunkedSeries<double> _lower = new();
    private IndicatorOutputSeries? _meanW, _upW, _loW;
    private RollingMinMax _maxH = null!, _minL = null!;

    public DonchianChannel(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Mean",  _mean,  PlotStyle.Line, NTColors.Goldenrod,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper", _upper, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower", _lower, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window for high/low extremes.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Mean  => _meanW ??= new IndicatorOutputSeries(_mean,  Bars);
    public ISeries<double> Upper => _upW   ??= new IndicatorOutputSeries(_upper, Bars);
    public ISeries<double> Lower => _loW   ??= new IndicatorOutputSeries(_lower, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxH = new RollingMinMax(Period, trackMax: true);
        _minL = new RollingMinMax(Period, trackMax: false);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            _maxH.Push(bar.High); _minL.Push(bar.Low);
            double u = _maxH.Value, l = _minL.Value;
            _upper.Append(u); _lower.Append(l); _mean.Append((u + l) / 2);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var bar = Bars[0];
        _maxH.Push(bar.High); _minL.Push(bar.Low);
        double u = _maxH.Value, l = _minL.Value;
        _upper.Append(u); _lower.Append(l); _mean.Append((u + l) / 2);
    }
}
