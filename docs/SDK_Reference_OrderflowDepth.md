Reference
Orderflow & depth
The contracts a footprint / volume-profile / DOM plugin consumes: the per-bar orderflow model in SabrTrader.Pipeline.OrderFlow, and the live Level-2 / Level-3 market-depth surface in SabrTrader.Pipeline.MarketDepth.

On this page
OrderFlowBar · OrderFlowBarStats · OrderFlowResolution · OrderFlowWindowRequest · VolumeProfilePaintStyle · IOrderFlowWindowSource · IOrderFlowWindow · VolumeProfileStatus · VolumeProfileSnapshot · VolumeProfileRow
IMarketDepthFeed · IMarketByOrderFeed · IDepthFeedRouting · IOrderBook · OrderBook · OrderBookSnapshot · PriceLevel · OrderBookEntry · OrderBookLease · OrderBookNeed · DomBookView
DepthUpdate · DepthAction · MboEvent · MboAction · Side · OrderBookDepthMode · OrderBookLifecycleEvent · MboExecutionDetail · MboRowScope
Detections: IcebergEvent · IcebergPhase · StopRunEvent · StopRunDirection · DetectionsStatus
IRecordedDepthCatalog · DepthTapeMarker · Tape example
Orderflow: SabrTrader.Pipeline.OrderFlow
The per-bar orderflow model footprint-class indicators read, plus the volume-profile-window seam a drawing tool requests. Add using SabrTrader.Pipeline.OrderFlow; to reach these types.

OrderFlowBar
A readonly record struct: the OHLCV Bar plus parallel per-price-level bid/ask volume and trade-print counts. Prices are discretized to integer ticks; level i sits at (LowTicks + i) × TickSize + PriceOffset. Designed for allocation-free reads in tight render loops.

Member	Description
Bar Bar	The OHLCV bar this snapshot corresponds to.
long LowTicks	Integer tick offset of level index 0.
double TickSize	Price increment used for level indexing.
double PriceOffset	Constant added to every level price (0 for ordinary bars; non-zero only on derived / tick-grouped bars).
int LevelCount	Number of populated price levels (0 for an empty bar).
long TotalBidVolume / TotalAskVolume	Cross-level side totals. O(1) (precomputed in ctor).
long TotalTradeCount	Total trade prints across all levels. O(1).
long Delta	TotalAskVolume - TotalBidVolume (positive = net buying).
long TotalUpTickVolume / TotalDownTickVolume	Tick-rule uptick / downtick volume (Stats?.UpTickVolume / DownTickVolume, else 0). Distinct from aggressor ask/bid volume: same-price trades inherit the prior tick-rule direction.
long LocalDeltaHigh / LocalDeltaLow	Intra-bar running-delta extremes with a 0-open floor (high ≥ 0, low ≤ 0). Cum-delta candle high/low = entering_cum + these.
OrderFlowBarStats? Stats	Optional tick-derived statistics; null when the bar wasn't built from a tick stream.
bool HasStats	True when Stats is populated.
bool HasTradeCountSplit	True when per-side trade-print counts are available.
OrderFlowProvenance Provenance	How the bid/ask split was obtained, as a [Flags] enum : byte. Exact (0) = every trade carried its aggressor side from the venue, so the ladder is measured. EstimatedFromBars (1) = no tape at all: the bar's volume was spread uniformly over its range and credited whole to the side of its close-to-close direction: exact for volume-at-price, an estimate for anything delta-sensitive. TickRuleClassified (2) = at least one trade carried no aggressor side and was credited by the tick rule (above the previous trade = ask, below = bid, equal = the last direction); the rest of the bar may still be venue-stamped. A study that draws or alerts on delta should label an estimated value as an estimate.
bool IsEstimated / bool HasTickRuleClassifiedVolume	The two questions you normally ask Provenance: the ladder was estimated from OHLCV bars, and some or all of the volume was credited by the tick rule because the venue sent no aggressor side.
OrderFlowBar ShiftedByTicks(long ticks)	The same bar with every price moved by whole ticks: OHLC, every level (through LowTicks, so levels stay on the tick grid) and Stats.LastTradePrice. Volumes, totals, deltas and times are unchanged and the level arrays are shared, not copied. This is how an older futures contract's order flow is back-adjusted onto the next contract's price level. Identity for 0 ticks.
OrderFlowLevel GetLevel(int index)	Read one level by index (0 = lowest price). Throws on out-of-range.
long GetBidTradeCount(int index) / GetAskTradeCount(int index)	Per-side trade-print count at a level; 0 when the split wasn't tracked.
bool TryGetLevelAtPrice(double price, out OrderFlowLevel level)	Look up the level at a price; true only when it lands on a tick within range.
bool TryGetPoc(out double pocPrice, out long pocVolume)	Point of Control = highest-total-volume level (lower price wins on a tie).
OrderFlowBar WithChartDisplay(TimeZoneInfo tz)	Stamps the wrapped Bar's StartDisplay / EndDisplay without copying the level arrays. Identity when the bar stamp is a no-op (UTC zone, day-and-longer bars, or a default-struct with no levels).
OrderFlowBar WithoutChartDisplay()	Drops chart-zone stamps on the wrapped bar so a cache write cannot persist display wall-clock. Ladder arrays, stats, and UTC identity are unchanged.
Constructors. Three overloads:

OrderFlowBar.cs
// Full-fidelity: bid/ask/trade-count arrays + running-delta extremes (validated).
public OrderFlowBar(Bar bar, long lowTicks, double tickSize,
    long[] bidVolumes, long[] askVolumes, long[] tradeCounts,
    long localDeltaHigh, long localDeltaLow,
    long[]? bidTradeCounts = null, long[]? askTradeCounts = null,
    OrderFlowBarStats? stats = null, double priceOffset = 0,
    OrderFlowProvenance provenance = OrderFlowProvenance.Exact);

// Defaults the running-delta extremes to 0 (empty / no-tick bars).
public OrderFlowBar(Bar bar, long lowTicks, double tickSize,
    long[] bidVolumes, long[] askVolumes, long[] tradeCounts);

// Volume-only: every level's TradeCount = 0 (seeding, fixtures).
public OrderFlowBar(Bar bar, long lowTicks, double tickSize,
    long[] bidVolumes, long[] askVolumes);
OrderFlowLevel: one price row, a readonly record struct returned by GetLevel / TryGetLevelAtPrice:

Member	Description
double Price	Price at this level.
long BidVolume / AskVolume	Volume credited to bids (sell-aggressor) / asks (buy-aggressor).
long TradeCount	Number of distinct trade prints at this price.
long TotalVolume	BidVolume + AskVolume.
long Delta	AskVolume - BidVolume.
OrderFlowBarStats
A readonly record struct of per-bar quantities that can only be computed during tick aggregation (they depend on tick order). Carried by OrderFlowBar.Stats; null when no tick aggregator ran.

OrderFlowBarStats.cs
public readonly record struct OrderFlowBarStats(
    long RunningDeltaMax,
    long RunningDeltaMin,
    long AskVolumeSinceHigh,
    long BidVolumeSinceHigh,
    long AskVolumeSinceLow,
    long BidVolumeSinceLow,
    double VolumePerSecond,
    bool? LastTickIsBid = null,
    long FirstTickVolume = 0,
    System.TimeSpan? TradeSpan = null,
    long UpTickVolume = 0,
    long DownTickVolume = 0,
    double? LastTradePrice = null,
    int LastTickRuleDirection = 0,
    bool? DeltaHighSetAfterLow = null,
    DateTime? FirstTradeUtc = null,
    DateTime? LastTradeUtc = null);
Member	Description
long RunningDeltaMax / RunningDeltaMin	Highest / lowest the intra-bar running delta reached (no 0 floor).
long AskVolumeSinceHigh / BidVolumeSinceHigh	Volume traded since the bar last made its high.
long AskVolumeSinceLow / BidVolumeSinceLow	Volume traded since the bar last made its low.
double VolumePerSecond	Short-window volume rate.
bool? LastTickIsBid	Whether the bar's last classified tick hit the bid (sell-aggressor). Null when untracked.
long FirstTickVolume	Size of the bar's first classified trade.
System.TimeSpan? TradeSpan	Elapsed time from the bar's first to last print (LastTradeUtc − FirstTradeUtc). Null when untracked. The endpoints are FirstTradeUtc / LastTradeUtc; read them there rather than reconstructing them from this span plus the bar window.
long UpTickVolume / DownTickVolume	Tick-rule uptick / downtick volume for the bar. Surfaced on the bar as TotalUpTickVolume / TotalDownTickVolume.
double? LastTradePrice	Last trade price in the bar, used to continue the tick rule across the history-to-live seed without a discontinuity.
int LastTickRuleDirection	Last established tick-rule direction at bar end: +1 up, −1 down, 0 none yet.
bool? DeltaHighSetAfterLow	Which running-delta extreme updated last: true = high, false = low. Null when no exclusive update has happened yet (empty bar, or the opening print where max == min). Same >/< rule as the extremes themselves. The "finish" delta (delta after the last extreme) is barDelta − (DeltaHighSetAfterLow ? RunningDeltaMax : RunningDeltaMin); when this is null the finish is 0. Distinct from AskVolumeSinceHigh/SinceLow, which are volume since the bar's price high/low.
DateTime? FirstTradeUtc / LastTradeUtc	Exchange timestamp of the bar's first / last trade print: true venue UTC, always, and never rewritten to a display-zone clock. Null when untracked. These are the endpoints of TradeSpan; the span plus the bar window does not give them back, because the first print is not necessarily the bar open. Overlay them on the chart with ChartDisplayTime.FromUtc(stats.FirstTradeUtc.Value, ctx.DisplayTimeZone) and compare to Bar.Start / Bar.End, not StartUtc. TradeSpan is a duration and does not change with the display zone.
long CotHigh	AskVolumeSinceHigh - BidVolumeSinceHigh: the delta traded since the bar's high.
long CotLow	AskVolumeSinceLow - BidVolumeSinceLow: the delta traded since the bar's low.
static OrderFlowBarStats Empty	All-zero stats.
Stats is filled on tick-built bars and on profiled 1-minute ladders a venue builds by replaying an aggressor-flagged tape. A venue whose profiled ladder carries per-price volume without print order leaves it null.

OrderFlowAggressorClassifier & AggressorSplit
The platform's tick-rule classifier, for when you build a ladder yourself from a tape whose prints carry no aggressor side. Feed it trades in order; it credits each to bid or ask and remembers the direction so an unchanged price inherits the previous one, which is why it is stateful.

classifying a print
using SabrTrader.Pipeline.OrderFlow;

var classifier = new OrderFlowAggressorClassifier();

AggressorSplit split = classifier.Classify(price, size, tick.Flags);
// split.BidVolume / AskVolume / BidPrints / AskPrints, plus Delta and HitBid.
// split.ByTickRule is true when the SIDE was inferred rather than venue-stamped;
// split.HadPriorTrade is false for the first print, where no direction exists yet.

classifier.Reset();                                   // new session
classifier.Restore(previousPrice, direction);         // resume where a recorded run left off
Direction and PreviousPrice expose the state so it can be persisted and restored, and OrderFlowProvenance.TickRuleClassified is what a bar built this way should carry (see OrderFlowBar).

OrderFlowTapeStatsAccumulator is the matching mutable struct for the per-bar statistics: call OnTrade(DateTime utc, long size, long delta, bool lastTickIsBid, double price) for each trade folded into the bar, then Build() for its OrderFlowBarStats. Start a new accumulator per bar.

OrderFlowResolution
Backfill granularity an orderflow consumer requests. Coarser = far less data = faster backfill, at the cost of aggressor fidelity. Settings persist the value by name.

Value	Meaning
Auto = 0	Default. The host picks the concrete resolution from the chart's primary bar period. Not a point on the fidelity scale.
Tick = 1	Full tick data: exact per-trade aggressor, exact volume-at-price and delta. Heaviest backfill.
Second = 2	Native 1-second OHLCV; aggressor estimated per second. Bid/ask approximate.
Minute = 3	Native 1-minute OHLCV; aggressor estimated per minute. Coarsest approximation.
Note: at Second/Minute, volume-at-price (Volume Profile / TPO) stays accurate but delta is estimated with an uptick/downtick rule. The current forming bar is always built from live ticks and is exact.
OrderFlowWindowRequest
An immutable readonly record struct that says what volume-at-price profile a drawing wants for a time window: what to compute. Value equality tells whether your drawing's window changed: the same window and settings keep the profile your drawing holds, and dragging an anchor or changing a setting means a new one is built. How the profile looks is VolumeProfilePaintStyle, which never rebuilds.

OrderFlowWindowRequest.cs
public readonly record struct OrderFlowWindowRequest(
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    OrderFlowResolution Resolution,
    int TickAggregation,          // ticks per row; 0 = auto (sized to the window's price range)
    double ValueAreaPercentage,
    bool EnableSmoothing,
    int SmoothingPeriod,
    int HighVolumeNodeCount,
    int LowVolumeNodeCount,
    int HighVolumeRegionSize,
    int LowVolumeRegionSize)
{
    bool HasWindow { get; }                       // true when ToUtcExclusive > FromUtc
    bool WantDeveloping { get; init; }            // build developing POC / VA / VWAP lines
    ValueAreaBuild ValueAreaBuild { get; init; }  // grow the value area one or two rows per step
    ProfileRightEdge RightEdge { get; init; }     // FixedTime, or LatestBar (keeps growing live)
    double MinOrderSize { get; init; }            // count only orders of at least this size; 0 = all
    Guid DrawingId { get; init; }                 // the drawing this window belongs to
}
With RightEdge = ProfileRightEdge.LatestBar the window is open-ended: pass DateTime.MaxValue as ToUtcExclusive and the host keeps the profile growing with every live trade. Set DrawingId to your drawing's id: the host reports changes of the profile's levels (POC, value area high and low) under it.

VolumeProfilePaintStyle
An immutable sealed record that says how the host paints the profile. Every member has a default (the Fixed Volume Profile drawing's look), so set only what you change with an object initializer or with. A change here only repaints; nothing is rebuilt.

Group	Members
Histogram	HistogramOrientation, ColorMode, ProfileMeasure (volume, delta, trades or time at price), OpacityPercent, ProfileWidthPercent, RowSpacingPx, HighlightPocRow, ShowOutline, ShowDeltaHistogram
Numbers	VolumeText, ShowTotal
Auction marks	ShowTpoOutline, TpoOutlineWidthPx, ShowSinglePrints, ShowPoorHighLow, AuctionLineWidthPx
Levels and labels	LevelDisplay, LabelContent, LabelSide, LevelLineWidthPx, ShowVwap, ShowHigh, ShowLow, ShowMid, ShowSecondPoc, ShowVwapBands, ExtendLinesRight, ExtendUntil
Developing lines	ShowDevelopingPoc, ShowDevelopingValueArea, ShowDevelopingVwap, DevelopingLineWidthPx (also set WantDeveloping on the request)
Colours	VolumeColor, ValueAreaColor, BuyColor, SellColor, OutsideBuyColor, OutsideSellColor, HeatmapLowColor, HeatmapHighColor, PositiveDeltaColor, NegativeDeltaColor, OutlineColor, PocColor, VahColor, ValColor, VwapColor, SecondPocColor, VwapBandColor, HighColor, LowColor, MidColor, HvnColor, LvnColor, VolumeTextColor, TotalColor, TpoInsideColor, TpoOutsideColor, SinglePrintColor, PoorHighLowColor
The text the profile paints (labels, row numbers, the total) uses a ProfileTextFont(string? Family, double SizePx, bool Bold, bool Italic), normally your drawing's own font settings.

IOrderFlowWindowSource
The host that builds volume profiles for your drawing. Reach it through IDrawingToolRenderContext.OrderFlowWindows (null on a host that has none). Your drawing owns the profile it gets: Open starts building the profile of a request and hands it to you as an IOrderFlowWindow, which your drawing keeps while it shows that window. The host keeps no list of profiles, so a chart can show any number of them.

IOrderFlowWindowSource.cs
public interface IOrderFlowWindowSource
{
    // Start building the profile of the request's window and return it. Never blocks.
    // Every call opens a new window: call it only when your drawing has none or its request changed.
    IOrderFlowWindow Open(in OrderFlowWindowRequest request);
}
IOrderFlowWindow
The profile one drawing holds. There are two ways to use it, and both read the same build:

TryGetProfile: you get the rows and levels and paint the profile yourself.
Paint: the host paints it with the Volume Profile indicator's engine and renderers, in the look a VolumeProfilePaintStyle sets.
IOrderFlowWindow.cs
public interface IOrderFlowWindow : IDisposable
{
    IOrderFlowWindowSource Source { get; }       // the host that built it
    OrderFlowWindowRequest Request { get; }       // what it is built for

    // The profile, for a drawing that paints it itself. Never blocks.
    VolumeProfileStatus TryGetProfile(IDrawingToolRenderContext ctx, out VolumeProfileSnapshot? profile);

    // The host paints the profile between the chart X of the window's start and end times. Never blocks.
    VolumeProfileStatus Paint(VolumeProfilePaintStyle style, in ProfileTextFont font, IDrawingToolRenderContext ctx);
}
Call either one from your drawing's render method. While the profile is not built it returns Building; when the build lands, or a LatestBar window grows with new trades, the host repaints the chart and the next frame gets the profile. Your drawing draws its own box, handles and placeholder. Dispose stops a LatestBar window following the chart's trades; a fixed window has nothing to release.

Holding the profile in your drawing
A drawing that derives from DrawingToolBase holds one profile with one call in its render method:

DrawingToolBase.cs
// The profile this drawing holds for the request, opened on the chart's host when the drawing has none,
// its request changed or its chart got a new host; the one it replaces is disposed. Null when the chart
// has no order-flow host or after the drawing left its chart.
protected IOrderFlowWindow? Profile(IDrawingToolRenderContext ctx, in OrderFlowWindowRequest request);

// The profile this drawing holds, or null.
public IOrderFlowWindow? HeldProfile { get; }

// Called by the chart when the drawing is put on it or leaves it. OnRemoved stops a held LatestBar profile.
// Overrides call the base.
public virtual void OnAdded();
public virtual void OnRemoved();
The same request returns the same profile with no allocation, so calling Profile every frame is free. A drawing that needs several profiles at once calls ctx.OrderFlowWindows.Open itself, keeps what it gets and disposes each in OnRemoved.

VolumeProfileStatus
Value	Meaning
Building	Being built in the background. Show a placeholder; the host repaints when it lands.
Ready	The profile is available (Paint: it was painted).
Empty	Built, but the window holds no traded volume. Draw the box only.
Unavailable	No order-flow licence (trial or paid). Nothing is built or painted. Draw the box only.
VolumeProfileSnapshot
The profile TryGetProfile hands you. It never changes after you get it, so you may keep it across frames and threads. A new instance means the profile changed; the same instance means nothing changed, so you can cache what you derive from it by reference. With EnableSmoothing on the request, the rows, totals and levels are the smoothed values the profile is drawn from, not the raw traded volume.

Member	Description
ReadOnlySpan<VolumeProfileRow> Rows	The rows, sorted by price from low to high. Read them with foreach (ref readonly var row in profile.Rows); no allocation.
double RowHeight	The price span of one row.
double Poc	The point of control: the centre price of the row with the most volume. NaN when the window holds no volume.
double ValueAreaHigh, double ValueAreaLow	The value area's edge prices. NaN when the window holds no volume.
double High, double Low	The centre prices of the highest and lowest row. NaN when there are no rows.
double TotalVolume	All volume in the window.
double MaxRowVolume	The volume of the fullest row: divide a row's volume by it to scale the row's width.
VolumeProfileRow
One price row, a readonly struct.

Member	Description
double Price	The row's centre price; the row spans Price ± RowHeight / 2.
double Volume	All volume at the row: buy, sell and volume traded between bid and ask.
double BuyVolume	Volume traded at or above the ask.
double SellVolume	Volume traded at or below the bid.
double Delta	BuyVolume - SellVolume.
bool InValueArea	Whether the row lies inside the value area.
Examples
Paint the profile yourself:

MyProfileBox.cs
var request = new OrderFlowWindowRequest(fromUtc, toUtc, OrderFlowResolution.Auto,
    TickAggregation: 0, ValueAreaPercentage: 70, EnableSmoothing: false, SmoothingPeriod: 0,
    HighVolumeNodeCount: 0, LowVolumeNodeCount: 0, HighVolumeRegionSize: 1, LowVolumeRegionSize: 1)
{ DrawingId = Id };

VolumeProfileSnapshot? profile = null;
switch (Profile(ctx, in request)?.TryGetProfile(ctx, out profile))
{
    case VolumeProfileStatus.Ready:
        foreach (ref readonly var row in profile!.Rows)
        {
            double y     = ctx.Viewport.PriceToPixelY(row.Price + profile.RowHeight / 2);
            double h     = ctx.Viewport.PriceToPixelY(row.Price - profile.RowHeight / 2) - y;
            double width = boxWidth * row.Volume / profile.MaxRowVolume;
            ctx.Renderer.FillRect(xLeft, y, width, h, row.InValueArea ? InsideColor : OutsideColor);
        }
        break;
    case VolumeProfileStatus.Building:
        ctx.Renderer.DrawText(xLeft + 8, yMid, "Calculating…", Color, 11);
        break;
    // Empty or Unavailable: draw the box only
}
Let the host paint it:

MyProfileBox.cs
var style = new VolumeProfilePaintStyle { ShowOutline = true, ProfileWidthPercent = 60 };
var font  = new ProfileTextFont(null, 11, false, false);

if (Profile(ctx, in request)?.Paint(style, in font, ctx) == VolumeProfileStatus.Building)
    ctx.Renderer.DrawText(xLeft + 8, yMid, "Calculating…", Color, 11);
Licence. Order flow is a licensed feature. On a session without an order-flow licence (trial or paid), both calls return Unavailable and nothing is built or painted. When the licence changes, the host repaints the chart, so the profile appears or goes away by itself.
Market depth: SabrTrader.Pipeline.MarketDepth
The live order-book surface. A venue may offer aggregated Level-2 (IMarketDepthFeed), per-order Level-3 / MBO (IMarketByOrderFeed), or both; the OrderBook unifies whichever arrives. Add using SabrTrader.Pipeline.MarketDepth;.

IMarketDepthFeed
Live aggregated Level-2 depth feed. Emits DepthUpdate per price-level change. Events may arrive on any thread; the feed reference-counts per instrument so subscribers share one upstream connection.

IMarketDepthFeed.cs
public interface IMarketDepthFeed : IDisposable
{
    IDisposable Subscribe(string instrumentId, Action<DepthUpdate> onUpdate);
    // Also hear about updates the feed dropped before the next one it delivers.
    // Default forwards to the two-argument form: a synchronous fan-out never drops.
    IDisposable Subscribe(string instrumentId, Action<DepthUpdate> onUpdate, Action<long> onLoss);
    event Action? SubscriptionReset;   // drop state; default no-op
}
SubscriptionReset fires when consumers must drop all depth state (typically a connection loss). Disposing the token from Subscribe unsubscribes; the upstream sub is torn down when the last subscriber drops.

IMarketByOrderFeed
Live Market-By-Order (Level-3) feed. Emits MboEvent per resting-order lifecycle event (Add / Modify / Cancel). MboEvent.OrderId must be the venue's exchange-supplied id, stable across Modify (including a price move).

IMarketByOrderFeed.cs
public interface IMarketByOrderFeed : IDisposable
{
    IDisposable Subscribe(string instrumentId, Action<MboEvent> onEvent);
    // True only while the stream is known to carry every resting order's events, so the sizes
    // in a price's queue add up to what truly rests ahead. Default false: not known.
    bool DeliversEveryOrder { get; }
    // Also hear about events the feed dropped before the next one it delivers: the
    // count arrives before that event. Default forwards to the two-argument form
    // (a synchronous fan-out never drops); the hosted feed's per-instrument drain
    // pump overrides it, and the recorder subscribes this way.
    IDisposable Subscribe(string instrumentId, Action<MboEvent> onEvent, Action<long> onLoss);
    // Which execution rows (Trade / Fill / EventEnd) this stream carries. Default None.
    MboExecutionDetail ExecutionDetail { get; }
    // Choose the rows: Book (what the other overloads deliver) or All (book and execution
    // rows in stream order, for a consumer that stores the stream as it came, the recorder).
    IDisposable Subscribe(string instrumentId, Action<MboEvent> onEvent, Action<long> onLoss, MboRowScope scope);
    event Action? SubscriptionReset;   // clear book on connection loss; default no-op
    // One instrument's book was reset, re-imaged (RebuildStarted / RebuildCompleted)
    // or changed authority. Args: instrumentId, transition. Default no-op.
    event Action<string, OrderBookLifecycleEvent>? BookLifecycleChanged;
}
Loss heals itself, on the tape too. After the hosted feed reports a loss to a consumer that asked for it (the recorder), it asks the venue for a fresh image of the instrument, storm-guarded per instrument (5 s cooldown, doubling to 60 s). The image arrives as BookLifecycleChanged RebuildStarted / RebuildCompleted around the image events, and the recorder writes those as ReimageBegin / ReimageEnd, so a tape reads Gap, a few live deltas, then a bracketed image: the book is certifiably consistent again from the ReimageEnd. Plain consumers falling behind trigger nothing; the shared book they read never missed an event.
How a venue serves this feed. It creates a hosted feed from IMarketDepthHost (on its session context), injects raw book events into it, and exposes that same object as IDataProvider.Mbo. Sequencing, application to the shared book, late-subscriber seeding and consumer fan-out all happen behind Inject, the same way for every venue at every rate. See the hosted depth plane and the venue-seam reference.
IDepthFeedRouting
Optional capability of a data provider that fronts several underlying feeds: one venue login with several datasets behind a single IDataProvider, where Depth and Mbo route each instrument to the dataset that carries it. The routing surface itself keeps no order books, so when the host needs the shared book for an instrument (a chart's order book, a DOM) it asks this capability which underlying feed serves that instrument and reads the books from that feed. Several datasets of one login may share one set of books. The host probes it with AsCapability<IDepthFeedRouting>(); a provider with one depth feed per session leaves it out, and the host reads Depth / Mbo directly.

IDepthFeedRouting.cs
public interface IDepthFeedRouting
{
    // The MBO feed that serves instrumentId, or null when none does.
    IMarketByOrderFeed? MboFeedFor(string instrumentId);

    // The aggregated depth feed that serves instrumentId, or null when none does.
    IMarketDepthFeed? DepthFeedFor(string instrumentId);
}
Return the same feed object your routed Mbo / Depth would hand that instrument's subscription to. Null means no feed keeps books for it, and the consumer builds a local book from its own subscription; it never borrows another venue's book.

MyRoutedProvider.Depth.cs
using SabrTrader.Pipeline.MarketDepth;
using SabrTrader.Pipeline.Providers;

// The other half of this partial class implements IDataProvider. Its Depth and Mbo
// forward each subscription to the dataset that lists the instrument.
public sealed partial class MyRoutedProvider : IDepthFeedRouting
{
    private readonly IReadOnlyDictionary<string, IDataProvider> _datasetByInstrument;

    public IMarketByOrderFeed? MboFeedFor(string instrumentId)
        => _datasetByInstrument.TryGetValue(instrumentId, out var dataset) ? dataset.Mbo : null;

    public IMarketDepthFeed? DepthFeedFor(string instrumentId)
        => _datasetByInstrument.TryGetValue(instrumentId, out var dataset) ? dataset.Depth : null;
}
OrderBookLease
An indicator's claim on its host's order book, from IIndicatorContext.LeaseOrderBook(in OrderBookNeed) (namespace SabrTrader.Pipeline.MarketDepth). Every host that has a book serves it: a chart, a strategy run (live from the venue, in a backtest from the recorded tape), a Market Analyzer row and the SuperDOM, at any depth of the indicator tree. While at least one lease is open the host subscribes the venue's depth and market-by-order streams; when the last one is disposed it lets them go. The lease always follows the host's current book: after a reconnect or a feed rebind the host moves it and re-applies your need. An indicator that only wants the callbacks sets ProcessesMarketDepth and leaves the lease to the framework.

Member	Description
DomBookView? CurrentView	The newest published view. Null while there is no book yet, the lease asks for events only, or the session has no order-flow access.
DomBookView? LatestView()	The book's newest state, with any change still waiting for the next publish materialised first. Use it where you read once (at a bar close, when you attach); per-event code reads CurrentView.
IDisposable SubscribeViews(Action<DomBookView> onView)	Every published view of the current book until you dispose the handle, across book swaps. Called on the book's publishing thread: keep it short. Read LatestView() after subscribing to show the book at once.
void Reconfigure(in OrderBookNeed need)	Ask for more levels or per-order detail. A shared book's view only grows: another reader may need more than you.
RecordedDepthLookback? Lookback	Where the instrument's recorded book is, for a reader that shows the book from before it attached; null on hosts without recordings.
void Dispose()	Give the claim back. Idempotent. Call it from OnDispose.
OrderBookNeed
What a reader asks of the book (readonly record struct OrderBookNeed(int Levels, DomSegmentDetail SegmentDetail, int IcebergWindowMs)).

Member	Description
static OrderBookNeed Events	The callbacks only; no view is built for this reader.
static OrderBookNeed View(int levels, DomSegmentDetail detail = TotalsOnly, int icebergWindowMs = 1500)	A view of levels price levels per side, counted from the touch. DomSegmentDetail.FullSegments adds the orders per level and costs the book work on every update: ask for it only when you read orders.
bool WantsView	True when Levels is above 0.
DomBookView
One published, immutable state of the book (SabrTrader.Pipeline.MarketDepth.Hub): read it on any thread, keep a reference as long as you like. Version grows with every publish; Snapshot is a DomBookSnapshot (SabrTrader.Pipeline.MarketDepth.Dom) with InstrumentId, CapturedAtUtc, BestBidPrice / BestAskPrice and the levels per side, best first, in Bids and Asks. Each level carries Price, Side, AggregateSize and a Summary (OrderCount, TotalVolume, and the Segments per order when the need asked for FullSegments).

reading levels from a lease
private OrderBookLease? _book;

public override void OnInit(IIndicatorContext ctx)
{
    base.OnInit(ctx);
    _book ??= ctx.LeaseOrderBook(OrderBookNeed.View(10));   // null on a host without a book
}

public override void OnBarUpdate(IIndicatorContext ctx)
{
    if (_book?.LatestView() is not { } view) return;
    long bids = 0, asks = 0;
    var s = view.Snapshot;
    for (int i = 0; i < s.Bids.Count; i++) bids += s.Bids[i].AggregateSize;
    for (int i = 0; i < s.Asks.Count; i++) asks += s.Asks[i].AggregateSize;
    // ... use the 10-level totals
}

public override void OnDispose()
{
    _book?.Dispose();
    base.OnDispose();
}
IOrderBook
Read-only view onto a live OrderBook, the surface exposed via IIndicatorContext.OrderBook. The mutation API stays on the concrete OrderBook; indicator code only reads. For price levels read a leased view, which needs no lock. For per-order reads use IIndicatorContext.ReadOrderBook (or TryReadOrderBook), which holds the host's book lock; callers that need a frozen copy use OrderBookSnapshot.Capture there.

Member	Description
string InstrumentId	The instrument this book tracks.
double BestBidPrice / BestAskPrice	Top of book, or double.NaN when the side is empty.
double Spread	Ask − bid, or NaN when either side is empty.
int OrderCount	Total per-order count across both sides. Zero in L2-only mode.
int BidLevelCount / AskLevelCount	Distinct price levels per side.
IEnumerable<PriceLevel> EnumerateBids()	Bid levels top of book first (highest price). Live view.
IEnumerable<PriceLevel> EnumerateAsks()	Ask levels top of book first (lowest price). Live view.
PriceLevel? GetLevel(Side side, double price)	Level at an exact price; null when absent.
bool TryGetEntry(string orderId, out OrderBookEntry? entry)	Look up a single resting order by id (MBO mode); false when absent.
event Action<OrderBookEntry, DateTime>? OrderRemoved	Raised when an MBO order leaves the book (Cancel / consumed), with its last state and the event time.
OrderBookDepthMode DepthMode	Which feed currently authors the book: PerOrder while the host has marked a market-by-order stream authoritative, Aggregated otherwise. Can change mid-session; announced through OrderBookLifecycleEvent.DepthModeChanged.
long MboAddCount / MboModifyCount / MboCancelCount	Monotonic counts of what applying an event did to this book instance. Not wire actions: an Add for an id already resting counts as a Modify, a Modify that drains the size to zero removes the order but counts as a Modify, and an event for an id the book never saw counts only as a miss. Never reset, and on a live chart the book is shared by every consumer of the instrument. Their deltas are not meant to match one indicator's callback count; count your own callbacks when you need that.
long MboDuplicateAddCount / MboModifyMissCount / MboCancelMissCount	Miss / duplicate diagnostics. The health read is MboAddCount far above the two miss counts; sustained misses mean Modify/Cancel events whose Add never landed (image/live race or an upstream symbol-key mismatch).
int PeakOrderCount / PeakLevelCount	Peak observed counts since construction.
Thread-safety: the live book is mutated under a feed-side lock. Read it inside ReadOrderBook / TryReadOrderBook, and capture an OrderBookSnapshot there to hand to a render thread.
OrderBook
The concrete sealed class OrderBook : IOrderBook for one instrument. Accepts both aggregated L2 and per-order MBO events into a unified per-level surface. Not internally synchronised: the feed code that owns it holds the lock. Construct your own for offline work such as replaying a recorded tape.

Member	Description
OrderBook(string instrumentId)	Construct for an instrument (throws on blank id).
void ApplyDepth(in DepthUpdate update)	Apply one aggregated L2 event. Set creates/replaces a level's size; Delete (or size 0) removes it.
void ApplyMbo(in MboEvent evt)	Apply one MBO event (Add / Modify / Cancel). Unknown id on Modify/Cancel is a silent no-op (a price-move Modify resurrects the order).
void Clear()	Drop every level + per-order entry (used on reconnect).
bool TryGetOrder(string orderId, out Side side, out double price)	Locate a resting order's side and price by id; false when absent.
bool SetDepthMode(OrderBookDepthMode mode)	Record which feed authors the book. The host calls it when it decides market-by-order authority; the book never infers the mode from the events it receives. Returns true when the mode changed.
Plus the full IOrderBook read surface above (including TryGetEntry and OrderRemoved).
OrderBookSnapshot
Immutable, render-safe copy of a book at one moment: flat Bids / Asks arrays (index 0 = top of book) so iteration is allocation-free and rows match visual ladder order. Per-order detail is dropped; each row is aggregated only.

Member	Description
string InstrumentId	Instrument the source book tracks.
DateTime CapturedAtUtc	Wall-clock time the snapshot was taken (UTC).
IReadOnlyList<Row> Bids / Asks	Rows, top of book first.
double BestBidPrice / BestAskPrice	Bids[0].Price / Asks[0].Price, or NaN when empty.
record struct Row(double Price, long Size, int OrderCount)	One snapshot row (OrderCount informational, 0 in L2 mode).
static OrderBookSnapshot Capture(IOrderBook book, DateTime capturedAtUtc, int maxLevelsPerSide = int.MaxValue)	Capture a flat copy. Call it inside ReadOrderBook so the book is locked. maxLevelsPerSide caps depth per side.
static OrderBookSnapshot Empty(string instrumentId, DateTime capturedAtUtc)	A "no data yet" default for renderers.
PriceLevel
One price level inside an OrderBook (a sealed class). Carries the aggregated Size and, when MBO-fed, an ordered per-order queue. A level constructed only from L2 reports OrderCount == 0.

Member	Description
double Price	Price of this level (immutable while in the book).
Side Side	Which side this level is on.
long Size	Aggregated total size (sum of the per-order queue when MBO-fed).
int OrderCount	Per-order entries at this level. 0 in L2-only mode.
IEnumerable<OrderBookEntry> EnumerateOrders()	Per-order queue front to back. Empty in L2-only mode. Live view.
OrderBookEntry
One resting order in an MBO-mode book (a sealed class; identity is OrderId).

Member	Description
string OrderId	Venue-supplied id, stable for the order's lifetime.
Side Side	Side the order rests on.
double Price	Current price (changes on a price-move Modify).
long Size	Current size (changes on Modify).
ulong QueuePriority	Vendor queue position (smaller = front; 0 = preserve arrival order).
DateTime FirstSeenUtc	When the order was first added to the book.
DateTime? LastChangedUtc	When the order was last modified; null if never changed since add.
long? SizeBeforeLastChange	The size the order showed before the change at LastChangedUtc (changes with the same exchange time count as one); null if never changed, or when that change moved its price.
bool IsAggressive	Whether the order has been flagged as an aggressor.
bool IsIceberg	Whether the order shows iceberg (hidden-size refresh) behaviour.
DepthUpdate
One aggregated Level-2 depth event, a readonly record struct passed by value. Venues pass SequenceNumber = 0; the host's sequencer stamps it during fan-out.

DepthUpdate.cs
public readonly record struct DepthUpdate(
    long SequenceNumber,
    DateTime ExchangeTimestampUtc,
    string InstrumentId,
    Side Side,
    DepthAction Action,
    double Price,
    long Size);
DepthAction
Kind of update a DepthUpdate carries (an enum : byte).

Value	Meaning
Set = 0	Replace the size at this price level; creates the level if absent.
Delete = 1	Remove the price level entirely (carried Size ignored).
MboEvent
One Market-By-Order event, a readonly record struct passed by value. Carries the individual resting order's identity, price, size and queue priority. On a price-move Modify, PrevPrice holds the old price (double.NaN when unchanged).

MboEvent.cs
public readonly record struct MboEvent(
    long SequenceNumber,        // platform delivery stamp: venues leave it 0
    DateTime ExchangeTimestampUtc,
    string InstrumentId,
    string OrderId,
    Side Side,
    MboAction Action,
    double Price,
    long Size,
    ulong QueuePriority,
    double PrevPrice,
    ulong VenueSequence);       // the venue's own wire counter, 0 when it has none

// Ten-argument constructor: VenueSequence = 0.
public MboEvent(long SequenceNumber, DateTime ExchangeTimestampUtc, string InstrumentId,
    string OrderId, Side Side, MboAction Action, double Price, long Size,
    ulong QueuePriority, double PrevPrice);
Two sequence numbers, two owners. SequenceNumber is stamped by the host's sequencer at fan-out and orders events across every instrument on the same sequencer shard, so one instrument's stream is never contiguous and a gap in it proves nothing. VenueSequence is forwarded untouched from the wire when the venue's protocol carries a per-record or per-frame counter (rows of one frame share the frame's value). A venue sets it to 0 when its protocol has no such counter, or when the counter only disambiguates orders with the same timestamp. Its scope and stride are the venue's; a jump between values the venue defines as consecutive is the only wire-level evidence of rows this process never received. The recorded tape keeps VenueSequence and stores SequenceNumber as 0.
MboAction
What one MBO row says (an enum : byte). Two families share the one ordered stream: book rows (the exchange's new / change / delete order messages) and execution rows (what traded).

Value	Meaning
Add = 0	A new resting order appeared. The book must not already contain the id.
Modify = 1	An order changed size and/or moved price. Only Cancel deletes; the book removes a Modify that drains the size to 0.
Cancel = 2	Order is gone; the book removes it by OrderId.
Trade = 3	Execution row: an aggressing order traded. OrderId is the aggressor's exchange order id (empty when the venue names none, e.g. an implied trade), Side the aggressor's side (Bid = a buyer), price and size the trade. The Fill rows it produced follow it.
Fill = 4	Execution row: a resting order was filled. OrderId is the resting order's id, Side its side, price and size the fill.
EventEnd = 5	Execution row: the exchange match event that held the preceding execution rows is complete. Sent only after events that held execution rows; id empty, size 0.
Execution rows never reach OnMarketByOrder. They come only from a feed whose ExecutionDetail is not None, never change the book (the venue's following book rows do), and are not delivered to book consumers: Subscribe without a scope, and the indicator and strategy OnMarketByOrder hooks, deliver book rows only. A recorded MBO tape (RecordedDepth) holds them in stream order; OrderBook.ApplyMbo ignores them, so replaying a tape into a book needs no filter. MboActionExtensions.IsBookRow(this MboAction) is true for Add, Modify and Cancel: use it when you read a tape. The platform turns these rows into detections.
MboExecutionDetail
Which execution rows an MBO stream carries (a [Flags] enum : byte), read through IMarketByOrderFeed.ExecutionDetail. A venue declares it from its documented wire fields.

Value	Meaning
None = 0	No execution rows: book rows only.
RestingOrderIds = 1	Fill rows name the resting order that was filled. Enough to prove icebergs.
AggressorOrderIds = 2	Trade rows name the aggressing order, and every match event that held execution rows is closed by an EventEnd row. With RestingOrderIds, enough to prove stop runs.
MboRowScope
Which MBO rows a subscription receives (an enum : byte), passed to the four-argument IMarketByOrderFeed.Subscribe.

Value	Meaning
Book = 0	Book rows only (Add, Modify, Cancel). What every book consumer gets.
All = 1	Book rows and execution rows, in stream order. For a consumer that stores the stream as it came (the tape recorder).
Side
Side of the book a depth or order event applies to (an enum : byte). Two-valued: every depth event is sided at the source (aggressor classification on a trade is separate, via TickFlags).

Value	Meaning
Bid = 0	Resting buy order. Stacks under best bid in descending price order.
Ask = 1	Resting sell order. Stacks above best ask in ascending price order.
OrderBookDepthMode
Which feed currently authors an IOrderBook (an enum : byte, read through IOrderBook.DepthMode). A venue that grants both streams starts per-order and falls back to aggregated when its market-by-order book cannot be recovered; the change is announced as OrderBookLifecycleEvent.DepthModeChanged.

Value	Meaning
Aggregated = 0	Aggregated Level-2 updates author the price levels. Per-order detail may be present but is advisory: an aggregated update that touches a level replaces that level's per-order queue with its total.
PerOrder = 1	A market-by-order stream is authoritative: every level is the sum of its resting orders and aggregated updates are not applied. TryGetEntry and PriceLevel.EnumerateOrders describe the venue's book.
OrderBookLifecycleEvent
A transition of the shared book that invalidates state a consumer derived from earlier depth or MBO callbacks (an enum : byte). Delivered through IIndicator.OnOrderBookLifecycle; each value names what has already happened to the book when the callback runs.

Value	Meaning
Reset = 0	The book was cleared and nothing is promised to follow: the feed subscription was invalidated (a connection loss is the usual cause). Drop derived state and rebuild it from the events that follow.
RebuildStarted = 1	The book was cleared because the venue is sending a fresh image of every resting order. The events that follow are that image, then live deltas. Derived state is stale until RebuildCompleted.
RebuildCompleted = 2	The image has been applied and the book is consistent again. Re-derive state from ReadOrderBook here rather than from the image events, which reach a consumer through the same sampled delivery as live events.
DepthModeChanged = 3	IOrderBook.DepthMode changed. Per-order features stop describing the venue's book once the mode is Aggregated.
CaptureGap = 4	The recorded stream has a hole of unknown size here (the recorder wrote a Gap marker): the book was cleared and is incomplete until the next RebuildCompleted. Raised only when a tape is replayed (strategy backtests, market replay); on a live chart delivery loss is counted through OnMarketByOrderLoss instead.
Depth-hub diagnostics
Host-side. SabrTrader.Pipeline.MarketDepth.Hub holds the process-wide diagnostic sink the host writes to its activity log for the market-by-order path (venue subscribe, sequencer, hub, feed, DOM and chart consumers). The application wires it at startup; plugins do not set it.
Type	What it is
MboConnectionDiagnosticLog	The sink for MBO connection-lifecycle lines: Enabled and Write (set by the host), Log, LogOnce (once per key, reset with ClearOneShotPrefix), LogAlways, LogAnomalyRateLimited, and Format.
Detections: SabrTrader.Pipeline.MarketDepth.Detections
Native icebergs and stop runs the platform proves from an MBO feed's execution rows, delivered to indicators and strategies that set ProcessesDetections = true. Rules, feeds, guarantees and examples are in the guide: Icebergs & stop runs. Add using SabrTrader.Pipeline.MarketDepth.Detections;.

IIndicator / IndicatorBase
bool ProcessesDetections { get; }   // IndicatorBase: public virtual, protected set; default false

void OnIceberg(in IcebergEvent iceberg, IIndicatorContext ctx);
void OnStopRun(in StopRunEvent stopRun, IIndicatorContext ctx);
void OnDetectionsStatus(DetectionsStatus status, IIndicatorContext ctx);
void OnDetectionsLoss(long lostDetections, IIndicatorContext ctx);   // all four: default no-op
Every detection arrives once, in Sequence order, on the indicator's own thread; never sampled or merged. A reader that attaches late first receives every detection from the moment the platform opened the instrument's book, flagged IsHistory. Loss happens only when a reader falls more than 100,000 detections behind, and is reported through OnDetectionsLoss and the Sequence jump. For strategies see Strategies › Detections.

IcebergEvent
One step in the life of a native iceberg. A readonly record struct with { get; init; } properties, passed in.

Property	Meaning
long Sequence	Position in this instrument's detection log, shared with StopRunEvent, contiguous from 1. A jump means detections were lost.
string InstrumentId	The instrument.
long IcebergId	Stable id of this iceberg for the platform session; the same on every event of its life.
string OrderId	The exchange order id. A native iceberg keeps it through every refill.
IcebergPhase Phase	What happened in this step.
Side Side	The side it rests on: Bid = a buy iceberg, Ask = a sell iceberg.
double Price	Its price.
long Size	Contracts filled in this step (Detected, Hit, Executed); 0 for Refilled, Moved, Pulled and LostTrack.
long FilledTotal	Contracts filled over its whole life so far.
long HiddenTotal	Contracts filled beyond what the book showed, over its whole life so far. Per book update: fills - (shown before - shown after).
long ShownSize	What the book shows of it right now (0 once it is gone).
int Refills	How many times the exchange refilled it so far.
DateTime ExchangeTimeUtc	Exchange time of the row that caused this step.
bool IsHistory	True for events that happened before this reader attached.
IcebergPhase
An enum : byte. Life: Detected, then any number of Hit / Refilled / Moved, then exactly one of Executed, Pulled or LostTrack. Treat a value you do not know as "ignore".

Value	Meaning
Detected = 0	First proof: fills reached the size the order showed and the exchange kept the same order on the book.
Hit = 1	Filled again.
Refilled = 2	The exchange restored its shown size after it was filled.
Executed = 3	The last of it was filled; it is gone.
Pulled = 4	Its owner cancelled it with size left; it is gone.
LostTrack = 5	The book was reset, had a gap, or was replaced by another one; the outcome is unknown and stays unknown.
Moved = 6	Its owner moved it to another price (Price is the new one); same order id, same iceberg.
StopRunEvent
One match event in which an incoming order (the trigger) set off stop orders that were elected and traded in that same event. A readonly record struct with { get; init; } properties, passed in.

Property	Meaning
long Sequence	Position in this instrument's detection log, shared with IcebergEvent, contiguous from 1.
string InstrumentId	The instrument.
long StopRunId	Stable id of this stop run for the platform session.
StopRunDirection Direction	Buy stops (price pushed up) or sell stops (price pushed down).
double TriggerPrice	The first price the trigger order traded at.
long TriggerSize	Contracts the trigger order traded in this event.
double FromPrice	The first price the elected stops traded at.
double ToPrice	The last price the elected stops reached (the far end of the run).
int StopCount	Number of elected stop orders.
long StopVolume	Contracts traded by the elected stops (the trigger's contracts not included).
DateTime ExchangeTimeUtc	Exchange time of the match event.
bool IsHistory	True for events that happened before this reader attached.
StopRunDirection
An enum : byte. Treat a value you do not know as "ignore".

Value	Meaning
BuyStops = 0	Elected stop orders that bought, pushing price up.
SellStops = 1	Elected stop orders that sold, pushing price down.
DetectionsStatus
What the instrument's feed can detect. A readonly record struct, delivered once when a reader attaches and on every change.

Property	Meaning
bool Icebergs	The feed names the resting order on every fill, so native icebergs can be proven.
bool StopRuns	The feed names the aggressing order on every trade and closes every match event, so stop runs can be proven.
bool Interrupted	True right after the book was reset, had a gap, or was replaced by another one: every active iceberg was ended as LostTrack (those events come first). Always followed at once by a status with Interrupted false.
Recorded tapes: SabrTrader.Pipeline.Storage
The platform's recorder writes an instrument's L2 / MBO stream to disk together with the control markers a reader needs to rebuild the book. Live indicator callbacks are sampled; the tape is the complete record. The user starts recording from the platform's recording window. Add using SabrTrader.Pipeline.Storage;.

An MBO tape from a feed with execution rows also holds its Trade, Fill and EventEnd rows, in stream order between the book rows. OrderBook.ApplyMbo ignores them; filter with MboAction.IsBookRow() when you count book rows yourself.

Tape items and the catalog ambient
Read side only. The host's recorder writes the tapes and the replay engine reads them back. A plugin reads recorded depth through IRecordedDepthCatalog; the types below are what its cursors return.
Type	What it is
ITickTapeCursor · TickTapeItem	The tick-tape cursor is Current + MoveNext(), and TickTapeItem is (Marker, ExchangeTimestampUtc, Tick). A tick tape carries no re-image markers.
HeatmapSidecarColumn	One column of the pre-bucketed heatmap sidecar written alongside a depth tape: BucketStartUtc, parallel Prices, BidSizes and AskSizes arrays, and LevelCount. Plugins read columns through IRecordedDepthCatalog.OpenHeatmap.
DepthTapeItem · RecordedDepthCatalogAmbient	One L2 tape entry (Marker, ExchangeTimestampUtc, Update); and the ambient the host publishes the catalog through (Catalog, SetCatalog, Use). Plugins read the catalog from IIndicatorContext.RecordedDepth.
IRecordedDepthCatalog
Read seam over the recorded tapes, reached from an indicator through IIndicatorContext.RecordedDepth (null when the host records none). Tapes are keyed by DepthStoreKey (venue provider key + instrument); a null cursor means the store has no coverage of the window. Reads run beside the live feed without blocking it. DepthStoreKeyInfo is (Key, ItemCount, FirstUtc, LastUtc, ApproxByteSize).

IRecordedDepthCatalog.cs
public interface IRecordedDepthCatalog
{
    IMboCursor? OpenMbo(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);
    IDepthCursor? OpenDepth(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);
    IHeatmapSidecarCursor? OpenHeatmap(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);

    ITickTapeCursor? OpenTicks(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);

    IReadOnlyList<DepthStoreKey> EnumerateMboKeys();     // every key with an MBO tape
    IReadOnlyList<DepthStoreKey> EnumerateDepthKeys();   // every key with an L2 tape
    IReadOnlyList<DepthStoreKey> EnumerateTickKeys();    // every key with a tick tape
    DepthStoreKeyInfo? DescribeMbo(DepthStoreKey key);       // coverage, or null
    DepthStoreKeyInfo? DescribeDepth(DepthStoreKey key);
    DepthStoreKeyInfo? DescribeTicks(DepthStoreKey key);

    // Which parts of a window are actually on disk - ask before you plan a read.
    IReadOnlyList<DateRange> GetRecordedMboRanges(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);
    IReadOnlyList<DateRange> GetRecordedDepthRanges(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);
    IReadOnlyList<DateRange> GetRecordedTickRanges(DepthStoreKey key, DateTime fromUtc, DateTime toUtcExclusive);
}

public readonly struct DepthStoreKey : IEquatable<DepthStoreKey>
{
    public DepthStoreKey(string providerKey, string instrumentId);
    public string ProviderKey { get; }
    public string InstrumentId { get; }
}

public interface IMboCursor : IDisposable
{
    bool MoveNext();          // false when the window is exhausted
    MboTapeItem Current { get; }
}

public readonly record struct MboTapeItem(
    DepthTapeMarker Marker,
    DateTime ExchangeTimestampUtc,   // the marker time when Marker is not Event
    MboEvent Event);                 // default when Marker is not Event
DepthTapeMarker
Control records on a tape (an enum : byte). Events are the payload; the rest bracket reconnects, venue images, capture loss and recording sessions, so a reader knows which stretches of book are trustworthy.

Value	Meaning
Event = 0	A venue MboEvent / DepthUpdate follows.
Reset = 1	The live subscription was reset. Drop reconstructed book state.
Gap = 2	Events are missing here: the recorder's writer overflowed and capture stopped, or the feed dropped events on the way to the recorder and capture continues. Either way the book is incomplete from here until the next ReimageBegin.
ReimageBegin = 3	The venue began a snapshot / re-image. Subsequent events replace the book.
ReimageEnd = 4	The venue finished the snapshot / re-image. The book is consistent again.
RecordingStarted = 5	The recorder began watching this instrument. Recorded coverage starts here, even if the market is silent afterwards.
RecordingStopped = 6	The recorder stopped watching this instrument on purpose. Coverage ends here (exclusive); the silence that follows is absence, not loss, and the reconstructed book is meaningless until the next ReimageEnd.
What a tape can and cannot certify. The recorder subscribes with the loss-reporting overload, so the tape carries a Gap for every hole the platform can observe: its own writer falling behind, and events the hosted feed's drain pump discarded before they reached the recorder. Every venue image and per-instrument reset, whether at recording start, after a loss, or from the platform's own book recovery, is bracketed by ReimageBegin / ReimageEnd or marked Reset through IMarketByOrderFeed.BookLifecycleChanged. What no consumer can mark is a row the venue never sent; a stretch of tape between markers that carries a venue sequence (MboEvent.VenueSequence) can be audited against that sequence, and a stretch without one is complete as far as the platform saw. The markers are written when the platform learns of the transition, so a marker can sit a few queued events early relative to the image rows; the book is consistent from ReimageEnd either way.
Example: reading the book from an indicator
DepthImbalance.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.MarketDepth;

// Inside an IndicatorBase subclass whose constructor sets ProcessesMarketDepth = true.
private double _imbalance;              // top-of-book imbalance in [-1, 1]: positive = bid-heavy
private OrderBookSnapshot? _snapshot;   // frozen copy for a render thread

public override void OnMarketDepth(in DepthUpdate update, IIndicatorContext ctx)
{
    // ReadOrderBook runs the reader under the host's book lock.
    ctx.ReadOrderBook(book =>
    {
        double bid = book.BestBidPrice, ask = book.BestAskPrice;
        if (double.IsNaN(bid) || double.IsNaN(ask)) return;   // one side empty

        long bidSize = book.GetLevel(Side.Bid, bid)?.Size ?? 0;
        long askSize = book.GetLevel(Side.Ask, ask)?.Size ?? 0;
        long total = bidSize + askSize;
        _imbalance = total == 0 ? 0 : (double)(bidSize - askSize) / total;

        // Capture inside the reader so the copy is consistent.
        _snapshot = OrderBookSnapshot.Capture(book, DateTime.UtcNow, maxLevelsPerSide: 20);
    });
}
Example: rebuilding order lifecycles from the tape
QueueAudit.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.MarketDepth;
using SabrTrader.Pipeline.Storage;

// Off the hot path (a toolbar action, a background task): replay one hour of an
// instrument's MBO tape into a private book and measure how long orders rested.
static void Audit(IIndicatorContext ctx, string instrumentId, DateTime fromUtc)
{
    IRecordedDepthCatalog? tapes = ctx.RecordedDepth;
    if (tapes is null) return;                        // this host records nothing

    DepthStoreKey? found = null;
    foreach (var k in tapes.EnumerateMboKeys())
        if (k.InstrumentId == instrumentId) { found = k; break; }
    if (found is not { } key) return;                 // never recorded

    using IMboCursor? cursor = tapes.OpenMbo(key, fromUtc, fromUtc.AddHours(1));
    if (cursor is null) return;                       // no coverage of that window

    var book = new OrderBook(key.InstrumentId);
    book.OrderRemoved += (entry, removedAtUtc) =>
    {
        TimeSpan rested = removedAtUtc - entry.FirstSeenUtc;
        // record `rested` for entry.Side / entry.Price ...
    };

    bool complete = true;                             // false once a Gap sits inside the window
    while (cursor.MoveNext())
    {
        MboTapeItem item = cursor.Current;
        switch (item.Marker)
        {
            case DepthTapeMarker.Event:
                MboEvent evt = item.Event;
                book.ApplyMbo(in evt);
                break;
            case DepthTapeMarker.Reset:
            case DepthTapeMarker.ReimageBegin:
                book.Clear();                         // everything before is superseded
                break;
            case DepthTapeMarker.Gap:
                complete = false;                     // lifecycles across this point are unknowable
                break;
        }
    }
    // complete == true: every order that appeared in the window has a certain fate.
}
