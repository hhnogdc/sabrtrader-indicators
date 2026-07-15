using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Momentum Trend — a binary trend filter derived from EMA-smoothed
/// momentum. Outputs +1 while smoothed momentum is positive (uptrend),
/// −1 while negative (downtrend), and holds the prior state exactly at
/// zero. Plotted as a histogram it is a clean regime switch for gating
/// other signals.
///
/// Math:
///   <code>
///   mom   = Close − Close[min(CurrentBar, Period)]
///   sMom  = EMA(mom, Smooth)
///   Trend = sMom &gt; 0 ? +1 : sMom &lt; 0 ? −1 : Trend[1]
///   </code>
///
/// Defaults Period=14, Smooth=3. Single histogram plot, zero
/// centerline, range −1…1, sub-panel.
/// </summary>
[IndicatorDescription("Momentum Trend: a +1/-1 trend filter from EMA-smoothed momentum. +1 while smoothed momentum is positive, -1 while negative; a clean regime switch for gating other signals.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MomentumTrend : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _aMom, _momEma;
    private bool _hasMom;
    private int _trend;
    private int _barIdx;

    public MomentumTrend(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Trend", _output, PlotStyle.Histogram, NTColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, -1, 1, PlotValueUnit.Raw, PlotBullishDirection.Up, "+1 up / -1 down") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bars back for the momentum comparison.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth", Description = "EMA smoothing over the raw momentum.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Period), "All periods must be at least 1.");
        _aMom = 2.0 / (1 + Smooth);
        _momEma = 0;
        _hasMom = false;
        _trend = 1;       // default to up until the first signed reading
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++) Consume(src, src.Count - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        double mom = _barIdx == 0 ? 0 : src[barsAgoOfCurrent] - src[barsAgoOfCurrent + Math.Min(_barIdx, Period)];
        _barIdx++;

        if (!_hasMom) { _momEma = mom; _hasMom = true; }
        else           _momEma = _aMom * mom + (1 - _aMom) * _momEma;

        if (_momEma > 0) _trend = 1;
        else if (_momEma < 0) _trend = -1;
        // exactly zero → hold prior trend
        _output.Append(_trend);
    }
}
