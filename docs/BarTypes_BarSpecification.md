Bar types
The BarSpecification
The immutable, value-comparable identity record that names your bar type and carries its parameters: the half of a custom bar type the engine uses to deduplicate work across charts.

On this page
The base record
Members you override
The built-in subtypes
Defining your own spec
Plugin parameters: [BarParameter]
A setting worth knowing: OversizedTradePolicy
The base record
A BarSpecification is an abstract record, which is deliberate: records compare by value, so two requests for the same instrument and the same settings hash and equate identically, and the engine can hand both charts one shared builder and one output series. The base record carries the instrument id, optional continuous-futures wiring, optional trading-hours (session) binding, and the hooks the host needs for labels and symbol translation. You inherit from it and add your own settings.

Key user-owned state on RollStableInstrumentId, not InstrumentId. InstrumentId is the venue-native WIRE symbol, which changes at every roll. Anything the user owns and expects to outlive a roll (an alert rule, a saved preference, a per-instrument default) must key on RollStableInstrumentId (the continuous root when the series is continuous, otherwise the instrument id), or it stops matching the moment the front month changes. Subscriptions, backfill and order routing keep using InstrumentId: the two answer different questions.
BarSpecification.cs
namespace SabrTrader.Pipeline.Bars;

public abstract record BarSpecification(string InstrumentId)
{
    // Continuous-futures intent: the product root ("ES") when the series follows the product.
    // That is a chart on the product, and also a chart on the dated contract the product is
    // live on right now: both load the product's history. Null for a month-locked contract
    // (any other month, which loads its own history only) and for anything that is not a future.
    public string? ContinuousRoot { get; init; }
    public BarAdjustmentMode ContinuousAdjustment { get; init; } = BarAdjustmentMode.BackAdjusted;

    // Trading-hours (session) binding. Null = no session filtering.
    public string? TradingHoursTemplateName { get; init; }
    public bool ResetOnNewTradingDay { get; init; } = true;

    // Specific source exchange (e.g. a delayed feed). Null = venue resolves the default.
    public string? Exchange { get; init; }

    // Tick, volume and range only: the venue's own bars (Native, default) or bars built from the
    // trade tape (Ticks). Every other bar type keeps Native.
    public BarBuildSource BuildSource { get; init; }
    public virtual bool OffersBuildSourceChoice { get; }

    // What this series is to a USER, stable across contract rolls:
    // the continuous root when continuous, else InstrumentId.
    public string RollStableInstrumentId { get; }

    // Human-readable label for logs / UI. Each concrete spec overrides it.
    public abstract string DisplayName { get; }

    // True when bars end on a time grid (a trade at a bar's end belongs to the next bar).
    public virtual bool EndsOnTimeGrid => false;

    // Symbol-translation hook. Default returns this unchanged.
    public virtual BarSpecification WithInstrumentId(string newInstrumentId) => this;

    // Convenience clones (not overridable) the host's resolution chokepoints use.
    public BarSpecification WithContinuous(string? continuousRoot, BarAdjustmentMode adjustment);
    public BarSpecification WithTradingHours(string? templateName);
    public BarSpecification WithTradingHours(string? templateName, bool resetOnNewTradingDay);
    public BarSpecification WithExchange(string? exchange);
}
Members you override
A concrete spec is a sealed record that adds its settings and overrides two members the host calls into, plus a third when its bars close on a clock.

DisplayName: the abstract label shown in chart UI and logs. Build it from InstrumentId and your settings (e.g. "MNQ Range 4t").
WithInstrumentId(string): the symbol-translation hook. When the chart layer re-targets a series from a user-typed symbol ("ES") to the venue-native one ("/ESM26:XCME"), the host calls this to clone the spec onto the new instrument. The base default returns this unchanged, so a chart on your spec keeps the user-typed symbol. Override it with a record with-expression so your settings copy across, and, for a composite spec, propagate the rewrite into any nested spec.
EndsOnTimeGrid (virtual, default false): says where a bar ends. Leave it false when a bar ends at its own last trade (tick, volume, range, Renko style), so a trade stamped at that end is already inside the bar. Override it to return true when your bars end on a time grid, so a trade stamped exactly at a closed bar's end belongs to the next bar. A spec built over another spec returns the underlying's value, the way HeikinAshiBarSpec does. The platform reads it to decide where live ticks take over after the last closed historical bar, so a wrong answer drops or double-counts the trades at that instant.
Code that needs to know how a spec's bars are cut, without switching on concrete types, asks BarConstruction.IsBuiltFromTrades(spec) and BarConstruction.IsCutOnTrades(spec) (see the bars reference).

The base also exposes WithContinuous(...), WithTradingHours(...), WithExchange(...) and WithBuildSource(...). They are non-virtual clone helpers the host uses to stamp continuous-futures, session, exchange and bar-source intent onto any spec through the record with. Because ContinuousRoot, ContinuousAdjustment, TradingHoursTemplateName, ResetOnNewTradingDay, Exchange and BuildSource are part of the record's value, two otherwise-identical specs with different sessions (one RTH, one ETH), different source exchanges (real-time vs delayed), or venue bars versus tick-built bars, get separate builders.

Continuous-futures adjustment. BarAdjustmentMode (in SabrTrader.Pipeline.Bars) has three values: None (raw stitched bars), BackAdjusted (additive, the TA-friendly default), and RatioAdjusted (multiplicative, for returns analysis). The data layer that stitches contracts applies it before bars reach any builder.
The built-in subtypes
All are sealed record types in SabrTrader.Pipeline.Bars. A custom type usually looks like one of these.

Record	Parameters
TimeBarSpec	(string InstrumentId, TimeSpan Period)
TickBarSpec	(string InstrumentId, int Ticks)
VolumeBarSpec	(string InstrumentId, long Volume, OversizedTradePolicy OversizedTrades = SplitAcrossBars)
RangeBarSpec	(string InstrumentId, int RangeTicks)
RenkoBarSpec	(string InstrumentId, int BrickTicks, bool WithWicks = false)
MedianRenkoBarSpec	(string InstrumentId, int BrickTicks, int TrendThresholdTicks)
KagiBarSpec	(string InstrumentId, int ReversalTicks, BarSpecification? Underlying = null, KagiReversalType ReversalType = Tick)
PointAndFigureBarSpec	(string InstrumentId, int BoxTicks, int Reversal, BarSpecification? Underlying = null, PointAndFigurePriceType PriceType = Close)
LineBreakBarSpec	(string InstrumentId, int LineCount, BarSpecification? Underlying = null)
UniRenkoBarSpec	(string InstrumentId, int BrickTicks, int TrendThresholdTicks, int ReversalTicks)
HeikinAshiBarSpec	(string InstrumentId, BarSpecification Underlying)
Four specs are composites over another BarSpecification. HeikinAshiBarSpec requires an Underlying (any spec, usually a TimeBarSpec). KagiBarSpec, PointAndFigureBarSpec and LineBreakBarSpec take an optional Underlying and expose ResolvedUnderlying, which is a 1-minute TimeBarSpec when Underlying is null. Their WithInstrumentId overrides rewrite both the spec and its nested underlying so the composed spec stays self-consistent.

Defining your own spec
For a plugin bar type (the usual path), use a parameterless record constructor so the host catalog and workspace restore can construct the type, put settings on init properties marked with [BarParameter], override DisplayName / WithInstrumentId, and optionally set [BarTypeDisplayName] for the picker label.

SampleFixedTickBarSpec.cs
using SabrTrader.Pipeline.Bars;

[BarTypeDisplayName("Sample Fixed Tick")]
public sealed record SampleFixedTickBarSpec() : BarSpecification("SAMPLE")
{
    [BarParameter(DisplayName = "Ticks per bar",
                  Description = "Bar closes after this many trade ticks.",
                  MinValue = 1)]
    public int Ticks { get; init; } = 50;

    public override string DisplayName => $"{InstrumentId} Sample {Ticks}t";

    public override BarSpecification WithInstrumentId(string newInstrumentId)
        => this with { InstrumentId = newInstrumentId };
}
Built-in specs use positional record parameters (TickBarSpec(string InstrumentId, int Ticks)) because the host constructs them itself. Plugin specs must be constructible with no arguments so discovery and workspace restore can create them.

Keep specs pure data. A spec is comparable, hashable, and used as a dictionary key. Hold only the parameters that define the series identity: no live state, no services, no tick size. Tick size is resolved later, when the builder is created (see Registration).
Plugin parameters: [BarParameter]
Declaring [BarParameter] on a public get+set or init property is the editor and persistence contract, the same pattern as Market Analyzer column parameters. The New Chart / Data Series dialogs reflect those properties into editors, and the host writes them into the plugin JSON params map when it saves a workspace and applies them again on restore.

BarParameterAttribute.cs
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class BarParameterAttribute : Attribute
{
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public double MinValue { get; init; } = double.NaN;   // inclusive; NaN = unbounded
    public double MaxValue { get; init; } = double.NaN;
}
Supported types: bool, int, long, double, decimal, string, TimeSpan, DateTime, enums.
Unsupported types fail at install: the catalog reflects parameters when the plugin loads, so a bad property surfaces then, before any chart opens.
[BarTypeDisplayName("…")] is optional; without it the picker uses the type's short name.
A setting worth knowing: OversizedTradePolicy
This built-in setting decides whether a series is safe for orderflow. VolumeBarSpec takes an OversizedTradePolicy alongside its volume threshold, which decides what happens when a single trade is larger than the volume remaining in the forming bar.

SplitAcrossBars (default): divide the oversized trade across consecutive bars to hit the threshold exactly. Each synthetic bar is a single price point, with the tick-count credit on the first bar only. This matches NinjaTrader's volume bars bit for bit, which is what you want for pure volume work and for comparing results, but it is wrong for footprint work.
KeepAtomic: treat the trade as one indivisible event, so the bar closes with its full traded volume even if that overshoots the threshold. This is required for orderflow correctness, because a single exchange print carries one bid/ask snapshot and one aggressor side; splitting it across bars would duplicate that state and corrupt volume-at-price and delta.
If you author a custom volume-like bar type that footprint or delta indicators will ride, offer the same choice and let those users pick KeepAtomic: a footprint indicator built on a SplitAcrossBars series shows corrupted delta.

