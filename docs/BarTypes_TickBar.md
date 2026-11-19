Bar types
A tick bar
The simplest complete builder: accumulate ticks into OHLC and close a bar every N trades. This is the whole pattern for a tick-driven type, end to end.

SIMPLE A tick bar, end to end
A tick bar closes after a fixed number of trades. The builder below pairs with a plugin-style SampleFixedTickBarSpec (see The BarSpecification): it holds the forming bar's state in plain fields, seeds them on the first trade, folds each subsequent trade into the high / low / close, and emits a finished Bar the moment the tick count reaches the spec's Ticks. Four details matter:

It gates on TickFlags.Trade: only trades count toward an OHLCV bar.
CurrentBar throws while no bar is forming, exactly as the contract requires.
It clears _has after closing, so the next trade opens a fresh bar.
It implements ForceCloseCurrentBar, because ResetsOnSessionBoundary defaults to true and the runtime flushes the forming bar at a session boundary.
SampleFixedTickBarBuilder.cs
using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Ticks;

[BarBuilderFor(typeof(SampleFixedTickBarSpec))]
public sealed class SampleFixedTickBarBuilder : ITickBarBuilder
{
    private readonly SampleFixedTickBarSpec _spec;
    private bool _has;
    private double _o, _h, _l, _c;
    private long _vol, _ticks;
    private DateTime _start, _end;

    public SampleFixedTickBarBuilder(SampleFixedTickBarSpec spec) => _spec = spec;

    public BarSpecification Spec => _spec;
    public bool HasCurrentBar => _has;

    // Contract: CurrentBar throws when no bar is forming; callers check HasCurrentBar first.
    public Bar CurrentBar => _has
        ? new Bar(_start, _end, _o, _h, _l, _c, _vol, _ticks)
        : throw new InvalidOperationException("No bar in progress.");

    public int OnTick(in Tick tick, Span<Bar> closedBars)
    {
        if ((tick.Flags & TickFlags.Trade) == 0) return 0;   // trades only; ignore quotes
        _end = tick.ExchangeTimestampUtc;                    // latest trade time for CurrentBar/close

        if (!_has)
        {
            _has = true; _start = tick.ExchangeTimestampUtc;
            _o = _h = _l = _c = tick.Price; _vol = tick.Size; _ticks = 1;
        }
        else
        {
            _h = Math.Max(_h, tick.Price); _l = Math.Min(_l, tick.Price);
            _c = tick.Price; _vol += tick.Size; _ticks++;
        }

        if (_ticks >= _spec.Ticks)
        {
            closedBars[0] = new Bar(_start, tick.ExchangeTimestampUtc,
                                    _o, _h, _l, _c, _vol, _ticks);
            _has = false;                 // next tick opens a fresh bar
            return 1;
        }
        return 0;
    }

    // Session boundary: flush the forming bar so it never spans the overnight gap.
    public int ForceCloseCurrentBar(Span<Bar> closedBars)
    {
        if (!_has) return 0;
        closedBars[0] = CurrentBar;
        _has = false;
        return 1;
    }
}
Where the closing tick belongs
A tick bar leaves ClosingTickBelongsToNextBar at its default of false: the trade that reaches the threshold is the last tick of the bar that just closed, and its volume + aggressor side are credited to that closing bar. That is the correct accounting for any builder that accumulates "up to" a count or volume threshold. Contrast this with range / Renko bars, where the threshold-crossing tick opens the next band, covered on the next page.

No chaining, no seeding. Each tick bar opens at the next trade's own price, so the series does not chain across a gap. Tick bars therefore implement no seeding interface: at the history-to-live handoff the first live tick opens a fresh bar, which is the correct result. See Seeding for the bar types that do chain.
