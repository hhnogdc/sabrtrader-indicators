Bar types
Overview
Custom candle aggregation. Turn a raw tick stream into your own bars (tick, volume, range, Renko, or something proprietary) with the public SabrTrader.Pipeline.Bars contracts.

On this page
Two pieces: a spec + a builder
Plugin bar types (drop a DLL)
How a bar series is built
Built-in bar types
Where to go next
Two pieces: a spec + a builder
Every custom bar type is two cooperating types: a description of what the bars are, and a worker that produces them. Keeping them apart lets the engine deduplicate work: the spec is a cheap, comparable identity shared across charts, and the builder is the stateful machine that runs once per distinct spec. You implement both; the host wires them together.

A BarSpecification subclass: an immutable, value-comparable identity record carrying the parameters (instrument id + your settings). Two charts asking for an identical spec share one builder and one output series.
A builder that implements ITickBarBuilder and is driven by raw Ticks. It accumulates trades and emits closed Bar values. (The host's own transforms such as Heikin-Ashi use IDerivedBarBuilder, driven by another series' bars; plugin discovery registers tick builders.)
Both live in the namespace SabrTrader.Pipeline.Bars (with Tick in SabrTrader.Pipeline.Ticks). A new bar type is one record plus one builder, paired by an attribute.

Plugin bar types (drop a DLL)
For a user plugin the host does the wiring for you. Annotate the pair, put the DLL in Documents\SabrTrader\Plugins, and the New Chart / Data Series pickers show your type with editable parameters. Workspaces persist it as "type":"plugin".

[BarTypeDisplayName("…")] on the spec: the picker label (the type name when absent).
[BarParameter(…)] on public get/set or init properties: editor fields and workspace persistence (bool, numeric, string, TimeSpan, DateTime, enums).
[BarBuilderFor(typeof(TSpec))] on an ITickBarBuilder: pairs the builder with its spec; set NeedsTickSize = true when the builder needs (TSpec, double tickSize).
Specs need a parameterless (or all-optional) constructor so the catalog and workspace restore can construct them; builders take (TSpec) or (TSpec, double tickSize) and are never zero-arg constructed. See Defining your own spec and Registration & discovery for the full contract.

How a bar series is built
The runtime never talks to your builder directly. The host resolves the spec to a fresh builder, feeds the builder its input, and collects the closed Bar values into an IBarSeries that indicators, strategies, and the chart read.

graph LR
  Spec[BarSpecification] -->|resolve| F[Host builder factory]
  F -->|Create| B[IBarBuilder]
  T[Tick / source bar] -->|OnTick / OnSourceBarClosed| B
  B -->|closed Bar values| S[IBarSeries]
  S --> C[Indicators · strategies · chart]
Built-in bar types
The host ships the bar types below. Each row is a spec record plus a builder that closes a bar on its own rule, and your custom type sits beside them under the same rules. Most proprietary bar types are a variation on one of these.

Spec	Closes when…
TimeBarSpec(id, Period)	every Period elapses (e.g. 5 min, epoch-aligned).
TickBarSpec(id, Ticks)	Ticks trades have accumulated.
VolumeBarSpec(id, Volume, OversizedTrades)	traded volume reaches Volume.
RangeBarSpec(id, RangeTicks)	price moves RangeTicks × tickSize from the open.
RenkoBarSpec(id, BrickTicks, WithWicks)	price moves one brick (reversal = 2 bricks).
MedianRenkoBarSpec(id, BrickTicks, TrendThresholdTicks)	continuation / reversal thresholds are crossed.
KagiBarSpec(id, ReversalTicks)	price reverses ReversalTicks from the active extreme.
UniRenkoBarSpec(id, BrickTicks, TrendThresholdTicks, ReversalTicks)	price moves TrendThresholdTicks in the trend direction or ReversalTicks against it.
PointAndFigureBarSpec(id, BoxTicks, Reversal)	a column completes (box + reversal rule).
LineBreakBarSpec(id, LineCount)	price breaks the last LineCount lines' high/low.
HeikinAshiBarSpec(id, Underlying)	derived: transforms any underlying spec's bars.
Which of these a chart offers depends on the venue; see which venues offer your bar type. How specs persist to JSON is covered on the same page.

Where to go next
The rest of this section covers the lifecycle of a custom bar type: the identity record, a worked tick builder, a multi-close range builder, the derived-transform contract, and registration, seeding and discovery.

Page	What it covers
The BarSpecification	The immutable identity record, its built-in subtypes, and how to subclass it.
Builder interfaces	IBarBuilder, ITickBarBuilder, IDerivedBarBuilder, the Bar struct, and the closedBars span.
A tick bar	A complete SIMPLE tick-driven builder, end to end.
A range bar	Closing several bars in one tick, and where the crossing print's volume goes.
A derived bar	The ADVANCED transform contract over another series (Heikin-Ashi).
Registration & discovery	Mapping a spec to its builder, seeding the live handoff, persistence, and availability.
