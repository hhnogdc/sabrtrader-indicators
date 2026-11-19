Reference
Ticks
The raw trade/quote update a connection emits and every bar builder, order-flow aggregator and tape consumes. Namespace SabrTrader.Pipeline.Ticks.

Tick
A readonly record struct, passed by value on the hot path; the pipeline copies it and holds no references to it. Consumers that need persistent state copy out the fields they care about.

Tick.cs
public readonly record struct Tick(
    long SequenceNumber,
    DateTime ExchangeTimestampUtc,
    string InstrumentId,
    double Price,
    long Size,
    TickFlags Flags,
    long IngressStamp = 0,
    long StageStamp = 0);
Member	Type	Meaning
SequenceNumber	long	Global ordering key the host's sequencer assigns at ingest (a venue constructs ticks with 0). The canonical ordering for the whole pipeline: replay, backtest and live all use this field.
ExchangeTimestampUtc	DateTime	Venue-reported timestamp of the trade/quote: true UTC, always, and never rewritten to a display zone. Overlay on the chart with ChartDisplayTime.FromUtc.
InstrumentId	string	The instrument this update belongs to.
Price	double	Trade price, or the quoted bid/ask price.
Size	long	Traded / quoted size.
Flags	TickFlags	Classification bitmask; see below.
IngressStamp	long	Diagnostics only. Stopwatch stamp taken when the process first saw the tick, or 0 when untraced (the common case). Never persisted: a cached tick comes back untraced. Venues and ordinary consumers leave it at 0.
StageStamp	long	Diagnostics only, meaningful only when IngressStamp is non-zero. Rolling stamp of the last pipeline hop the tick cleared, used to build the per-hop latency waterfall.
TickOrdering
Two statics for the one rule that matters when you assemble ticks yourself: bool IsChronological(IReadOnlyList<Tick> ticks) and void EnsureChronologicalStable(List<Tick> ticks), a stable sort, so ticks sharing a timestamp keep the order the venue sent them in. Bar builders assume chronological input and build wrong bars from an unsorted batch without raising an error. History sources call these before returning ticks.

TickFlags
A [Flags] enum : byte. Multiple bits can be set at once (a print at the bid is both Trade + AtBid). Bar builders consume only ticks carrying Trade; order-flow reads the aggressor bits. Bit 1 << 5 is reserved: never set it. A venue's previous-close / settlement reference travels on the level-1 summary surface (IMarketSummaryFeed / MarketSummaryUpdate), not on ticks.

TickFlags.cs
[Flags]
public enum TickFlags : byte
{
    None          = 0,
    Bid           = 1 << 0,
    Ask           = 1 << 1,
    Trade         = 1 << 2,
    AtAsk         = 1 << 3,
    AtBid         = 1 << 4,
    // 1 << 5 is reserved: never set it.
    QuoteDerived  = 1 << 6,
    Synthetic     = 1 << 7,
}
Value	Meaning
None	No classification.
Bid	Quote update reflecting the best bid.
Ask	Quote update reflecting the best ask.
Trade	An actual trade / print. The only flag bar builders act on for OHLCV.
AtAsk	Trade aggressor was a buyer (lifted the ask).
AtBid	Trade aggressor was a seller (hit the bid).
QuoteDerived	The print was derived from the quote rather than observed on a tape. A quote-driven venue (most spot forex and CFD brokers) publishes no trades, so its gateway synthesizes a price series from bid/ask to give the instrument a chart. Always set alongside Trade: to a bar builder, the tape and order flow it is the print. It matters to a consumer that needs an executable price: a synthesized mid is a price nobody traded at, and marking a short position to it overstates unrealized PnL by half the spread. Use the bid/ask side that applies instead.
Synthetic	Pipeline-injected synthetic tick (replay reconstruction, bar-builder edge cases). Most indicators treat it like any other tick.
