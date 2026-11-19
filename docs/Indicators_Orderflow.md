Indicators
Orderflow ADVANCED
Per-price-level bid/ask volume per bar: the data behind footprint, delta and POC tools. Opt in once and the framework aggregates it for you.

On this page
Opting in
What an OrderFlowBar gives you
Per-bar statistics
Backfill resolution
Open Interest
Dynamic order-flow requirement
Opting in
Footprint-class indicators need per-level, per-side traded volume. Building that for history means loading tick (or second/minute) data, which is expensive, so the host only loads it when an indicator on the chart asks. Tag the indicator with [RequiresOrderFlow] and read the per-bar data through ctx.OrderFlow(seriesIndex), an ISeries<OrderFlowBar> aligned 1:1 with the bars.

Say how much you read. [RequiresOrderFlow] asks for the full footprint. An indicator that reads only the per-bar totals (volume, buy volume, sell volume, trade count, delta) declares [RequiresOrderFlow(OrderFlowDetail.Totals)]. The host then loads the venue's own bars where they carry the buy/sell split, instead of the footprint. A chart loads the most any of its indicators asks for, so a totals indicator next to a footprint simply reads the footprint's totals. With totals, read TotalAskVolume, TotalBidVolume, TotalTradeCount and Delta: historical bars can come without price levels.

Without the attribute the history is empty. ctx.OrderFlow still returns a series of the right length, but on a chart that was not built with historical order flow every historical entry is an empty snapshot (LevelCount == 0). When you add an indicator marked [RequiresOrderFlow] to such a chart, the host reloads the chart with order-flow backfill enabled.
The attribute covers the chart's own series, index 0. A series you add yourself is loaded as bars unless you add it with ctx.AddDataSeries(spec, OrderFlowDetail.Totals) or ctx.AddDataSeries(spec, OrderFlowDetail.PriceLevels); see Adding a series.

The data arrives for history and live, as a pre-aggregated OrderFlowBar read in OnDataLoaded and OnBarUpdate. If you also want the raw live trade stream, opt into ProcessesMarketData separately; the two combine.

PocMarker.cs
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.OrderFlow;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorDescription("Marks each bar's volume Point of Control: the price level with the most traded volume.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[IndicatorInput(IndicatorInputKind.None)]
[RequiresOrderFlow]                                  // loads historical order flow for this chart
public sealed class PocMarker : IndicatorBase
{
    private readonly ChunkedSeries<double> _poc = new();

    public PocMarker(IIndicator? parent = null) : base(parent)
    {
        AddPlot(new Plot("POC", _poc, PlotStyle.Dots, new ChartColor(255, 152, 0), 4.0));
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var of = ctx.OrderFlow(0);
        for (int i = of.Count - 1; i >= 0; i--)
            _poc.Append(PocOf(of[i]));
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        _poc.Append(PocOf(ctx.OrderFlow(0)[0]));
    }

    private static double PocOf(in OrderFlowBar bar)
        => bar.TryGetPoc(out double price, out _) ? price : double.NaN;
}
What an OrderFlowBar gives you
An OrderFlowBar is a value type holding the underlying Bar plus its per-price-level volume, built for allocation-free reads in tight render loops. The members you use most:

Member	Meaning
Bar	The OHLCV bar this order-flow snapshot belongs to.
LevelCount / GetLevel(i)	Number of price levels and the level at index i (index 0 = lowest price). A bar with no trades has zero levels.
TryGetLevelAtPrice(price, out level)	Looks up the level at a price. True when the price is inside the bar's range and on the tick grid.
TryGetPoc(out pocPrice, out pocVolume)	Point of Control: the level with the highest total volume. On a tie the lower price wins.
Delta / TotalBidVolume / TotalAskVolume / TotalTradeCount	Bar-level aggregates, precomputed, O(1). Delta is ask minus bid.
TotalUpTickVolume / TotalDownTickVolume	Uptick and downtick volume for the bar by the tick rule (a trade at an unchanged price keeps the previous direction). O(1); 0 when Stats is null.
LocalDeltaHigh / LocalDeltaLow	Highest and lowest running delta inside the bar, starting from 0 at the open (high >= 0, low <= 0). A cumulative-delta candle's wicks are the entering cumulative delta plus these.
TickSize / LowTicks / PriceOffset	The price grid: level i sits at (LowTicks + i) × TickSize + PriceOffset. PriceOffset is 0 except on derived bars whose grid is anchored off the tick grid (tick grouping).
Provenance / IsEstimated / HasTickRuleClassifiedVolume	How the bid/ask split was obtained: OrderFlowProvenance.Exact for a venue-stamped ladder, EstimatedFromBars when estimated from OHLCV bars, TickRuleClassified when some volume was credited by the tick rule because the venue sent no aggressor side (flags).
Each OrderFlowLevel carries Price, BidVolume, AskVolume, TradeCount, plus computed TotalVolume and Delta (ask minus bid). When the producer tracked a per-side print split, HasTradeCountSplit is true and GetBidTradeCount(i) / GetAskTradeCount(i) return it; otherwise they return 0. The full member list, including the helper types for building order flow yourself, is in the order flow & depth reference.

Per-bar statistics
Some quantities depend on the order of the trades and can only be computed while ticks are aggregated. When available, OrderFlowBar.HasStats is true and Stats (an OrderFlowBarStats) exposes:

RunningDeltaMax / RunningDeltaMin: running-delta extremes within the bar.
DeltaHighSetAfterLow: which extreme was set last (Sierra Chart's Finish AskVol BidVol Diff).
AskVolumeSinceHigh, BidVolumeSinceHigh, AskVolumeSinceLow, BidVolumeSinceLow, and the commitment-of-traders helpers CotHigh / CotLow.
FirstTradeUtc / LastTradeUtc and TradeSpan (last minus first). These are true UTC; to show them on the chart, convert with ChartDisplayTime.FromUtc(utc, ctx.DisplayTimeZone) and keep the field itself unchanged.
VolumePerSecond, FirstTickVolume, LastTickIsBid, LastTradePrice, LastTickRuleDirection.
UpTickVolume / DownTickVolume: the tick-rule totals, also surfaced as OrderFlowBar.TotalUpTickVolume / TotalDownTickVolume.
Bars built without a tick aggregator (empty bars, multi-timeframe seeding, ladders without trade order) carry Stats == null, so check HasStats first.

Where Stats is filled. Tick-built bars (live tape and historical tick replay, every bar type) carry Stats whenever an order-flow aggregator ran. Minute-resolution profiled ladders carry them only when the data source replays an aggressor-flagged tape in trade order; ladders without trade order, and estimated order flow, have Stats == null.
Backfill resolution
OrderFlowResolution sets the granularity of the history loaded for order flow: Tick (finest, exact aggressor and delta), Second, Minute (coarsest, fastest, with estimated bid/ask), and Auto (the default, derived from the chart's bar period: intraday charts get tick data, higher timeframes get second or minute data). When several indicators on a chart ask for different resolutions, the chart loads the finest one requested.

IndicatorBase implements IOrderFlowResolutionAware, so every indicator has an OrderFlowResolution property. The settings dialog shows it as a "Resolution" dropdown on indicators marked [RequiresOrderFlow].

What is lost at coarse resolution. Native second and minute bars carry no per-trade aggressor side, so bid/ask (and therefore delta) is estimated with an uptick/downtick rule. Volume at price stays accurate; delta-sensitive tools (footprint, delta) see an approximation. The forming bar is always built from live ticks and is exact whatever the setting.
Open Interest
For the venue-reported open interest, tag the indicator with [RequiresOpenInterest] and read ctx.OpenInterest(seriesIndex): a scalar series aligned 1:1 with the bars, with double.NaN on bars that report no open interest. The attribute steers backfill routing; the channel itself is always reachable. Hosts without an open-interest channel return an empty series, so read defensively (TryGet, and treat a miss as NaN).

Order flow gives you the data; turning it into footprint cells, profile columns or POC lines is a rendering job. See Custom rendering.

Dynamic order-flow requirement
When the need for order flow depends on runtime configuration instead of being fixed for the class, implement IDynamicOrderFlowRequirement, the runtime counterpart of [RequiresOrderFlow]:

public interface IDynamicOrderFlowRequirement
{
    bool RequiresOrderFlow { get; }
    event Action? RequirementChanged;
}
The host reads RequiresOrderFlow whenever it plans a chart's backfill (chart build, workspace restore, adding the indicator). Raise RequirementChanged when a configuration change turns the need on; if the chart was built without historical order flow, the host reloads it with order flow enabled.

