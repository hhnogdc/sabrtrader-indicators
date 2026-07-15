using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Buy/Sell Volume — port of NT @BuySellVolume.
/// NT runs at tick granularity: classifies each tick as buy aggressor
/// (price ≥ ask) or sell aggressor (price ≤ bid). Without per-bar tick
/// streams here we approximate at bar level:
///   buy = bar.Volume when Close ≥ Open, else 0.
///   sell = bar.Volume when Close &lt; Open, else 0.
/// Buys plot value = buys + sells (total volume), Sells plot = sells —
/// matches NT's stacked-histogram convention.
/// Plots DarkCyan / Crimson, 2px Histogram, sub-panel, no params.
/// </summary>
[IndicatorDescription("Buy/Sell Volume. Stacked-histogram split of each bar's volume into buy volume (close >= open) and sell volume (close < open), making aggressor imbalance visible at a glance.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BuySellVolume : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _buys  = new();
    private readonly ChunkedSeries<double> _sells = new();
    private IndicatorOutputSeries? _bW, _sW;

    public BuySellVolume(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Buys",  _buys,  PlotStyle.Histogram, NTColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Sells", _sells, PlotStyle.Histogram, NTColors.Crimson,  2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
    }

    public ISeries<double> Buys  => _bW ??= new IndicatorOutputSeries(_buys,  Bars);
    public ISeries<double> Sells => _sW ??= new IndicatorOutputSeries(_sells, Bars);

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
        double sells = bar.Close < bar.Open ? bar.Volume : 0;
        double buys = bar.Close >= bar.Open ? bar.Volume : 0;
        _sells.Append(sells);
        _buys.Append(buys + sells);   // NT total-volume convention
    }
}
