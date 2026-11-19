Bar types
A derived bar
Some bar types transform another bar series instead of consuming ticks. Heikin-Ashi is the canonical example, and it shows the two-method derived pattern the host's transforms implement.

ADVANCED A derived bar (transform another series)
A derived builder implements IDerivedBarBuilder instead of ITickBarBuilder. It sits downstream of another series and reshapes its bars. Because the source has both committed (closed) bars and a live forming bar, the interface splits into two methods:

OnSourceBarClosed(in Bar, Span<Bar>): the upstream series closed a bar. Compute the transformed bar, advance the running state, write it into closedBars, and return the count (always 1 for Heikin-Ashi; Kagi, Point & Figure and Line Break return 0 until their rule closes a bar).
PreviewSourceBar(in Bar): the upstream forming bar changed on a tick. Refresh CurrentBar from it so the live tail propagates, and leave the previous-bar state untouched: the developing source bar changes again on the next tick, and the next real close must compute from the last committed bar.
The Heikin-Ashi shape below applies one formula in one place; only OnSourceBarClosed writes _prevHaOpen / _prevHaClose.

HaShapeBuilder.cs (shape)
using System;
using SabrTrader.Pipeline.Bars;

public sealed class HaShapeBuilder : IDerivedBarBuilder
{
    private double _prevHaOpen, _prevHaClose;
    private bool _seeded, _has;
    private Bar _current;

    public BarSpecification Spec { get; }
    public bool HasCurrentBar => _has;
    public Bar CurrentBar => _has ? _current : throw new InvalidOperationException("No bar in progress.");

    public HaShapeBuilder(HeikinAshiBarSpec spec) => Spec = spec;

    public int OnSourceBarClosed(in Bar s, Span<Bar> closedBars)
    {
        Bar ha = ComputeHa(s);
        _prevHaOpen = ha.Open; _prevHaClose = ha.Close; _seeded = true;   // advance recurrence
        closedBars[0] = ha; _has = true; _current = ha;
        return 1;                       // one HA bar per source bar
    }

    public void PreviewSourceBar(in Bar developing)
    {
        _current = ComputeHa(developing);   // refresh live tail; do NOT touch _prev*
        _has = true;
    }

    private Bar ComputeHa(in Bar s)
    {
        double close = (s.Open + s.High + s.Low + s.Close) / 4;
        double open  = _seeded ? (_prevHaOpen + _prevHaClose) / 2 : (s.Open + s.Close) / 2;
        double high  = Math.Max(s.High, Math.Max(open, close));
        double low   = Math.Min(s.Low,  Math.Min(open, close));
        return new Bar(s.StartUtc, s.EndUtc, open, high, low, close, s.Volume, s.TickCount);
    }
}
Derived over anything. The host wraps a derived builder over any underlying tick builder, so Heikin-Ashi over range bars works through composition and the transform only implements the formula. That is why HeikinAshiBarSpec.Underlying is an open BarSpecification rather than a fixed time bar.
Registration. Plugin discovery binds ITickBarBuilder implementations only; a class that implements IDerivedBarBuilder is not picked up from a plugin DLL. The derived contract is public so you can see how the built-in transforms (Heikin-Ashi, Kagi, Point & Figure, Line Break) behave at the live handoff and at session boundaries.
Continuing the recurrence across the live handoff
A derived transform whose output depends on its own previous bar (Heikin-Ashi's prevHaOpen / prevHaClose) needs that recurrence state to survive the history-to-live boundary; otherwise the first live bar falls back to its first-bar formula and visibly jumps. It opts in by also implementing ISeedableDerivedBarBuilder:

SabrTrader.Pipeline.Bars
public interface ISeedableDerivedBarBuilder
{
    void SeedFromLastDerivedBar(in Bar lastDerivedBar);
}
At the handoff the series holds the derived bars, so the last one carries exactly the recurrence state the transform needs. The runtime hands it to SeedFromLastDerivedBar; your implementation primes _prevHaOpen / _prevHaClose (and sets _seeded) so the next derived bar continues correctly. The underlying tick builder starts fresh, because a lossy transform cannot be inverted from a derived bar; that first begin-of-period gap matches any non-derived chart.

Other derived seams
Two more opt-in interfaces complete the derived contract:

IHistorySeedableDerivedBarBuilder.SeedFromDerivedHistory(IBarSeries series, DateTime nowUtc) returns a BarSeedResult and lets a transform that looks back several derived bars rebuild its window from the historical derived series. Line Break uses it for its last LineCount lines.
ISessionResettableDerivedBarBuilder.ForceCloseAndResetForNewSession(Span<Bar> closedBars) closes whatever is open at a session boundary, writes it into the span, returns the count, and starts the next session clean.
The tick-builder seeding seams are on Registration & discovery.

