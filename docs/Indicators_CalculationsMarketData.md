Indicators
Calculation & market data
Two independent opt-ins control how often your code runs: a calculation mode sets the cadence of OnBarUpdate, and a separate flag delivers raw ticks. This page also covers depth, alerts and sessions.

On this page
Two separate knobs
Calculation mode
Raw market data
Depth & market-by-order
Alerts
Trading hours & sessions
Two separate knobs
Calculate (a CalculationMode) and ProcessesMarketData (a bool) are independent. The first chooses how often OnBarUpdate fires; the second opts into raw OnMarketData tick delivery. Pick either, both or neither. The runtime calls an indicator on the tick path only when it opts in.

Per-tick recalculation from the forming bar's OHLC: OnEachTick, ProcessesMarketData left false.
Raw trades with their aggressor side: OnBarClose and ProcessesMarketData = true.
A footprint-style indicator that needs both: set both.
Set them in your constructor (the setters are protected). The values must stay the same for the indicator's lifetime.

Calculation mode
Mode	Fires OnBarUpdate
OnBarClose (default)	Once per closed bar. Lowest CPU; the right choice for SMA/EMA/RSI and other closed-bar smoothers.
OnPriceChange	When the bar closes, and on every intra-bar update that changes the bar's close price.
OnEachTick	When the bar closes, and on every intra-bar update. Highest CPU; for tick-precision VWAP, footprint and volume-reactive logic.
In every mode the closing OnBarUpdate for a bar fires exactly once; the intra-bar modes add updates before it. The mode applies to the primary series: secondary series added with AddDataSeries fire OnBarUpdate once per closed bar. Inside the callback, read ctx.IsBarClosed to branch close and intra-bar logic, ctx.IsFirstTickOfBar to start a new output slot, and ctx.IsFirstBarOfSession to reset daily state.

a per-tick indicator
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MyVwap : IndicatorBase
{
    private readonly ChunkedSeries<double> _vwap = new();
    private double _sessionPV, _sessionVol;       // closed bars of the current session

    public MyVwap(IIndicator? parent = null) : base(parent)
    {
        Calculate = CalculationMode.OnEachTick;   // recompute on every tick
        AddPlot(new Plot("VWAP", _vwap, PlotStyle.Line, new ChartColor(255, 193, 7), 1.5));
    }

    // OnDataLoaded (the history pass) is left out to keep the example short.

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (ctx.IsFirstTickOfBar && ctx.IsFirstBarOfSession)
            _sessionPV = _sessionVol = 0;         // new session: start over

        Bar b = ctx.Bars(0)[0];                   // the forming bar, or the bar that just closed
        double typical = (b.High + b.Low + b.Close) / 3.0;
        double pv  = _sessionPV  + typical * b.Volume;
        double vol = _sessionVol + b.Volume;
        double vwap = vol > 0 ? pv / vol : double.NaN;

        if (ctx.IsFirstTickOfBar) _vwap.Append(vwap);     // new bar: new slot
        else                      _vwap.UpdateLast(vwap); // same bar: rewrite the tail

        if (ctx.IsBarClosed) { _sessionPV = pv; _sessionVol = vol; }  // commit the finished bar
    }
}
The bar's volume is cumulative within the bar, so the example adds the forming bar's contribution on top of the committed session totals instead of accumulating it on every tick. If you write your own gate, CalculationCadence.ShouldFire(mode, isClosed, closeChanged) implements the same rules the runtime uses.

Raw market data
Set ProcessesMarketData = true to receive OnMarketData(in Tick tick, IIndicatorContext ctx) on every live tick. A Tick (from SabrTrader.Pipeline.Ticks) carries Price, Size, ExchangeTimestampUtc and a TickFlags bitmask in Flags. Filter on the flags: a tick can be a trade (Trade, with AtAsk or AtBid for the aggressor side) or a quote update (Bid, Ask). On quote-driven venues that publish no trades (most forex and CFD venues) the prints are synthesized from the quote and carry QuoteDerived alongside Trade.

handling raw ticks
public MyTape(IIndicator? parent = null) : base(parent)
{
    Calculate = CalculationMode.OnBarClose;
    ProcessesMarketData = true;     // opt into OnMarketData
}

public override void OnMarketData(in Tick tick, IIndicatorContext ctx)
{
    if ((tick.Flags & TickFlags.Trade) == 0) return;   // ignore pure quote updates
    bool buyAggressor = (tick.Flags & TickFlags.AtAsk) != 0;
    _delta += buyAggressor ? tick.Size : -tick.Size;
}
OnMarketData fires for live ticks only. History arrives as bars: orderflow indicators read the pre-aggregated OrderFlowBar series in OnDataLoaded (see Orderflow). Live, this fires at the full tick rate (thousands per second on a busy instrument), so keep it allocation-free and do not Print per tick.
Depth & market-by-order
Depth and trades are separate streams. Set ProcessesMarketDepth = true to receive the order-book callbacks: OnMarketDepth(in DepthUpdate update, ctx) (aggregated Level 2), OnMarketByOrder(in MboEvent evt, ctx) (Level 3), OnMarketByOrderLoss(long lostEvents, ctx) and OnOrderBookLifecycle(OrderBookLifecycleEvent evt, ctx). The chart applies each event to the shared ctx.OrderBook before your callback runs, so a read of the book inside the callback sees consistent state. All four are default no-ops; override the ones you need. The callbacks are serialised per indicator: no two of them run at the same time.

What the callbacks deliver
The shared book holds every event; the callbacks deliver a sample. On a live chart the host drains the instrument's queued MBO events once per worker pass and hands your indicator the newest one; the events it skipped are still applied to the book. OnMarketByOrderLoss reports the skipped events before the event that follows them, with an exact count that also includes anything the delivery ring discarded under load (that part may be reported one pass early, never twice). In a quiet market most passes deliver one event and no loss notice; in a busy one loss notices are constant and expected. Read the book (ctx.ReadOrderBook) for state, and treat OnMarketByOrder as the cue to do so. An indicator that seeds itself from the book calls ctx.TryReadOrderBook instead: it returns false when there was no book to read, which tells a cold start on a chart without a depth feed apart from a host fault.

From the callbacks alone an indicator can build sampled-flow and current-book studies. Complete order lifecycles and queue persistence need the full record, and the deltas of the book's MboAddCount / MboModifyCount / MboCancelCount will differ from your callback count: those counters record apply outcomes on a book shared by every consumer of the instrument (see IOrderBook). For the complete record use the recorded tape through ctx.RecordedDepth (IRecordedDepthCatalog), which carries every event the recorder received plus markers for each reset, venue re-image and capture gap.

An indicator that joins an instrument already streaming first receives the current book replayed as MboAction.Add events stamped with the join time. They describe orders already resting, are not applied to the book again, and leave its counters unchanged.

Book lifecycle
State you derive from the callbacks is only as good as the book it came from, and the book is cleared and rebuilt during a session: on a feed reset, and whenever the venue re-images the instrument after a recovery. OnOrderBookLifecycle tells you when:

Reset: the book was cleared because the feed subscription was invalidated (usually a connection loss), and no rebuild is announced. Drop derived state and rebuild it from the events that follow.
RebuildStarted: the book was cleared and the venue's fresh image is on its way.
RebuildCompleted: the image is applied and the book is consistent again. Re-derive your state from ctx.ReadOrderBook here.
DepthModeChanged: ctx.OrderBook.DepthMode switched between PerOrder and Aggregated. After a switch to Aggregated, per-order features stop describing the venue's book.
CaptureGap: raised only when a recorded tape is replayed (backtests, market replay). The tape has a hole of unknown size here; the book was cleared and is incomplete until the next RebuildCompleted.
The notice runs on the same serialised path as the event callbacks but is not ordered against events still queued for delivery, so a few pre-transition events may arrive after RebuildStarted. Re-deriving at RebuildCompleted is therefore more reliable than tracking the image events yourself.

a DOM indicator
public MyDom(IIndicator? parent = null) : base(parent)
{
    ProcessesMarketDepth = true;    // opt into the order-book callbacks
}

public override void OnMarketByOrder(in MboEvent evt, IIndicatorContext ctx)
{
    // The newest event of this pass. The book already has every event applied.
    ctx.ReadOrderBook(book => Sample(book));
}

public override void OnMarketByOrderLoss(long lostEvents, IIndicatorContext ctx)
{
    _skippedSinceOpen += lostEvents;   // honest denominator for anything counted from callbacks
}

public override void OnOrderBookLifecycle(OrderBookLifecycleEvent evt, IIndicatorContext ctx)
{
    switch (evt)
    {
        case OrderBookLifecycleEvent.Reset:
        case OrderBookLifecycleEvent.RebuildStarted:
        case OrderBookLifecycleEvent.CaptureGap:
            _derived.Clear();                          // built from a book that no longer exists
            break;
        case OrderBookLifecycleEvent.RebuildCompleted:
            ctx.ReadOrderBook(book => Rebuild(book));  // the image is in; start again from the book
            break;
        case OrderBookLifecycleEvent.DepthModeChanged:
            _perOrder = ctx.OrderBook?.DepthMode == OrderBookDepthMode.PerOrder;
            break;
    }
}
Alerts
Raise a user-facing alert with ctx.Alert(message) for the common case, or the full overload for control over de-duplication and sound:

alerts
ctx.Alert("RSI crossed above 70");                    // simple; message is also the rearm id

ctx.Alert(
    id: "rsi-overbought",                             // rearm key: throttles repeats
    message: "RSI overbought",
    soundPath: "Alert2.wav",                          // bare filename resolves to the host sounds folder
    severity: AlertSeverity.Warning,                  // Info / Warning / Critical
    rearmSeconds: 60);                                // don't re-fire this id for 60s
AlertSeverity lives in SabrTrader.Pipeline.Alerts. While an id is inside its rearmSeconds window, further alerts with that id are dropped; use a stable id to throttle a repeating signal, or include the bar time in the id to fire once per bar.

Alerts fire live only. The host routes them through its sinks only when ctx.IsLive is true, so a historical or backtest pass stays silent without any gating in your code. Gate your other live-only side effects on ctx.IsLive yourself. See Alerts for the alert channels.
Trading hours & sessions
For session-aware indicators (daily VWAP, opening range, initial balance) the context exposes the instrument's trading calendar. ctx.TradingHours is the bound TradingHoursTemplate (null means 24/7), and ctx.CreateSessionIterator() returns a stateful ISessionIterator, the counterpart of NinjaTrader's session iterator. Create it once in OnInit or OnDataLoaded and keep it; each call returns a new instance. Pass a template to CreateSessionIterator(template) to iterate another instrument's sessions.

session iteration
using SabrTrader.Pipeline.Indicators.TradingHours;

private ISessionIterator? _sessions;

public override void OnInit(IIndicatorContext ctx)
{
    base.OnInit(ctx);
    _sessions = ctx.CreateSessionIterator();   // null on a stub context; production always returns one
}

public override void OnBarUpdate(IIndicatorContext ctx)
{
    var t = ctx.Bars(0)[0].Start;
    if (_sessions is not null && _sessions.IsNewSession(t, includesEndTimestamp: false))
    {
        _sessions.GetNextSession(t, includesEndTimestamp: false);
        ResetDailyAccumulators();
    }
}
ctx.IsFirstBarOfSession is the simpler signal when all you need is a reset at the open. Chart session iterators take Bar.Start / Bar.End (the chart timeline). ctx.DisplayTimeZone is the zone that timeline is stamped in; convert an external UTC instant with ChartDisplayTime.FromUtc before comparing it (see The Bar struct). The Trading hours & sessions page covers templates and the iterator in full.

