Reference
Bars & series
The OHLCV bar value type, the bar-specification records that identify a series, the builder interfaces that construct bars from ticks, per-instrument metadata, and the barsAgo-indexed series you read in OnBarUpdate. Namespaces SabrTrader.Pipeline.Bars and SabrTrader.Pipeline.Series.

On this page
Bar
ChartDisplayTime
BarSpecification & subtypes
BarConstruction
BarAdjustmentMode · OversizedTradePolicy
IBarBuilder / factory / seeding
IInstrumentMetadata
QuantityUnit · InstrumentCategory
IBarSeries · ISeries<T> · ChunkedSeries<T>
Bar
SabrTrader.Pipeline.Bars · a readonly record struct passed by value through pipeline callbacks. For non-time bars there is no fixed period: EndUtc is the timestamp of the closing tick. Disk and memory caches persist only the UTC instants; display fields are chart-boundary and must not be written to cache.

Bar.cs
public readonly record struct Bar(
    DateTime StartUtc,
    DateTime EndUtc,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    long TickCount,
    DateTime StartDisplay = default,
    DateTime EndDisplay = default)
{
    public DateTime Start => StartDisplay == default ? StartUtc : StartDisplay;
    public DateTime End   => EndDisplay   == default ? EndUtc   : EndDisplay;

    public Bar WithChartDisplay(TimeZoneInfo tz);
    public Bar WithoutChartDisplay();
}
Member	Meaning
StartUtc / EndUtc	True venue UTC, always. Open instant, and the moment the bar closed (closing-tick timestamp for non-time bars). Never rewritten to a display zone. Use these for cache keys, identity, fill/execution alignment, and any comparison against another *Utc field.
StartDisplay / EndDisplay	Chart-zone wall-clock tagged DateTimeKind.Utc ("display-as-Utc-kind"), or default when unstamped (UTC chart, or a day-and-longer bar). The host stamps these at the chart boundary via WithChartDisplay; bar-type builders leave them default.
Start / End	Chart/session timeline: display wall-clock when stamped, otherwise the UTC instant. Use these for axis labels, session time-of-day, BarTimer remaining, and ISessionIterator queries on a chart iterator.
Open / High / Low / Close	OHLC prices.
Volume	Total traded volume in the bar.
TickCount	Number of underlying ticks that formed the bar. Optional metadata for time bars; load-bearing for order-flow / tick bars.
WithChartDisplay(tz)	Stamp StartDisplay / EndDisplay without changing UTC identity. Identity when tz is UTC, and for day-and-longer bars (those stay 00:00 UTC date labels).
WithoutChartDisplay()	Drop display stamps so a cache write cannot persist chart-zone wall-clock. UTC identity is unchanged.
Two clocks. *Utc is venue UTC. The chart timeline is Start / End. Do not write display wall-clock into a field named Utc, and do not compare FirstTradeUtc / fill timestamps / ctx.Now against StartUtc on a non-UTC chart: convert the UTC side with ChartDisplayTime.FromUtc and compare to Bar.Start, or compare UTC to UTC.
ChartDisplayTime
SabrTrader.Pipeline.Time · converts true UTC ↔ display-as-Utc-kind. The host uses this to stamp bars; indicators use it to overlay an external UTC instant (news, prints, OrderFlowBarStats.FirstTradeUtc) onto the chart timeline.

ChartDisplayTime.cs
public static class ChartDisplayTime
{
    // True UTC → display-zone wall-clock tagged DateTimeKind.Utc. Identity when tz is UTC.
    public static DateTime FromUtc(DateTime utc, TimeZoneInfo tz);

    // Inverse: display-as-Utc-kind → true UTC.
    public static DateTime ToUtc(DateTime displayAsUtcKind, TimeZoneInfo tz);
}
Never write FromUtc's result into a field named *Utc. IIndicatorContext.DisplayTimeZone is the zone to pass; treat null as "already on the bar timeline / no shift".

BarSpecification
An abstract record that is the identity of a series: comparable by value, hashable, serialisable. Two consumers asking for the same spec share one builder and one output series. A new bar type is one record plus one builder.

BarSpecification.cs
public abstract record BarSpecification(string InstrumentId)
{
    public string? ContinuousRoot { get; init; }
    public BarAdjustmentMode ContinuousAdjustment { get; init; } = BarAdjustmentMode.BackAdjusted;
    public string? TradingHoursTemplateName { get; init; }
    public bool ResetOnNewTradingDay { get; init; } = true;
    public string? Exchange { get; init; }
    public BarBuildSource BuildSource { get; init; }   // Native (default) or Ticks
    public string RollStableInstrumentId { get; }   // ContinuousRoot ?? InstrumentId

    public abstract string DisplayName { get; }
    public virtual bool EndsOnTimeGrid => false;
    public virtual bool OffersBuildSourceChoice => false;   // true for Tick, Volume, Range

    public virtual BarSpecification WithInstrumentId(string newInstrumentId) => this;
    public BarSpecification WithContinuous(string? continuousRoot, BarAdjustmentMode adjustment);
    public BarSpecification WithTradingHours(string? templateName);
    public BarSpecification WithTradingHours(string? templateName, bool resetOnNewTradingDay);
    public BarSpecification WithExchange(string? exchange);
    public BarSpecification WithBuildSource(BarBuildSource buildSource);
}
Member	Meaning
RollStableInstrumentId	What this series is to a USER, stable across contract rolls: the continuous root when continuous, else InstrumentId. Key alert rules, saved preferences and per-instrument defaults on this; keyed on the wire symbol they stop matching at the next roll. Subscriptions, backfill and order routing keep using InstrumentId.
InstrumentId	The venue-native symbol the series is built from. Stays the front-month wire symbol even for continuous futures.
ContinuousRoot	When non-null, the continuous-futures root (e.g. "ES") the data layer stitches dated contracts behind. Set for a series on the product and for a series on the dated contract the product is live on right now (InstrumentId is that contract either way); both load the product's history. Null for a month-locked contract (any other month, which loads its own history only) and for an equity / forex / crypto series.
ContinuousAdjustment	How rollover gaps are bridged when ContinuousRoot is set. Default BackAdjusted. Ignored when ContinuousRoot is null.
TradingHoursTemplateName	Resolved trading-hours template the series is bound to (part of identity, so RTH vs ETH series don't share a builder). Null = no session filtering.
ResetOnNewTradingDay	Whether bar construction restarts the grid at each session boundary ("Break at EOD"). Only meaningful when a template is set. Default true.
Exchange	Specific source exchange when the user picked one (e.g. a delayed feed "CME-Delayed") rather than the symbol's catalog default. Part of identity, so real-time vs delayed series don't share a builder / cache / subscription. Null = venue resolves the exchange from the wire symbol.
BuildSource	Where the history of a tick, volume or range series comes from (the "Bars: Native / Ticks" choice in every series picker). Native (default) takes the venue's own finished bars when the venue builds that bar type itself, otherwise bars built from ticks. Ticks always builds the bars from the trade tape, the same way order-flow series and live bars are built, and applies ResetOnNewTradingDay; it downloads every trade. Part of identity. Every other bar type keeps Native whatever is assigned. Live bars are built from ticks for both. Set it with spec.WithBuildSource(BarBuildSource.Ticks) or spec with { BuildSource = BarBuildSource.Ticks }.
OffersBuildSourceChoice	true for TickBarSpec, VolumeBarSpec and RangeBarSpec: the bar types a venue can serve as its own bars, and the only ones where BuildSource means anything.
DisplayName	Abstract; each concrete spec overrides for logs / UI (e.g. "MNQ Time 5m").
EndsOnTimeGrid	True when the series' bars end on a time grid: a time bar, or a transform over one. A trade stamped exactly at a closed bar's end then belongs to the NEXT bar. False (the default) when a bar ends at its own last trade (tick, volume, range, Renko), so a trade at that end is already in it. The platform uses it to decide where live ticks take over after a closed bar. TimeBarSpec returns true; HeikinAshiBarSpec returns its underlying's value; KagiBarSpec, PointAndFigureBarSpec and LineBreakBarSpec return their underlying's value, or true when Underlying is null (the default base is a 1-minute time series). A plugin spec whose bars close on a clock overrides it to return true.
WithInstrumentId(string)	Copy with a replaced instrument id (symbol-translation step). Default returns this; built-ins and plugin specs override.
WithContinuous(string?, BarAdjustmentMode)	Copy carrying continuous-futures intent.
WithTradingHours(string?, bool)	Copy bound to a resolved trading-hours template.
WithExchange(string?)	Copy sourced from a specific venue exchange; a null / blank value clears it.
BarConstruction
Static helpers that say how a spec's bars are cut. Use them instead of type-switching on specs when a calculation depends on what a bar's timestamps mean.

BarConstruction.cs
public static class BarConstruction
{
    public static bool IsBuiltFromTrades(BarSpecification spec);
    public static bool IsCutOnTrades(BarSpecification spec);
}
Method	Returns true when
IsBuiltFromTrades(spec)	A bar closes on a trade (tick, range, volume, Renko, UniRenko and every other non-time spec), so Bar.StartUtc / Bar.EndUtc are its first and last trade times and consecutive bars can share a timestamp. False for TimeBarSpec, whose EndUtc is the exclusive grid boundary. HeikinAshiBarSpec answers for its underlying.
IsCutOnTrades(spec)	Every bar starts on its own first trade and ends on its own last trade, and Bar.TickCount counts exactly those trades (tick, volume, range, Renko, Median Renko, UniRenko). False for time bars and for Kagi, Point & Figure and Line Break: those are built from underlying bars, so a new bar opens at a source bar's end and carries that source bar's trades, all of which traded before the new bar's start. HeikinAshiBarSpec answers for its underlying.
Both throw ArgumentNullException for a null spec. Any spec not named above, including a plugin spec, answers true to both.

Pair them with EndsOnTimeGrid when you match trades to bars yourself:

Matching a trade to a bar
using SabrTrader.Pipeline.Bars;

// Does a trade stamped t belong to this closed bar?
static bool Holds(BarSpecification spec, in Bar bar, DateTime t)
    => spec.EndsOnTimeGrid
        ? t >= bar.StartUtc && t < bar.EndUtc    // grid end is exclusive: t == EndUtc is the next bar
        : t >= bar.StartUtc && t <= bar.EndUtc;  // the bar ends on its own last trade

// Trade-built bars can share a timestamp, so never key them by time alone.
static bool TimestampsAreUnique(BarSpecification spec)
    => !BarConstruction.IsBuiltFromTrades(spec);
Concrete bar specs
All sealed record in SabrTrader.Pipeline.Bars. Each overrides DisplayName and WithInstrumentId.

BarSpecification.cs
public sealed record TimeBarSpec(string InstrumentId, TimeSpan Period) : BarSpecification(InstrumentId);
public sealed record TickBarSpec(string InstrumentId, int Ticks) : BarSpecification(InstrumentId);
public sealed record VolumeBarSpec(string InstrumentId, long Volume,
    OversizedTradePolicy OversizedTrades = OversizedTradePolicy.SplitAcrossBars) : BarSpecification(InstrumentId);
public sealed record RangeBarSpec(string InstrumentId, int RangeTicks) : BarSpecification(InstrumentId);
public sealed record RenkoBarSpec(string InstrumentId, int BrickTicks, bool WithWicks = false) : BarSpecification(InstrumentId);
public sealed record MedianRenkoBarSpec(string InstrumentId, int BrickTicks, int TrendThresholdTicks) : BarSpecification(InstrumentId);
public sealed record KagiBarSpec(string InstrumentId, int ReversalTicks,
    BarSpecification? Underlying = null, KagiReversalType ReversalType = KagiReversalType.Tick) : BarSpecification(InstrumentId);
public sealed record PointAndFigureBarSpec(string InstrumentId, int BoxTicks, int Reversal,
    BarSpecification? Underlying = null, PointAndFigurePriceType PriceType = PointAndFigurePriceType.Close) : BarSpecification(InstrumentId);
public sealed record LineBreakBarSpec(string InstrumentId, int LineCount,
    BarSpecification? Underlying = null) : BarSpecification(InstrumentId);

public enum KagiReversalType        { Tick, Percent }
public enum PointAndFigurePriceType { Close, HighLow }

// Distance units a price gap can be expressed in (PriceDistanceFormatter converts between them).
public enum DistanceUnit { Price, Tick, Point, Pip }
public sealed record HeikinAshiBarSpec(string InstrumentId, BarSpecification Underlying) : BarSpecification(InstrumentId);
public sealed record UniRenkoBarSpec(string InstrumentId, int BrickTicks, int TrendThresholdTicks, int ReversalTicks)
    : BarSpecification(InstrumentId);
Spec	Closes when…
TimeBarSpec(Period)	Every Period (seconds/minutes/hours/days).
TickBarSpec(Ticks)	Every N ticks (N positive).
VolumeBarSpec(Volume, OversizedTrades)	Accumulated traded volume reaches Volume; OversizedTrades controls a print larger than remaining capacity.
RangeBarSpec(RangeTicks)	The bar's high-low range reaches RangeTicks ticks; the bar is clamped to that range and the breaking tick opens the next bar one tick beyond the close.
RenkoBarSpec(BrickTicks, WithWicks)	Price moves one brick; reversal needs 2 bricks. WithWicks exposes intra-brick excursions (default false = classical wickless).
MedianRenkoBarSpec(BrickTicks, TrendThresholdTicks)	"Median Renko" variant: always-wicked, body height BrickTicks, continuation/reversal driven by TrendThresholdTicks.
KagiBarSpec(ReversalTicks)	Price reverses ReversalTicks from the active extreme; line closes and a new one opens opposite.
PointAndFigureBarSpec(BoxTicks, Reversal)	One column = one bar. BoxTicks box height; Reversal boxes (classically 3) to start a new column.
LineBreakBarSpec(LineCount)	Price closes above the highest high / below the lowest low of the last LineCount bars.
ResolvedUnderlying (Kagi, P&F, Line Break)	These three are computed over a base series, one pattern test per completed base bar. ResolvedUnderlying is Underlying, or a 1-minute TimeBarSpec carrying this spec's trading hours and continuous-contract settings when Underlying is null. It builds a fresh spec on every read in that case, so read it once at construction, not per tick.
HeikinAshiBarSpec(Underlying)	Derived from another BarSpecification; OHLC computed from the underlying bars. WithInstrumentId propagates into Underlying.
UniRenkoBarSpec(BrickTicks, TrendThresholdTicks, ReversalTicks)	UniRenko: body height BrickTicks; continuation driven by TrendThresholdTicks; reversal by ReversalTicks.
BarAdjustmentMode · OversizedTradePolicy
BarAdjustmentMode.cs
public enum BarAdjustmentMode { None, BackAdjusted, RatioAdjusted }
public enum OversizedTradePolicy { KeepAtomic, SplitAcrossBars }
Value	Meaning
BarAdjustmentMode.None	Raw stitched bars; rollover gaps visible.
BarAdjustmentMode.BackAdjusted	Additive back-adjustment (default). Standard for technical analysis: absolute price distances stay meaningful.
BarAdjustmentMode.RatioAdjusted	Multiplicative back-adjustment. Standard for returns analysis; absolute distances drift.
OversizedTradePolicy.KeepAtomic	A single print is atomic, so the bar can exceed the threshold. Required for order-flow correctness: pick it when a footprint or delta overlay rides the series.
OversizedTradePolicy.SplitAcrossBars	Default, and matches NinjaTrader's volume bars exactly. Splits a print across bars so each equals the threshold (the last filled bar can have less); each synthetic bar is a single price point with the tick-count credit on the first. Do not use with order-flow indicators.
IBarBuilder
Shared metadata every builder exposes. HasCurrentBar is false until the first bar starts; reading CurrentBar while false throws.

IBarBuilder.cs
public interface IBarBuilder
{
    BarSpecification Spec { get; }
    bool HasCurrentBar { get; }
    Bar CurrentBar { get; }
}
ITickBarBuilder
Tick-driven builders (Time, Tick, Volume, Range, Renko, …). They consume only TickFlags.Trade ticks. OnTick writes newly-closed bars into a caller-supplied span and returns the count (0 mid-bar, 1 typical, 2 or more on a gap).

IBarBuilder.cs
public interface ITickBarBuilder : IBarBuilder
{
    int OnTick(in Tick tick, Span<Bar> closedBars);
    bool HasPendingClosedBars => false;
    int DrainPendingClosedBars(Span<Bar> closedBars) => 0;

    bool ClosingTickBelongsToNextBar => false;
    bool ResetsOnSessionBoundary => true;
    bool ExposesFormingBar => true;

    int ForceCloseCurrentBar(Span<Bar> closedBars) => 0;
    int ForceCloseAtSessionEnd(DateTime sessionEnd, Span<Bar> closedBars) => ForceCloseCurrentBar(closedBars);
    void BeginSession(DateTime sessionBegin) { }
}
Member	Meaning
OnTick(in Tick, Span<Bar>)	Process one tick; writes closed bars chronologically, returns the count written.
HasPendingClosedBars / DrainPendingClosedBars(Span<Bar>)	How many bars one tick closes is not bounded: a small Renko brick against a gap print closes as many bricks as the move contains. A builder that closes more bars than fit the caller's span parks the rest; its state has already moved past the whole batch, so CurrentBar is the bar after it. The caller drains the parked bars, in order, before the next tick: while (b.HasPendingClosedBars) n = b.DrainPendingClosedBars(scratch);. A builder that closes at most one bar per tick (time, tick, the derived transforms) keeps the defaults. A builder that can close more must park rather than drop them: dropped bars leave a hole in the series.
ClosingTickBelongsToNextBar	false (default): closing tick credited to the closing bar (Volume / Tick). true: it opens the next bar (Time / Range / Renko). Order-flow uses this to finalize the snapshot before/after crediting the tick.
ResetsOnSessionBoundary	Default true, and every built-in tick builder keeps it: the forming bar force-closes at a session boundary when the series resets on a new trading day (BarSpecification.ResetOnNewTradingDay). false for a builder with no session notion.
ExposesFormingBar	Default true (every built-in builder): CurrentBar is a continuously forming tail bar. false when CurrentBar is a snapshot of the last completed bar, so each new bar is appended instead of rewriting the tail.
ForceCloseCurrentBar(Span<Bar>)	Force-close at a session boundary; returns count written.
ForceCloseAtSessionEnd(DateTime, Span<Bar>)	Force-close clamping the bar end to the exact session end.
BeginSession(DateTime)	Notify a new session open so a time-grid builder anchors its bars.
IDerivedBarBuilder
Builders whose input is another series' closed-bar stream (the host's Heikin-Ashi, Kagi, Point & Figure and Line Break). Plugin discovery binds ITickBarBuilder only; see A derived bar.

IBarBuilder.cs
public interface IDerivedBarBuilder : IBarBuilder
{
    int OnSourceBarClosed(in Bar sourceBar, Span<Bar> closedBars);
    void PreviewSourceBar(in Bar developingSourceBar);
}
Derived-builder capabilities
Opt-in interfaces an IDerivedBarBuilder may also implement. Neither is required; a builder without them rebuilds from its source series.

Interface	Member
IHistorySeedableDerivedBarBuilder	BarSeedResult SeedFromDerivedHistory(IBarSeries series, DateTime nowUtc): continue from the already-derived history instead of replaying the source. Line Break uses it to rebuild its last LineCount lines.
ISessionResettableDerivedBarBuilder	int ForceCloseAndResetForNewSession(Span<Bar> closedBars): close whatever is open at a session boundary and start clean, for a bar type that must not span sessions. Returns how many bars it wrote.
Seeding capabilities
Opt-in interfaces the history-to-live handoff probes so the first live tick continues the series. A builder implements whichever applies.

ISeedableBarBuilder.cs
public readonly record struct BarSeedResult(bool Seeded, DateTime Cutoff, bool LastSeriesBarIsInProgress)
{
    public static readonly BarSeedResult NotSeeded;
}

public interface ISeedableBarBuilder
{
    BarSeedResult SeedFromLastBar(in Bar lastBar, DateTime nowUtc);
}

public interface ISeedableDerivedBarBuilder
{
    void SeedFromLastDerivedBar(in Bar lastDerivedBar);
}

public interface IHistorySeedableBarBuilder
{
    BarSeedResult SeedFromHistory(IBarSeries series, DateTime nowUtc);
}
Interface	For
ISeedableBarBuilder	Tick builders that continue from the last bar: time re-opens the period; range, Renko, Median Renko and UniRenko open a new bar off the previous close. Volume and tick bars start fresh and implement none of these.
ISeedableDerivedBarBuilder	A derived builder whose output depends on its own previous emitted bar (Heikin-Ashi, Kagi, Point & Figure).
IHistorySeedableBarBuilder	Tick builders needing more than one bar to continue, rebuilt from the historical series. Checked before ISeedableBarBuilder when a builder implements both.
IInstrumentMetadata
Per-instrument static metadata the runtime needs, most importantly GetTickSize, used by range / Renko builders. InMemoryInstrumentMetadata is the public thread-safe fixture/registry.

IInstrumentMetadata.cs
public interface IInstrumentMetadata
{
    double GetTickSize(string instrumentId);
    bool IsRegistered(string instrumentId);

    double? TryGetPointValue(string instrumentId) => null;
    QuantityUnit GetQuantityUnit(string instrumentId) => QuantityUnit.Contract;
    double? TryGetLotSize(string instrumentId) => null;
    double? TryGetQuantityStep(string instrumentId) => null;
    double? TryGetMinQuantity(string instrumentId) => null;
    string? TryGetQuoteCurrency(string instrumentId) => null;
    string? TryGetAdjustmentVersion(string instrumentId) => null;
    InstrumentCategory? TryGetCategory(string instrumentId) => null;

    // Multi-asset facets: forex/crypto sizing and contract shape.
    double? TryGetPipSize(string instrumentId) => null;
    double? TryGetContractSize(string instrumentId) => null;
    double? TryGetMaxLeverage(string instrumentId) => null;
    double? TryGetSizeMultiplier(string instrumentId) => null;
    bool? TryGetIsInverse(string instrumentId) => null;
    int? TryGetPriceSignificantFigures(string instrumentId) => null;
}
Member	Meaning
GetTickSize(string)	Minimum price increment (MNQ = 0.25, CL = 0.01). Throws if the instrument isn't registered.
IsRegistered(string)	True when GetTickSize would return a real value.
TryGetPointValue(string)	Currency value of one full point (ES = 50), or null. Non-throwing.
GetQuantityUnit(string)	Unit the quantity is counted in. Default Contract.
TryGetLotSize(string)	For Lot instruments, base-currency units per lot (forex standard 100 000), or null.
TryGetQuantityStep(string)	Smallest tradable quantity increment, in the instrument's unit, or null.
TryGetMinQuantity(string)	Smallest order quantity the venue accepts, or null.
TryGetQuoteCurrency(string)	ISO-4217 quote/settlement currency, or null.
TryGetAdjustmentVersion(string)	Opaque corporate-action token for split-adjusted equity series, or null. Flows into the cache key.
TryGetCategory(string)	The venue's explicit instrument category, or null when none reported.
TryGetPipSize(string)	Forex pip size (the display/quote increment, distinct from tick size), or null.
TryGetContractSize(string)	Underlying units represented by one contract, or null.
TryGetMaxLeverage(string)	Maximum leverage the venue permits on the instrument, or null.
TryGetSizeMultiplier(string)	Multiplier applied to quantity when computing exposure, or null.
TryGetIsInverse(string)	Whether the instrument is an inverse (coin-margined) contract, or null when unknown.
TryGetPriceSignificantFigures(string)	Number of significant figures the venue quotes the price to, or null when the venue states a fixed price step instead. A figure count resolves to a tick only against a real price (5 figures is a 1.0 step at 64 431 and 0.00001 on a sub-dollar coin).
InMemoryInstrumentMetadata.Register(instrumentId, tickSize, pointValue?, quantityUnit?, lotSize?, quantityStep?, minQuantity?, quoteCurrency?, adjustmentVersion?, category?, pipSize?, contractSize?, maxLeverage?, isInverse?, sizeMultiplier?, priceSignificantFigures?) populates the registry and returns it for chaining; only tickSize is required.

QuantityUnit
QuantityUnit.cs
public enum QuantityUnit { Contract = 0, Share, Lot, Unit, Coin }
Value	Meaning
Contract	Futures and derivatives: whole contracts. The default.
Share	Equities: shares (whole or fractional).
Lot	Forex: standard lots (one lot = TryGetLotSize base-currency units).
Unit	Forex: raw base-currency units.
Coin	Spot crypto: fractional coin amounts.
InstrumentCategory
A venue's explicit asset-class signal, used when QuantityUnit alone is ambiguous (a forex pair and a metal/index CFD are both Lot/Unit). Carried nullable everywhere; null is the single canonical "no category reported", so the enum has no Unknown value.

InstrumentCategory.cs
public enum InstrumentCategory { Forex, Equity, Crypto, Index, Commodity, Metal, Bond, Future }
Metadata & formatting helpers
Small statics in SabrTrader.Pipeline.Bars that exist so every surface formats and resolves the same way. None of them is required to build a bar type; they are here because a plugin that renders a distance or a quantity should not invent its own rounding.

Type	Members
PriceDistanceFormatter	static: DistanceUnit NativeUnit(double tickSize, double? pipSize), double CountIn(DistanceUnit, double priceDistance, double tickSize, double? pipSize), string FormatNative(...), string FormatUnitCount(...), string FormatMoney(double amount, string? quoteCurrency, bool signed = true), string Noun(DistanceUnit, bool plural), double UnitsPerLot(double? lotSize); const double ForexStandardLotUnits = 100000.
QuantityDisplay	static: decimal InLots(decimal wireQuantity, decimal? lotSize) and string FormatInLots(decimal wireQuantity, decimal? lotSize) (each with a double? overload): turns a venue's wire quantity into the lots/units a trader reads.
QuantityUnitExtensions · InstrumentMetadataQuantityModelExtensions	string Noun(this QuantityUnit unit, bool plural) and string Label(this QuantityUnit unit, string? baseAsset, bool plural) name a quantity for display ("contracts", "BTC"); QuantityModel GetQuantityModel(this IInstrumentMetadata metadata, string instrumentId) resolves unit, step and minimum into one QuantityModel(QuantityUnit Unit, decimal Step, decimal Min).
InstrumentMetadataWarmup · IAsyncInstrumentMetadata	Pre-registering reference data before it is needed: Task EnsureRegisteredAsync(string instrumentId, ct) on the async metadata seam, and the static InstrumentMetadataWarmup.EnsureRegisteredAsync(metadata, instrumentId, wait?, ct), which awaits the venue's asynchronous resolution for up to wait (default DefaultWait, 20 seconds) and returns whether the instrument is registered.
EmptyDoubleSeries	SabrTrader.Pipeline.Series: a shared empty ISeries<double> (Instance) for an unavailable input.
IBarSeries
SabrTrader.Pipeline.Series · read-only bar series with NinjaTrader-style barsAgo indexing: this[0] is the current bar, this[1] the one before. Backed by a power-of-2 ring (struct-of-arrays); Count is monotonic, the largest legal barsAgo is Math.Min(Count, Capacity) - 1.

ISeries.cs
public interface IBarSeries
{
    BarSpecification Spec { get; }
    int SeriesIndex { get; }
    int Count { get; }
    int Capacity { get; }

    Bar this[int barsAgo] { get; }
    bool TryGet(int barsAgo, out Bar bar);

    bool TryReadCloseSpan (int fromBarsAgo, int count, out ReadOnlySpan<double> firstSlice, out ReadOnlySpan<double> secondSlice);
    bool TryReadOpenSpan  (int fromBarsAgo, int count, out ReadOnlySpan<double> firstSlice, out ReadOnlySpan<double> secondSlice);
    bool TryReadHighSpan  (int fromBarsAgo, int count, out ReadOnlySpan<double> firstSlice, out ReadOnlySpan<double> secondSlice);
    bool TryReadLowSpan   (int fromBarsAgo, int count, out ReadOnlySpan<double> firstSlice, out ReadOnlySpan<double> secondSlice);
    bool TryReadVolumeSpan(int fromBarsAgo, int count, out ReadOnlySpan<long>   firstSlice, out ReadOnlySpan<long>   secondSlice);

    ISeries<double> Close { get; }
    ISeries<double> High { get; }
    ISeries<double> Low { get; }
    ISeries<double> Open { get; }
    ISeries<double> Volume { get; }
    ISeries<double> Typical { get; }   // (H+L+C)/3
    ISeries<double> Median { get; }    // (H+L)/2
    ISeries<double> Weighted { get; }  // (H+L+2C)/4
    ISeries<double> OHLC4 { get; }     // (O+H+L+C)/4
}
Member	Meaning
Spec	Spec the series was constructed from. Stable for its lifetime.
SeriesIndex	Position in the runtime's series table. 0 = primary; 1, 2, … = series added via AddDataSeries. Routes per-bar callbacks (matches NT's BarsInProgress).
Count / Capacity	Total bars ever observed (monotonic) / ring size (power of 2).
this[int barsAgo]	O(1) random-access read; this[0] = most recent. Materialises a Bar per call.
TryGet(int, out Bar)	Safe variant: returns false during warmup where the indexer throws.
TryRead*Span(…)	Zero-copy contiguous column slices in chronological order; up to two spans when the slice wraps the ring. False when the range exceeds bars in the ring.
Close … OHLC4	Cached price-series projections (single instance per component, so indicator dedup relies on reference equality).
ISeries<T>
Read-only generic series: the shape indicator output plots expose. Warmup positions hold double.NaN for double-typed series.

ISeries.cs
public interface ISeries<T> where T : struct
{
    int Count { get; }
    int Capacity { get; }
    T this[int barsAgo] { get; }
    bool TryGet(int barsAgo, out T value);
}
IBarSourcedSeries
Marker on an ISeries<double> that knows the IBarSeries it was derived from. The indicator framework uses it to resolve a child's primary bars in indicator-of-indicator chains.

IBarSourcedSeries.cs
public interface IBarSourcedSeries
{
    IBarSeries SourceBars { get; }
}
ChunkedSeries<T>
The public writable backing for indicator output. ISeries<T> + IClearableSeries; grows without bound (4096-slot chunks), single-writer / multi-reader, NaN-aware for warmup. this[0] is the newest value.

ChunkedSeries.cs
public sealed class ChunkedSeries<T> : ISeries<T>, IClearableSeries where T : struct
{
    public int Count { get; }
    public int Capacity { get; }            // int.MaxValue (unbounded)
    public T this[int barsAgo] { get; }
    public bool TryGet(int barsAgo, out T value);

    public void Append(T value);            // becomes the new this[0]
    public void UpdateLast(T value);        // rewrite the newest value (intra-bar)
    public void Set(int barsAgo, T value);  // rewrite at an arbitrary offset (swing/pivot backfill)
    public void Clear();                    // drop all values, Count -> 0
}
IClearableSeries
Opt-in: a mutable series that can be reset to empty in place. The recalc path clears values without reallocating, so the chart's IPlot.Values references never dangle.

IClearableSeries.cs
public interface IClearableSeries
{
    void Clear();
}
