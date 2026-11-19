Indicators
Multi-timeframe
Subscribe to extra data series, such as a higher timeframe or a different instrument, and read them alongside your primary series, knowing which series fired each callback.

On this page
Adding a series
Knowing which series fired
Async backfill & OnDataSeriesReady
Aligning bars without look-ahead
Adding a series
An indicator starts with one series: the chart's, at index 0. Call ctx.AddDataSeries(BarSpecification spec) to subscribe to another. It returns a Task<int> that completes with the new series index once the backfill is under way. Subscribing a spec you already have is idempotent: the existing index comes back and no second series is allocated. Indexes are local to your indicator, so two indicators that add the same spec each get their own index for one shared series.

An added series is loaded as bars. Its historical ctx.OrderFlow(seriesIndex) entries are empty snapshots. If your indicator reads the order flow of a series it adds, say how much it reads: ctx.AddDataSeries(spec, OrderFlowDetail.Totals) for the per-bar totals (volume, buy volume, sell volume, trade count, delta), or ctx.AddDataSeries(spec, OrderFlowDetail.PriceLevels) for the full footprint. Mark the indicator [RequiresOrderFlow] as well.

Ask for no more than you read. Totals come from the venue's own bars where those carry the buy/sell split, which is a small download. Price levels on a series shorter than one minute are built from every trade of the lookback, which is a heavy one. With totals, read the totals (TotalAskVolume, TotalBidVolume, TotalTradeCount, Delta): historical bars then have no price levels, while live bars do.

Read the series through the index that call returns. A series another indicator added can have the same spec: a request shares it when it carries at least what you asked for, and gets a series of its own next to it when it carries less.

Build the secondary spec from the primary's instrument (ctx.Bars(0).Spec.InstrumentId) and pick any bar type the data source supports: TimeBarSpec, RangeBarSpec, VolumeBarSpec, TickBarSpec and so on (all in SabrTrader.Pipeline.Bars). A same-instrument spec inherits the primary series' trading-hours template and continuous-futures root, so you only set the bar type and period.

The usual pattern is to start the subscription in OnInit without waiting for it, remember the spec, and resolve the index by comparing specs once the series is there. If the subscription fails (for example no connected venue serves the symbol), the task faults, OnDataSeriesReady never fires for it, and the indicator keeps running on its primary series.

subscribing a higher timeframe
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;

private BarSpecification? _htfSpec;
private int _htf = -1;              // local index of the 60-minute series, -1 until resolved

public override void OnInit(IIndicatorContext ctx)
{
    base.OnInit(ctx);
    _htf = -1;

    // Same instrument as the primary, 60-minute bars.
    _htfSpec = new TimeBarSpec(ctx.Bars(0).Spec.InstrumentId, System.TimeSpan.FromMinutes(60));
    _ = ctx.AddDataSeries(_htfSpec);
}

private void ResolveHtf(IIndicatorContext ctx)
{
    if (_htf >= 1 || _htfSpec is null) return;
    for (int i = 1; i < ctx.SeriesCount; i++)
        if (ctx.Bars(i).Spec.Equals(_htfSpec)) { _htf = i; return; }
}
Knowing which series fired
In OnBarUpdate, ctx.BarsInProgress is the index of the series the callback fires for. Branch on it, and read any series with ctx.Bars(index). ctx.CurrentBars[index] gives the index of the newest bar on any local series (the same array NinjaScript calls CurrentBars), or -1 while that series has no bars. Use it for warmup guards on a series other than the one that fired. Every series follows your indicator's Calculate mode, so with the default OnBarClose each series fires once per closed bar.

routing per series
public override void OnBarUpdate(IIndicatorContext ctx)
{
    ResolveHtf(ctx);

    if (ctx.BarsInProgress == 0)
    {
        // a primary bar closed
        var bar = ctx.Bars(0)[0];
        double htfClose = LastClosedHtfClose(ctx, bar.EndUtc);   // see "Aligning bars" below
        _diff.Append(double.IsNaN(htfClose) ? double.NaN : bar.Close - htfClose);
    }
    else if (ctx.BarsInProgress == _htf)
    {
        // a 60-minute bar closed: update the higher-timeframe state here
    }
}
Async backfill & OnDataSeriesReady
A series added with AddDataSeries backfills asynchronously, usually after your OnDataLoaded has already run over the primary history. The runtime calls OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) once that series has finished backfilling and joined the live stream; seriesIndex is your local index for it. From then on ctx.Bars(seriesIndex) returns real bars. Any output you computed over history before this point had no secondary data, so the common pattern is to rebuild it here.

rebuilding when the secondary arrives
public override void OnDataLoaded(IIndicatorContext ctx) => Rebuild(ctx);

public override void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) => Rebuild(ctx);

private void Rebuild(IIndicatorContext ctx)
{
    ResolveHtf(ctx);
    _diff.Clear();
    var primary = ctx.Bars(0);
    for (int i = primary.Count - 1; i >= 0; i--)
    {
        var bar = primary[i];
        double htfClose = LastClosedHtfClose(ctx, bar.EndUtc);
        _diff.Append(double.IsNaN(htfClose) ? double.NaN : bar.Close - htfClose);
    }
}
To drop a series, call ctx.RemoveDataSeries(index). Removing index 0 (the primary) throws InvalidOperationException.

Aligning bars without look-ahead
ctx.Bars(_htf)[0] is the newest bar of the secondary series, which is not necessarily the bar that matches the primary bar you are processing. During a rebuild the secondary already holds its full history, and during live updates its newest bar can still be forming. Align by time instead: for a primary bar, use the latest secondary bar whose EndUtc is at or before the primary bar's EndUtc. That bar had closed when the primary bar closed, so the result is the same in backfill, live trading and backtests.

time-aligned lookup
private double LastClosedHtfClose(IIndicatorContext ctx, System.DateTime primaryEndUtc)
{
    if (_htf < 1 || ctx.CurrentBars[_htf] < 0) return double.NaN;
    var htf = ctx.Bars(_htf);
    for (int i = 0; i < htf.Count; i++)          // newest first
        if (htf[i].EndUtc <= primaryEndUtc) return htf[i].Close;
    return double.NaN;
}
Keep it linear on long histories. The lookup above scans from the newest bar. In a rebuild over many bars, walk both series oldest-first with a cursor into the secondary instead, so the whole pass stays O(n).
