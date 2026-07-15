using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Money Flow Oscillator — 1-on-1 port of NT @MoneyFlowOscillator.
/// dvs = (H − prevL) + (prevH − L). If H[0] &lt; prevL → mltp = −1;
/// elif L[0] &gt; prevH → mltp = +1; else mltp = ((H−prevL)−(prevH−L))/dvs.
/// MFV = mltp · Volume. Output = ΣMFV / ΣVolume over Period.
/// Default Period=20, DodgerBlue, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Money Flow Oscillator: volume-weighted measure of bar overlap with the prior bar's range, summed over Period. Oscillates around zero to gauge buying versus selling pressure.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MoneyFlowOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _sumMfv = null!, _sumVol = null!;
    private double? _prevHigh, _prevLow;

    public MoneyFlowOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Money Flow", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumMfv = new RollingSum(Period);
        _sumVol = new RollingSum(Period);
        _prevHigh = _prevLow = null;
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
        if (_prevHigh is null) { _sumMfv.Push(0); _sumVol.Push(bar.Volume); _output.Append(0); _prevHigh = bar.High; _prevLow = bar.Low; return; }
        double pH = _prevHigh.Value, pL = _prevLow!.Value;
        double dvs = (bar.High - pL) + (pH - bar.Low);
        if (dvs == 0) dvs = 0.00001;
        double mltp = bar.High < pL ? -1 :
                      bar.Low > pH ?  1 :
                      Math.Round(((bar.High - pL) - (pH - bar.Low)) / dvs, 2);
        double mfv = mltp * bar.Volume;
        _sumMfv.Push(mfv);
        _sumVol.Push(bar.Volume);
        double sv = _sumVol.Sum;
        _output.Append(sv == 0 ? 0 : Math.Round(_sumMfv.Sum / sv, 3));
        _prevHigh = bar.High; _prevLow = bar.Low;
    }
}
