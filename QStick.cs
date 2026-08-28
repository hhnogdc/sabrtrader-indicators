using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// QStick (Tushar Chande) — the moving average of the candle body
/// <c>Close − Open</c>. Positive QStick means recent bars closed above
/// their opens on average (bullish bodies); negative means bearish.
/// Zero-line crossings flag a shift in the balance of intrabar
/// buying/selling.
///
/// Math: <c>QStick = SMA(Close − Open, Period)</c>.
///
/// Defaults Period=8. Single plot (Goldenrod), zero centerline,
/// sub-panel.
/// </summary>
[IndicatorDescription("QStick (Chande): the SMA of the candle body (Close - Open). Positive = bullish bodies dominate, negative = bearish; zero crossings flag a shift in intrabar control.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class QStick : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _sum = null!;
    private int _count;

    public QStick(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("QStick", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "SMA window over the candle body.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 8;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sum = new RollingSum(Period);
        _count = 0;
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
        _sum.Push(bar.Close - bar.Open);
        _count++;
        _output.Append(_sum.Sum / Math.Min(_count, Period));
    }
}
