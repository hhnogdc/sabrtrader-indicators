using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume-Weighted Moving Average — 1-on-1 port of NT @VWMA.
/// Value = Σ(price·vol) / Σ(vol) over the trailing Period bars.
/// When Σvol ≈ 0, falls back to the price sum (NT div-by-zero guard).
/// Default Period=14, DodgerBlue, sub-panel (NT DrawOnPricePanel=false).
/// </summary>
[IndicatorDescription("Volume-Weighted Moving Average. Sum(price*volume)/Sum(volume) over the trailing Period bars, giving heavier weight to bars with more participation than a simple SMA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Vwma : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double[] _prices = [];
    private double[] _vols = [];
    private int _count;
    private int _writeIdx;

    public Vwma(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("VWMA", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window size.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _prices = new double[Period];
        _vols   = new double[Period];
        _count = 0;
        _writeIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
            Consume(bars[bars.Count - 1 - absIdx]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        Consume(bars[0]);
    }

    private void Consume(Bar bar)
    {
        double price = bar.Close;
        double vol = bar.Volume;
        _prices[_writeIdx] = price;
        _vols[_writeIdx]   = vol;
        _writeIdx = (_writeIdx + 1) % Period;
        if (_count < Period) _count++;

        double pvSum = 0;
        double vSum = 0;
        for (int i = 0; i < _count; i++)
        {
            pvSum += _prices[i] * _vols[i];
            vSum  += _vols[i];
        }
        _output.Append(vSum <= double.Epsilon ? pvSum : pvSum / vSum);
    }
}
