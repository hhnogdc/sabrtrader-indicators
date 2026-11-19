Bar types
A range bar
Price-distance bar types can close more than one bar on a single print. This is the multi-close pattern: loop the threshold, write each bar into the span, and credit the print once.

On this page
Closing several bars in one tick
Tick size comes in through the constructor
A worked range builder
Volume accounting
Closing several bars in one tick
A tick bar closes at most one bar per tick. A price-distance rule can close several: one fast print jumps across several thresholds, and each crossing is a finished bar. That is why OnTick returns a count: loop while price keeps crossing the threshold, write one bar per crossing into closedBars, and return how many you wrote.

The builder below sets ClosingTickBelongsToNextBar => true: the crossing print opens the next bar, and each new bar opens at the previous bar's close (the threshold price). The built-in RangeBarSpec builder follows the NinjaTrader convention instead and opens the next bar one tick beyond the clamped close.

Tick size comes in through the constructor
RangeTicks is a count, but the rule operates in price, so the builder needs the instrument's tick size (the minimum price increment) to turn "4 ticks" into a price distance. The host resolves it once, when it creates the builder, and passes it to the constructor as a plain double (see Registration). The builder holds no IInstrumentMetadata reference.

A worked range builder
The plugin spec MyRangeBarSpec below carries one int RangeTicks parameter, and its builder does the work. It opens a bar at the first trade, then on every later trade closes as many bars as the move spans. Each closed bar is clamped to the threshold price and the next bar opens there. Bars that do not fit the caller's span are parked and delivered through DrainPendingClosedBars. As a threshold builder it keeps ResetsOnSessionBoundary at its default of true and implements ForceCloseCurrentBar, so the forming bar is flushed at the session boundary.

MyRangeBarBuilder.cs
using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Ticks;

[BarTypeDisplayName("My Range")]
public sealed record MyRangeBarSpec() : BarSpecification("SAMPLE")
{
    [BarParameter(DisplayName = "Range (ticks)", MinValue = 1)]
    public int RangeTicks { get; init; } = 4;

    public override string DisplayName => $"{InstrumentId} MyRange {RangeTicks}t";
    public override BarSpecification WithInstrumentId(string newInstrumentId)
        => this with { InstrumentId = newInstrumentId };
}

[BarBuilderFor(typeof(MyRangeBarSpec), NeedsTickSize = true)]
public sealed class MyRangeBarBuilder : ITickBarBuilder
{
    private readonly MyRangeBarSpec _spec;
    private readonly double _range;                    // RangeTicks * tickSize, resolved by the host
    private readonly Queue<Bar> _pending = new();     // closed bars that did not fit the caller's span
    private bool _has;
    private double _o, _h, _l, _c;
    private long _vol, _ticks;
    private DateTime _start, _end;

    public MyRangeBarBuilder(MyRangeBarSpec spec, double tickSize)
    {
        _spec = spec;
        _range = spec.RangeTicks * tickSize;
    }

    public BarSpecification Spec => _spec;
    public bool HasCurrentBar => _has;
    public Bar CurrentBar => _has
        ? new Bar(_start, _end, _o, _h, _l, _c, _vol, _ticks)
        : throw new InvalidOperationException("No bar in progress.");

    // The threshold-crossing tick opens the NEXT bar and is credited to it.
    public bool ClosingTickBelongsToNextBar => true;

    public int OnTick(in Tick tick, Span<Bar> closedBars)
    {
        if ((tick.Flags & TickFlags.Trade) == 0) return 0;
        DateTime ts = tick.ExchangeTimestampUtc;
        if (!_has) { Open(tick.Price, ts); Fold(tick.Price, tick.Size, ts); return 0; }

        int written = 0;
        // One print may cross several thresholds: close one bar per crossing.
        while (tick.Price >= _o + _range || tick.Price <= _o - _range)
        {
            double edge = tick.Price > _o ? _o + _range : _o - _range;
            _h = Math.Max(_h, edge); _l = Math.Min(_l, edge);
            var closed = new Bar(_start, ts, _o, _h, _l, edge, _vol, _ticks);
            if (written < closedBars.Length) closedBars[written++] = closed;
            else _pending.Enqueue(closed);           // park what does not fit; never drop a bar
            Open(edge, ts);                          // next bar opens at the threshold
        }
        Fold(tick.Price, tick.Size, ts);             // the print belongs to the bar now forming
        return written;
    }

    public bool HasPendingClosedBars => _pending.Count > 0;

    public int DrainPendingClosedBars(Span<Bar> closedBars)
    {
        int n = 0;
        while (n < closedBars.Length && _pending.Count > 0) closedBars[n++] = _pending.Dequeue();
        return n;
    }

    // Threshold builder: flush the forming bar at a session boundary.
    public int ForceCloseCurrentBar(Span<Bar> closedBars)
    {
        if (!_has) return 0;
        closedBars[0] = CurrentBar;
        _has = false;
        return 1;
    }

    private void Open(double price, DateTime ts)
    {
        _has = true; _start = ts; _end = ts;
        _o = _h = _l = _c = price; _vol = 0; _ticks = 0;
    }

    private void Fold(double price, long size, DateTime ts)
    {
        _h = Math.Max(_h, price); _l = Math.Min(_l, price); _c = price;
        _end = ts; _vol += size; _ticks++;
    }
}
Volume accounting
All of those bars closed on one print, so crediting the print's volume to each of them would multiply it. With ClosingTickBelongsToNextBar true the print belongs to the bar that is forming after the crossings: the first closed bar keeps the volume it accrued before this tick, the intermediate bars that opened and closed on the same print carry zero, and the print's size and tick count go to the new forming bar. The built-in range and Renko builders account the same way.

Volume accounting. A single print is one event. Credit its volume and tick count to exactly one bar, and make that bar agree with ClosingTickBelongsToNextBar: the closing bar when it is false, the next bar when it is true. The orderflow aggregator attributes the print's bid/ask volume by the same flag, so a mismatch splits a bar's volume from its footprint.
Range bars chain, so they seed. Because each bar opens off the previous close, a range series continues from history at the live handoff; without seeding it shows a phantom gap there. The built-in range, Renko, Median Renko and UniRenko builders implement ISeedableBarBuilder; see Seeding the live handoff.
