Indicators
Child indicators
Compose indicators from other indicators. A child is initialised and backfilled before its parent reads it, so the parent always sees up-to-date child output. MACD, for example, is a fast EMA minus a slow EMA.

On this page
Composition, not copy-paste
Example: a composed MACD
Feeding a child a different input
Disposal & the tree guards
Composition, not copy-paste
Many indicators are built from simpler ones: MACD is two EMAs, Bollinger Bands are an SMA plus a standard-deviation band, Stochastic %D is an SMA of %K. Instead of repeating the EMA maths inside MACD, you attach a child EMA and read its output.

You create a child by passing this as the parent argument to its constructor, from your OnInit (after base.OnInit(ctx)) or later. The IndicatorBase constructor calls parent.AttachChild(this) for you, which requires the parent to have a context, so a child cannot be created from the parent's constructor. The child's own OnInit and OnDataLoaded run after your OnInit returns and before your OnDataLoaded, so your backfill loop reads a fully backfilled child.

Children publish series, not values. A child exposes its result as an ISeries<double>. The convention is a public Output property; multi-output indicators expose named series such as Upper, Middle and Lower. You read it bars-ago, like any other series.
Example: a composed MACD
MACD is fast EMA minus slow EMA, with a signal EMA of that difference. The example ships its own tiny EMA as the child. It is declared internal: the plugin loader only lists public indicator types, so the helper stays out of the indicator picker.

The host runs every attached child before its parent, so by the time your OnBarUpdate runs, _fast.Output[0] and _slow.Output[0] already hold this update's values. Each child follows its own Calculate mode. This is the same on a chart, inside a strategy (live, backtest and optimizer) and in the Market Analyzer.

ComposedMacd.cs
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorDescription("MACD: the difference between a fast and a slow EMA, with a signal line.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class ComposedMacd : IndicatorBase, IIndicatorPanelHint
{
    private EmaLine _fast = null!, _slow = null!;
    private readonly ChunkedSeries<double> _macd   = new();
    private readonly ChunkedSeries<double> _signal = new();
    private readonly ChunkedSeries<double> _hist   = new();

    [IndicatorParameter(DisplayName = "Fast",   Group = "Parameters", Order = 0)] public int Fast   { get; set; } = 12;
    [IndicatorParameter(DisplayName = "Slow",   Group = "Parameters", Order = 1)] public int Slow   { get; set; } = 26;
    [IndicatorParameter(DisplayName = "Signal", Group = "Parameters", Order = 2)] public int Signal { get; set; } = 9;

    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    public ComposedMacd(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Histogram", _hist,   PlotStyle.Histogram, new ChartColor(120, 120, 120), 1.0));
        AddPlot(new Plot("MACD",      _macd,   PlotStyle.Line,      new ChartColor(33, 150, 243), 1.5));
        AddPlot(new Plot("Signal",    _signal, PlotStyle.Line,      new ChartColor(244, 67, 54),  1.5));
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);                     // resolves Input and Bars
        _fast = new EmaLine(this, Input, Fast);
        _slow = new EmaLine(this, Input, Slow);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Both children are backfilled before this runs.
        for (int i = Input.Count - 1; i >= 0; i--)
            Step(_fast.Output[i] - _slow.Output[i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        Step(_fast.Output[0] - _slow.Output[0]);   // both children already ran for this update
    }

    private void Step(double macd)
    {
        double k = 2.0 / (Signal + 1);
        double signal = _signal.Count == 0 ? macd : _signal[0] + k * (macd - _signal[0]);
        _macd.Append(macd);
        _signal.Append(signal);
        _hist.Append(macd - signal);
    }
}

// Minimal EMA child. Internal, so it does not appear in the indicator picker.
internal sealed class EmaLine : IndicatorBase
{
    private readonly ChunkedSeries<double> _out = new();
    private readonly int _period;

    public ISeries<double> Output => _out;

    public EmaLine(IIndicator? parent, ISeries<double> input, int period) : base(parent, input)
        => _period = period;

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        for (int i = Input.Count - 1; i >= 0; i--) Step(Input[i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx) => Step(Input[0]);

    private void Step(double x)
    {
        double k = 2.0 / (_period + 1);
        _out.Append(_out.Count == 0 ? x : _out[0] + k * (x - _out[0]));
    }
}
Composition is optional. Inlining the maths gives a tighter hot path, and the signal line above is inlined for that reason. Use children when they keep the maths readable or let you reuse an existing indicator.
Feeding a child a different input
The IndicatorBase constructor is (IIndicator? parent = null, ISeries<double>? input = null, IBarSeries? bars = null). Pass whatever series the child should read: your resolved Input, a price projection such as Bars.High or Bars.Typical, or another child's Output. Pass bars for a child that reads OHLC. When both are null the child defaults to the primary series (Bars(0) and its close).

Children can be stacked into chains. Siblings are initialised and advanced in attach order, so create an upstream child before the child that reads it.

chaining children
var onHigh   = new EmaLine(this, Bars.High, 14);                                     // EMA of the highs
var smoothed = new EmaLine(this, new IndicatorOutputSeries(onHigh.Output, Bars), 5);  // EMA of that EMA
When you hand a computed series to a child, wrap it in new IndicatorOutputSeries(series, Bars). The wrapper implements IBarSourcedSeries, whose SourceBars tells the framework which bar series the values belong to, so the child gets the right primary series along an indicator-of-indicator chain. Price projections such as Bars.High carry this already. A series without it makes the child fall back to the parent's primary series.

What a child gets
A child is a full indicator with its own context, at any depth of the tree. Everything it asks for reaches it directly, exactly as it would if you added it to a chart on its own:

its own data series: a series it adds with AddDataSeries in its OnInit is its own, read at its own index;
live ticks when it sets ProcessesMarketData;
depth, market-by-order and book transitions when it sets ProcessesMarketDepth, and the order book through ctx.LeaseOrderBook (see OrderBookLease);
data connection events.
So a composite can host an order-flow indicator, a tape-speed indicator on a 1-second series or a liquidity scan, and read its outputs, with no extra wiring. The host claims the venue's depth and market-by-order streams only while something in the tree reads the book. A hosted child computes; its plots are yours to read, and only the indicator the user added draws.

Driving an indicator yourself
An indicator you create with parent: null and keep for yourself (for example inside a test harness or a tool that steps indicators by hand) has no host. Call AdvanceChildren(ctx) at the top of its OnBarUpdate to run its children for that update. Under a host the call does nothing, because the host already ran them.

Disposal & the tree guards
Disposing a parent disposes every child in reverse-attach order, and disposing a child detaches it from its parent, so leave child disposal to the framework. Your OnDispose only releases what you allocated directly (timers, native handles).

When the user edits parameters, the framework disposes all children and runs your OnInit again, so creating children in OnInit rebuilds exactly the tree the new parameters need.

AttachChild throws InvalidOperationException when the child would form a cycle, when the child is already attached to a different parent, or when the chain would grow deeper than IndicatorBase.MaxChildDepth (16). DetachChild removes a child without disposing it.

