Bar types
Registration & discovery
How your spec and builder are wired together at runtime: [BarBuilderFor] plugin pairing, tick size, seeding the live handoff, how your parameters persist, and which venues offer your bar type.

On this page
How the host builds your bar
Plugin pairing: [BarBuilderFor]
Tick size & instrument metadata
Seeding the live handoff
Persistence of your parameters
Which venues offer your bar type
How the host builds your bar
A deployed plugin registers nothing by hand: dropping the DLL is enough. The host scans it, files each BarSpecification subclass into the bar-type catalog, and binds each ITickBarBuilder to the spec it builds. Whenever the runtime needs a builder for one of your specs, the host constructs a fresh one from that binding.

The registry is host-side. The builder factory and the process-wide registry belong to the host, and SabrTrader.Sdk does not include them. A plugin has no registry object to call: the attribute (or the constructor signature) is the registration.
In a unit test, construct the builder directly with the spec it was written for, as the host does:

Testing a builder
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Ticks;

// A tick-count bar needs no tick size.
var builder = new SampleFixedTickBarBuilder(new SampleFixedTickBarSpec { InstrumentId = "ES", Ticks = 100 });

// A price-distance bar takes its tick size as the second argument, resolved by the
// caller: the pair [BarBuilderFor(..., NeedsTickSize = true)] declares.
var range = new MyRangeBarBuilder(new MyRangeBarSpec { InstrumentId = "ES", RangeTicks = 4 }, tickSize: 0.25);
Plugin pairing: [BarBuilderFor]
Annotate the builder class with the spec type it constructs. The host reads the attribute and binds a factory that prefers a (TSpec, double tickSize) constructor, then (TSpec). Without the attribute the host infers the spec type from a single public (TSpec) or (TSpec, double tickSize) constructor; a builder with constructors for two different spec types needs the attribute. The attribute class is BarBuilderForAttribute; the type you pass is readable back as its SpecType property, which the host's plugin installer reads to register the pair.

SampleFixedTickBarBuilder.cs
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Ticks;

[BarBuilderFor(typeof(SampleFixedTickBarSpec))]
public sealed class SampleFixedTickBarBuilder : ITickBarBuilder
{
    public SampleFixedTickBarBuilder(SampleFixedTickBarSpec spec) { /* … */ }
    // ITickBarBuilder members…
}

// Price-distance builders that need tick size:
[BarBuilderFor(typeof(MyRangeBarSpec), NeedsTickSize = true)]
public sealed class MyRangeBarBuilder : ITickBarBuilder
{
    public MyRangeBarBuilder(MyRangeBarSpec spec, double tickSize) { /* … */ }
}
Constructor rules. Specs are constructible with no arguments (plugin discovery and workspace restore create them). Builders take the paired spec, plus the tick size when they need one. The builder must implement ITickBarBuilder: a class that implements only IDerivedBarBuilder is not discovered, and the install reports its spec as having no builder. A builder with both constructors receives the tick size whenever the host has instrument metadata.
Tick size & instrument metadata
A price-distance bar type (range, Renko, Kagi, Point&Figure, …) needs the instrument's tick size to turn a setting like "4 ticks" into a price distance. The host reads it from IInstrumentMetadata when it creates the builder (GetTickSize(spec.InstrumentId)) and passes the resulting double to the constructor. Your builder receives a ready tick size and holds no metadata reference.

IInstrumentMetadata.cs (excerpt)
namespace SabrTrader.Pipeline.Bars;

public interface IInstrumentMetadata
{
    double GetTickSize(string instrumentId);     // throws if unregistered: fail loud
    bool IsRegistered(string instrumentId);

    // Optional facets (default null / Contract): sizing, currency, classification.
    double? TryGetPointValue(string instrumentId) => null;
    QuantityUnit GetQuantityUnit(string instrumentId) => QuantityUnit.Contract;
    double? TryGetLotSize(string instrumentId) => null;
    double? TryGetQuantityStep(string instrumentId) => null;
    double? TryGetMinQuantity(string instrumentId) => null;
    string? TryGetQuoteCurrency(string instrumentId) => null;
    string? TryGetAdjustmentVersion(string instrumentId) => null;
    InstrumentCategory? TryGetCategory(string instrumentId) => null;
    // … plus pip size, contract size, leverage, size multiplier, inverse flag, significant figures
}
GetTickSize throws when the instrument is not registered, so a range bar is never built on a fabricated tick size. The SDK ships InMemoryInstrumentMetadata (a thread-safe registry with a fluent Register(...)) for tests. The optional facets describe sizing and classification (QuantityUnit: Contract / Share / Lot / Unit / Coin; InstrumentCategory: Forex / Equity / Crypto / Index / Commodity / Metal / Bond / Future) and feed order-entry surfaces. The GetQuantityModel(instrumentId) extension resolves unit, step and minimum into one QuantityModel. The full member list is in the bars reference.

Seeding the live handoff
When a chart switches from historical backfill to the live feed, a bar type that chains from the previous close has to continue the series, or it shows a phantom gap. A builder opts in by implementing a seeding interface; the handoff checks for it and applies the result the same way for built-in and plugin builders.

ISeedableBarBuilder.cs
namespace SabrTrader.Pipeline.Bars;

public readonly record struct BarSeedResult(bool Seeded, DateTime Cutoff, bool LastSeriesBarIsInProgress)
{
    public static readonly BarSeedResult NotSeeded = new(false, default, false);
}

// Single-bar seed: continue from the last historical bar.
public interface ISeedableBarBuilder
{
    BarSeedResult SeedFromLastBar(in Bar lastBar, DateTime nowUtc);
}

// Multi-bar seed: continue from the whole historical series.
// Checked first when a builder implements both.
public interface IHistorySeedableBarBuilder
{
    BarSeedResult SeedFromHistory(IBarSeries series, DateTime nowUtc);
}
Time bars re-open the last (possibly still-forming) period bar so live ticks extend it. They return LastSeriesBarIsInProgress = true.
Range, Renko, Median Renko and UniRenko chain: the next bar opens off the previous close (the built-in range bar one tick beyond it), so seeding opens a fresh in-progress bar from the last closed bar (LastSeriesBarIsInProgress = false). Cutoff tells the runtime which live ticks the seed already accounts for.
Volume and tick bars open the next bar at the next trade's own price, so they implement no seeding interface; starting fresh is already correct.
A rule that looks back several bars implements IHistorySeedableBarBuilder and rebuilds its window from the historical IBarSeries (barsAgo-indexed, [0] = newest).
The host's derived transforms use the derived seams (ISeedableDerivedBarBuilder for Heikin-Ashi, Kagi and Point & Figure, IHistorySeedableDerivedBarBuilder for Line Break), covered in A derived bar.

BarSeedResult fields. Seeded: whether a bar was seeded. Cutoff: live ticks at or before this instant are already in the seeded bar and are dropped. LastSeriesBarIsInProgress: true when the seed re-opened the last series bar (a time bar's partial period), false when it opened a new bar and the last series bar stays final (range chain).
Persistence of your parameters
Chart templates and workspaces persist the exact bar spec across restarts. The host writes each spec as a single JSON object with a string "type" discriminator plus the type's own fields; "instrumentId" is always present, and continuous-futures, trading-hours and exchange intent are written only when set.

Built-in discriminators include time, tick, range, volume, renko, medianrenko, unirenko, kagi, pointandfigure, linebreak, ha. Reading is case-insensitive, and missing optional keys take their defaults.

Plugin specs use discriminator "plugin" plus the CLR type full name and a params map of every [BarParameter] property:

plugin wire format
// new SampleFixedTickBarSpec { InstrumentId = "ES", Ticks = 50 }
// → {"type":"plugin",
//     "specType":"SabrTrader.Examples.SampleBarTypePlugin.SampleFixedTickBarSpec",
//     "instrumentId":"ES",
//     "params":{"Ticks":"50"}}
Restore requirements. The plugin must be loaded so the host can resolve specType back to your type. The type must honour WithInstrumentId, and the host reapplies each saved [BarParameter] value to a fresh instance, so a parameter that does not round-trip through its string form resets when the user reopens the chart. When the plugin is missing, the host reports the spec type by name instead of guessing a substitute.
Which venues offer your bar type
Which bar types a chart offers for an instrument depends on the venue. The host asks the active data provider, through IDataProvider.SupportsBarSpec, whether it can produce one representative spec of each bar type in the catalog, built-ins and plugin defaults alike, and the New Chart dialog, Data Series dialog and chart toolbar show only the types that pass.

A venue accepts a spec when it can fulfil it natively or by building it client-side from its historical ticks; both collapse into one boolean, so the pickers only need "offerable or not". The probe asks a type-level question: only the spec's runtime type (and parameter positivity, for venues that validate it) affects the verdict, so give your spec's parameterless defaults strictly positive values.

Time bars and re-aggregation. For TIME bars specifically, a venue describes which periods it serves natively via ITimeBarPeriodSupport (in SabrTrader.Pipeline.Runtime): SupportsNatively(period) and LargestNativeDivisor(period). The host's re-aggregating backfill fetches a divisor period and rolls it up. Tick, volume, range and Renko bars are built from ticks and do not consult it.
