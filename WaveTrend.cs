using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// WaveTrend Oscillator (LazyBear) — popular momentum/cycle oscillator.
///   ap   = HLC3
///   esa  = EMA(ap, ChannelLength)                       (typically 10)
///   d    = EMA(|ap − esa|, ChannelLength)
///   ci   = (ap − esa) / (0.015 · d)
///   tci  = EMA(ci, AverageLength)                       (typically 21)
///   WT1  = tci
///   WT2  = SMA(WT1, SmaLength)                          (typically 4)
///   Diff = WT1 − WT2                                    (histogram)
/// Cross of WT1 above WT2 in oversold zone → bullish; opposite in overbought → bearish.
/// Defaults ChannelLength=10, AverageLength=21, SmaLength=4. Overbought ±60, Oversold ±53 lines.
/// Plots: WT1 (DodgerBlue), WT2 (Crimson), Diff (Histogram DarkCyan). Sub-panel.
/// EMAs seeded with first valid sample (LazyBear's TradingView default).
/// </summary>
[IndicatorDescription("LazyBear's WaveTrend Oscillator. Double-EMA-smoothed deviation of HLC3 from its channel, plotted as WT1/WT2 lines plus a histogram of their difference. Crossovers in overbought/oversold zones flag reversals.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class WaveTrend : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _wt1  = new();
    private readonly ChunkedSeries<double> _wt2  = new();
    private readonly ChunkedSeries<double> _diff = new();
    private IndicatorOutputSeries? _w1W, _w2W, _dW;

    private double _esa, _d, _tci;
    private bool _seedEsa, _seedD, _seedTci;
    private double _kCh, _kAvg;
    private RollingSum _wt1Sum = null!;

    public WaveTrend(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("WT1",  _wt1,  PlotStyle.Line,      NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("WT2",  _wt2,  PlotStyle.Line,      NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Diff", _diff, PlotStyle.Histogram, NTColors.DarkCyan,   2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Overbought 2",   60, NTColors.DarkGray);
        AddLine("Overbought 1",   53, NTColors.DarkGray);
        AddLine("Zero line",       0, NTColors.DarkGray);
        AddLine("Oversold 1",    -53, NTColors.DarkGray);
        AddLine("Oversold 2",    -60, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "ChannelLength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int ChannelLength { get; set; } = 10;

    [IndicatorParameter(DisplayName = "AverageLength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int AverageLength { get; set; } = 21;

    [IndicatorParameter(DisplayName = "SmaLength",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmaLength     { get; set; } = 4;

    public ISeries<double> Output => _w1W ??= new IndicatorOutputSeries(_wt1,  Bars);
    public ISeries<double> Signal => _w2W ??= new IndicatorOutputSeries(_wt2,  Bars);
    public ISeries<double> Diff   => _dW  ??= new IndicatorOutputSeries(_diff, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (ChannelLength < 1 || AverageLength < 1 || SmaLength < 1) throw new ArgumentOutOfRangeException();
        _kCh  = 2d / (ChannelLength + 1);
        _kAvg = 2d / (AverageLength + 1);
        _seedEsa = _seedD = _seedTci = false;
        _wt1Sum = new RollingSum(SmaLength);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    private void Consume(Bar bar)
    {
        double ap = (bar.High + bar.Low + bar.Close) / 3;

        if (!_seedEsa) { _esa = ap; _seedEsa = true; }
        else            _esa = _esa + _kCh * (ap - _esa);

        double absDiff = Math.Abs(ap - _esa);
        if (!_seedD) { _d = absDiff; _seedD = true; }
        else          _d = _d + _kCh * (absDiff - _d);

        double ci = _d == 0 ? 0 : (ap - _esa) / (0.015 * _d);

        if (!_seedTci) { _tci = ci; _seedTci = true; }
        else            _tci = _tci + _kAvg * (ci - _tci);

        double wt1 = _tci;
        _wt1.Append(wt1);
        _wt1Sum.Push(wt1);

        if (_wt1Sum.Count < SmaLength) { _wt2.Append(double.NaN); _diff.Append(double.NaN); return; }
        double wt2 = _wt1Sum.Sum / SmaLength;
        _wt2.Append(wt2);
        _diff.Append(wt1 - wt2);
    }
}
