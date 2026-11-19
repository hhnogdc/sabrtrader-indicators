Bar types
Builder interfaces
The stateful workers that turn input into closed bars. A plugin bar type implements ITickBarBuilder to aggregate raw ticks; IDerivedBarBuilder is the contract the host's transforms over another series implement.

On this page
The shared surface, plus one drive method
The Bar you produce
The Tick you consume
Contract rules to honour
Optional: session-boundary hooks
The shared surface, plus one drive method
The spec is the cheap identity; the builder does the work. All builders share a small common surface (the spec they belong to, whether a bar is forming, and the in-progress bar), and each kind adds one drive method the engine calls as input arrives.

IBarBuilder.cs
namespace SabrTrader.Pipeline.Bars;

public interface IBarBuilder
{
    BarSpecification Spec { get; }
    bool HasCurrentBar { get; }   // false until the first bar starts forming
    Bar CurrentBar { get; }       // throws while HasCurrentBar is false
}

// Driven by raw ticks. Returns how many bars this single tick CLOSED.
public interface ITickBarBuilder : IBarBuilder
{
    int OnTick(in Tick tick, Span<Bar> closedBars);   // usually 0 or 1; Range/Renko can close N

    bool ClosingTickBelongsToNextBar => false;        // does the closing tick open the next bar?

    // Overflow: bars closed beyond the caller's span are parked and drained here.
    bool HasPendingClosedBars => false;
    int DrainPendingClosedBars(Span<Bar> closedBars) => 0;
}

// Driven by another series' bars (e.g. Heikin-Ashi over time bars).
public interface IDerivedBarBuilder : IBarBuilder
{
    int OnSourceBarClosed(in Bar sourceBar, Span<Bar> closedBars);  // commit + advance state
    void PreviewSourceBar(in Bar developingSourceBar);             // refresh live tail, no advance
}
The pivotal detail in both drive methods is the closedBars span and its return value. Rather than allocating a list, the caller hands you a pre-sized Span<Bar> to write into and trusts the integer you return to say how many entries are valid. This keeps the hot path allocation-free, but it puts the contract on you: write exactly the bars you closed, in chronological order, and return that count.

Plugin discovery registers ITickBarBuilder implementations only (see Registration). IDerivedBarBuilder is documented on A derived bar so you can read how the built-in transforms behave.

The closedBars span. Write each newly-completed bar into the caller's span and return the count. Returning 0 means "the current bar is still forming". A single tick can legitimately close several bars (a range/Renko tick that jumps multiple thresholds), so write them all and return N. The count is not bounded: a small Renko brick against a thin-book gap print closes as many bricks as the move contains, which can be more than the caller's span holds. That is what HasPendingClosedBars / DrainPendingClosedBars are for (see the contract rules).
The Bar you produce
Both drive methods produce Bar values. Mind the constructor order: time is an open stamp and a close stamp (there is no single Time field), and tick count follows volume. Bar is a readonly record struct passed by value through the pipeline. Builders emit true venue UTC only and leave StartDisplay / EndDisplay at their default. The host stamps the chart timeline at the chart boundary.

Bar.cs
namespace SabrTrader.Pipeline.Bars;

public readonly record struct Bar(
    DateTime StartUtc,    // true venue UTC open: what a builder writes
    DateTime EndUtc,      // true venue UTC close (closing tick's timestamp for non-time bars)
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    long TickCount,       // underlying ticks that formed this bar; load-bearing for orderflow
    DateTime StartDisplay = default,  // host-stamped; builders leave default
    DateTime EndDisplay   = default);
Indicators read the chart timeline via Bar.Start / Bar.End. Do not write display wall-clock into StartUtc / EndUtc: that is the cache and identity clock. See Bars & series.
The Tick you consume
An ITickBarBuilder reads Tick values from SabrTrader.Pipeline.Ticks. Bar builders consume only ticks with the TickFlags.Trade bit set; bid/ask quote updates carry no volume for a bar.

Tick.cs
namespace SabrTrader.Pipeline.Ticks;

public readonly record struct Tick(
    long SequenceNumber,
    DateTime ExchangeTimestampUtc,
    string InstrumentId,
    double Price,
    long Size,
    TickFlags Flags,
    long IngressStamp = 0,   // diagnostics only (latency tracing); leave 0
    long StageStamp = 0);    // diagnostics only

[Flags]
public enum TickFlags : byte
{
    None = 0, Bid = 1, Ask = 2, Trade = 4,
    AtAsk = 8, AtBid = 16, QuoteDerived = 64, Synthetic = 128,
}
Gate every OnTick on the trade bit before folding the price (if ((tick.Flags & TickFlags.Trade) == 0) return 0;) and use tick.ExchangeTimestampUtc for your bar's StartUtc / EndUtc (true UTC; the host stamps display wall-clock separately).

Contract rules to honour
The builder contract has a few sharp edges the runtime relies on. Get these right and your bar type behaves like a built-in; get them wrong and you will see exceptions or mis-credited volume.

CurrentBar must throw when nothing is forming. Reading it while HasCurrentBar is false throws (the built-ins throw InvalidOperationException); callers check HasCurrentBar first. A zero-filled bar would be read as real data.
Do not overflow closedBars, and never drop bars. Writing past the span's length throws. If your rule can close more bars than fit, write what fits, return that count, and park the rest inside the builder: HasPendingClosedBars returns true until they are delivered, and each DrainPendingClosedBars(span) call writes the next chunk in order and returns its count (0 when nothing is parked). Advance your state over the whole batch before returning, so CurrentBar is already the bar after it; the parked bars are a delivery backlog. The caller drains before feeding the next tick. A builder that closes at most one bar per tick (time, tick) keeps the defaults.
ClosingTickBelongsToNextBar drives volume attribution. false (the default) credits the closing tick's volume to the bar that just closed (volume / tick bars). true means the threshold-crossing tick opens the next bar instead (time / range / Renko), and its print is credited to that new bar. The orderflow aggregator reads the same flag to decide which bar a print belongs to.
Optional: session-boundary & forming-bar hooks
ITickBarBuilder exposes several members with default implementations. A threshold builder implements ForceCloseCurrentBar; the rest matter only for time-grid builders or unusual forming-bar semantics.

ITickBarBuilder.cs (defaults)
// Force-close the developing bar at a session boundary ("Break at EOD")?
bool ResetsOnSessionBoundary => true;             // true for every built-in tick builder
int ForceCloseCurrentBar(Span<Bar> closedBars) => 0;
int ForceCloseAtSessionEnd(DateTime sessionEnd, Span<Bar> closedBars)
    => ForceCloseCurrentBar(closedBars);          // time builders clamp END to the close
void BeginSession(DateTime sessionBegin) { }      // time-grid builders anchor to session open

// Is CurrentBar a continuously-forming tail bar, or just the last completed bar?
bool ExposesFormingBar => true;                   // true for every built-in builder
ResetsOnSessionBoundary: true (the default, and every built-in tick builder) means the forming bar force-closes at a session boundary and the next session re-anchors, so a bar never spans the overnight gap. The user turns this off per series with BarSpecification.ResetOnNewTradingDay. Return false only for a builder with no session notion at all.
ForceCloseCurrentBar / ForceCloseAtSessionEnd: the session runtime calls these on the boundary tick when ResetsOnSessionBoundary is true and the series resets on a new trading day. The default no-op suits builders that opt out; threshold builders implement ForceCloseCurrentBar, and time builders also override ForceCloseAtSessionEnd to clamp the bar's end to the exact session close.
BeginSession: lets a time-grid builder anchor its bars to the session open (an RTH 60-minute chart runs 09:30 to 10:30, not 09:00 to 10:00). A no-op for non-time builders.
ExposesFormingBar: true when CurrentBar is a live tail bar the runtime opens, updates per tick, and finalises. Return false when CurrentBar is a snapshot of the last completed bar; the runtime then appends each new bar instead of rewriting the tail.
