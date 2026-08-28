using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Double Stochastics.
/// p1 = 100*(C-minLow)/(maxHigh-minLow) clamped 0..100 (or prev p1 / 50 if range=0).
/// p2 = EMA(p1, 3).
/// p3 = 100*(p2-minP2)/(maxP2-minP2) clamped 0..100 (or prev p3 / 50 if range=0).
/// K  = EMA(p3, 3). Default Period=10. Crimson plot; 90/10 dashed DodgerBlue lines.
/// EMAs read parent-filled series → parent=null + manually driven.
/// </summary>
[IndicatorDescription("Double Stochastics (Bressert): a stochastic of a stochastic — %K is normalised, EMA-smoothed, then normalised and EMA-smoothed again. Sharper overbought/oversold turns than the single %K.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DoubleStochastics : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _p1 = new();
    private readonly ChunkedSeries<double> _p3 = new();
    private IndicatorOutputSeries? _wrap, _p1W, _p3W;

    private RollingMinMax _maxHigh = null!, _minLow = null!, _maxP2 = null!, _minP2 = null!;
    private Ema _emaP1 = null!, _emaP3 = null!;
    private int _barIdx;

    public DoubleStochastics(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("K", _output, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "10 oversold / 90 overbought") });
        AddLine("Overbought", 90, PlotColors.DodgerBlue);
        AddLine("Oversold",   10, PlotColors.DodgerBlue);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxHigh = new RollingMinMax(Period, trackMax: true);
        _minLow  = new RollingMinMax(Period, trackMax: false);
        _maxP2   = new RollingMinMax(Period, trackMax: true);
        _minP2   = new RollingMinMax(Period, trackMax: false);
        _p1W = new IndicatorOutputSeries(_p1, Bars);
        _p3W = new IndicatorOutputSeries(_p3, Bars);
        _emaP1 = new Ema(parent: null, input: _p1W) { Period = 3 };
        _emaP3 = new Ema(parent: null, input: _p3W) { Period = 3 };
        _emaP1.OnInit(ctx);
        _emaP3.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i], ctx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0], ctx);
    }

    private void Consume(Bar bar, IIndicatorContext ctx)
    {
        _maxHigh.Push(bar.High);
        _minLow.Push(bar.Low);
        double mxH = _maxHigh.Value, mnL = _minLow.Value;
        double r = mxH - mnL;
        double p1v;
        if (r == 0)
            p1v = _barIdx == 0 ? 50 : _p1[0];
        else
            p1v = Math.Min(100, Math.Max(0, 100 * (bar.Close - mnL) / r));
        _p1.Append(p1v);
        _emaP1.OnBarUpdate(ctx);
        double p2v = _emaP1.Output[0];

        _maxP2.Push(p2v); _minP2.Push(p2v);
        double mxP = _maxP2.Value, mnP = _minP2.Value;
        double s = mxP - mnP;
        double p3v;
        if (s == 0)
            p3v = _barIdx == 0 ? 50 : _p3[0];
        else
            p3v = Math.Min(100, Math.Max(0, 100 * (p2v - mnP) / s));
        _p3.Append(p3v);
        _emaP3.OnBarUpdate(ctx);
        _output.Append(_emaP3.Output[0]);
        _barIdx++;
    }
}
