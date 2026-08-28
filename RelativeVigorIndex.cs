using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Vigor Index.
/// s1 = (C0-O0 + 2(C1-O1) + 2(C2-O2) + C3-O3)/6.
/// s2 = (H0-L0 + 2(H1-L1) + 2(H2-L2) + H3-L3)/6.
/// Value = Σs1[0..min(barIdx,Period)]/Σs2[0..min(barIdx,Period)].
/// Signal = (V0 + 2V1 + 2V2 + V3)/6.
/// the original skips bars 0..2 (returns 0). Defaults Period=10. Green/Red plots, sub-panel.
/// </summary>
[IndicatorDescription("Relative Vigor Index: weighted four-bar ratio of (Close - Open) sums to (High - Low) sums, summed over Period bars, plus a four-bar weighted signal line. Crossovers signal momentum shifts.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class RelativeVigorIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _value = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _wrapV, _wrapS;

    private RollingSum _sum1 = null!, _sum2 = null!;
    private double[] _s1Hist = null!, _s2Hist = null!;
    private double[] _valHist = null!;
    private int _barIdx;

    public RelativeVigorIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("RVI",    _value,  PlotStyle.Line, PlotColors.Green, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, PlotColors.Red,   1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrapV ??= new IndicatorOutputSeries(_value, Bars);
    public ISeries<double> Signal => _wrapS ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sum1 = new RollingSum(Period);
        _sum2 = new RollingSum(Period);
        _s1Hist = new double[4];
        _s2Hist = new double[4];
        _valHist = new double[4];
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars, 0);
    }

    private void Consume(IBarSeries bars, int barsAgoOfCurrent)
    {
        if (_barIdx < 3)
        {
            _value.Append(0);
            _signal.Append(0);
            // Push the 4-bar rolling history of OHLC differences for when bar 3+ arrives.
            ShiftPush(_s1Hist, bars[barsAgoOfCurrent].Close - bars[barsAgoOfCurrent].Open);
            ShiftPush(_s2Hist, bars[barsAgoOfCurrent].High  - bars[barsAgoOfCurrent].Low);
            ShiftPush(_valHist, 0);
            _barIdx++;
            return;
        }

        // bar offsets 0..3 relative to current (current is barsAgoOfCurrent).
        Bar b0 = bars[barsAgoOfCurrent];
        Bar b1 = bars[barsAgoOfCurrent + 1];
        Bar b2 = bars[barsAgoOfCurrent + 2];
        Bar b3 = bars[barsAgoOfCurrent + 3];

        double s1 = (b0.Close - b0.Open + 2*(b1.Close - b1.Open) + 2*(b2.Close - b2.Open) + (b3.Close - b3.Open)) / 6.0;
        double s2 = (b0.High  - b0.Low  + 2*(b1.High  - b1.Low ) + 2*(b2.High  - b2.Low ) + (b3.High  - b3.Low )) / 6.0;

        _sum1.Push(s1);
        _sum2.Push(s2);

        double v = _sum2.Sum != 0 ? _sum1.Sum / _sum2.Sum : 0;
        double sig = (v + 2 * _valHist[0] + 2 * _valHist[1] + _valHist[2]) / 6.0;
        // valHist[0] = value 1 bar ago, [1] = 2 bars ago, [2] = 3 bars ago.

        _value.Append(v);
        _signal.Append(_sum2.Sum != 0 ? sig : 0);

        ShiftPush(_s1Hist, s1);
        ShiftPush(_s2Hist, s2);
        ShiftPush(_valHist, v);
        _barIdx++;
    }

    private static void ShiftPush(double[] hist, double v)
    {
        // hist[0] = most recent prior, shift to make room then put v at slot 0 on NEXT bar.
        // We want next call's hist[0] to equal what we appended this bar.
        for (int i = hist.Length - 1; i > 0; i--) hist[i] = hist[i - 1];
        hist[0] = v;
    }
}
