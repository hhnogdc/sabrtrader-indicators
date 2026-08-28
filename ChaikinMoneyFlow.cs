using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Chaikin Money Flow.
/// MFV_i = Volume·(2·Close − High − Low)/(High−Low). H==L → 0.
/// Value = 100·ΣMFV / ΣVolume over Period bars. NaN → 0.
/// Defaults Period=21, Goldenrod, sub-panel.
/// </summary>
[IndicatorDescription("Chaikin Money Flow. Rolling sum of Money Flow Volume (volume scaled by close position within bar range) divided by rolling sum of volume; persistent positive readings show accumulation, negative readings show distribution.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ChaikinMoneyFlow : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _sumMF = null!;
    private RollingSum _sumVol = null!;

    public ChaikinMoneyFlow(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("CMF", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -1, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Rolling window for MFV / Volume sums.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 21;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumMF = new RollingSum(Period);
        _sumVol = new RollingSum(Period);
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
        double hl = bar.High - bar.Low;
        double mfv = hl == 0 ? 0 : bar.Volume * (2 * bar.Close - bar.High - bar.Low) / hl;
        _sumMF.Push(mfv);
        _sumVol.Push(bar.Volume);
        double sv = _sumVol.Sum;
        double v = sv == 0 ? 0 : 100 * _sumMF.Sum / sv;
        _output.Append(double.IsNaN(v) ? 0 : v);
    }
}
