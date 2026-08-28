using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Keltner Channel.
/// Midline = SMA(Typical, Period). Offset = SMA(High−Low, Period) · OffsetMultiplier.
/// Upper/Lower = Midline ± offset.
/// Defaults Period=10, OffsetMultiplier=1.5.
/// Plots: Midline (DarkGray), Upper (DodgerBlue), Lower (DodgerBlue), overlay.
/// </summary>
[IndicatorDescription("Keltner Channel: SMA of the typical price with upper/lower bands offset by SMA(High-Low) times a multiplier. A volatility envelope around price for breakout and mean-reversion setups.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class KeltnerChannel : IndicatorBase
{
    private readonly ChunkedSeries<double> _mid   = new();
    private readonly ChunkedSeries<double> _upper = new();
    private readonly ChunkedSeries<double> _lower = new();
    private readonly ChunkedSeries<double> _typical = new();
    private readonly ChunkedSeries<double> _diff = new();
    private IndicatorOutputSeries? _midW, _upW, _loW, _typW, _diffW;
    private Sma _smaTypical = null!;
    private Sma _smaDiff = null!;

    public KeltnerChannel(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Midline", _mid,   PlotStyle.Line, PlotColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",   _upper, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",   _lower, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "OffsetMultiplier", Description = "Multiplier on SMA(H−L).",
        MinValue = 0.01, MaxValue = int.MaxValue, Step = 0.1)]
    public double OffsetMultiplier { get; set; } = 1.5;

    [IndicatorParameter(DisplayName = "Period", Description = "Window for both SMAs.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Midline => _midW ??= new IndicatorOutputSeries(_mid,   Bars);
    public ISeries<double> Upper   => _upW  ??= new IndicatorOutputSeries(_upper, Bars);
    public ISeries<double> Lower   => _loW  ??= new IndicatorOutputSeries(_lower, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (OffsetMultiplier < 0.01) throw new ArgumentOutOfRangeException(nameof(OffsetMultiplier));
        _typW = new IndicatorOutputSeries(_typical, Bars);
        _diffW = new IndicatorOutputSeries(_diff, Bars);
        // parent=null: parent-filled inputs; avoid auto-dispatch double-processing.
        _smaTypical = new Sma(parent: null, input: _typW)  { Period = Period };
        _smaDiff    = new Sma(parent: null, input: _diffW) { Period = Period };
        _smaTypical.OnInit(ctx);
        _smaDiff.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            _typical.Append((bar.High + bar.Low + bar.Close) / 3.0);
            _diff.Append(bar.High - bar.Low);
        }
        _smaTypical.OnDataLoaded(ctx);
        _smaDiff.OnDataLoaded(ctx);
        int n = _smaTypical.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double mid = _smaTypical.Output[b];
            double off = _smaDiff.Output[b] * OffsetMultiplier;
            _mid.Append(mid); _upper.Append(mid + off); _lower.Append(mid - off);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var bar = Bars[0];
        _typical.Append((bar.High + bar.Low + bar.Close) / 3.0);
        _diff.Append(bar.High - bar.Low);
        _smaTypical.OnBarUpdate(ctx);
        _smaDiff.OnBarUpdate(ctx);
        double mid = _smaTypical.Output[0];
        double off = _smaDiff.Output[0] * OffsetMultiplier;
        _mid.Append(mid); _upper.Append(mid + off); _lower.Append(mid - off);
    }
}
