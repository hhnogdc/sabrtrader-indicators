Core concepts
Core concepts
The model behind every chapter: how plugins load, what you implement, how data is indexed, and the patterns that recur across indicators, strategies, bar types, venues and the other plugin kinds.

On this page
The plugin model
The contract assemblies
Base class + capability interfaces
Series & bars-ago indexing
Ambient providers
Lifecycle phases
Threading expectations
Discovery & deployment
The plugin model
A plugin is one .NET assembly compiled against the SDK's contracts. You subclass a base class or implement an interface, build a net10.0 DLL, and copy it into Documents\SabrTrader\Plugins. The platform loads each DLL in that folder, inspects its public types, and registers every type that implements a known contract in the matching catalog: the indicator picker, the strategy list, the bar type picker, the drawing toolbar, the Market Analyzer column picker, the Connections dialog, the Control Center menu, and so on.

There is no registration call and no configuration file. Discovery is by type: the platform finds your plugin through the contracts it implements. Venue plugins and add-ons describe themselves with a manifest object in code (VenueManifest, AddOnManifest); the other kinds use attributes. The platform creates your type before it applies any settings, so a discoverable type needs a public constructor it can call without arguments. Bar builders are the exception: their constructor takes the bar specification they build.

You compile against contracts. The SDK assemblies hold interfaces, base classes, attributes, enums, data records and a small set of helpers. The implementations live in the platform and are supplied at load time, so they can change without a change to your plugin. The platform always binds your plugin to its own copy of the contract assemblies, which is why you deploy your plugin DLL without them.
The contract assemblies
SabrTrader.Sdk brings in four assemblies. Each declares several namespaces, and a type's namespace does not always match the folder its source file sits in, so use the namespace given on each chapter and reference page.

Assembly	What it holds
SabrTrader.Pipeline.Contracts	The bulk of the surface: indicators (SabrTrader.Pipeline.Indicators), plots (SabrTrader.Pipeline.Plots), series (SabrTrader.Pipeline.Series), bars (SabrTrader.Pipeline.Bars), market depth (SabrTrader.Pipeline.MarketDepth), drawing tools (SabrTrader.Pipeline.Drawing), chart and add-on panel elements (SabrTrader.Pipeline.Panels), Market Analyzer columns (SabrTrader.Pipeline.MarketAnalyzer), alerts (SabrTrader.Pipeline.Alerts), data providers (SabrTrader.Pipeline.Providers and SabrTrader.Pipeline.Runtime) and the venue-agnostic options data model (SabrTrader.Pipeline.Options).
SabrTrader.Pipeline.Venues.Contracts	The venue seam (SabrTrader.Pipeline.Venues): IVenuePlugin, IVenueDescriptor, VenueManifest, IVenueSession and VenueSessionContext, which a market-data feed or broker plugin implements or consumes. Also orders, positions, accounts, risk, commissions, ATM brackets, OCO and multi-leg option order types (SabrTrader.Pipeline.Venues.Trading and its sub-namespaces), the option chain SPI (SabrTrader.Pipeline.Venues.Options) and the venue trade-history sources (SabrTrader.Pipeline.Venues.TradeHistory).
SabrTrader.Pipeline.Strategies.Contracts	The Strategy base class and its IStrategyContext runtime context (SabrTrader.Pipeline.Strategies), the strategy catalog and the [StrategyMetadata] attribute (SabrTrader.Pipeline.Strategies.Catalog), and backtest analytics (SabrTrader.Pipeline.Analytics and SabrTrader.Pipeline.Analytics.Metrics).
SabrTrader.Pipeline.AddOns.Contracts	Add-ons (SabrTrader.Pipeline.AddOns): IAddOn, AddOnManifest, window kinds, IAddOnWindow, workspace state and IAddOnHost, the services an add-on reads the platform through.
Base class or interface, plus optional capability interfaces
This pattern recurs in every chapter. You implement one primary contract to be a plugin of a given kind: subclass IndicatorBase, Strategy or DrawingToolBase, or implement IVenuePlugin or IAddOn. You then opt into extra features by also implementing small capability interfaces. The platform checks for each one with a type test (is IChartCustomRender) and enables the feature when it is present.

The primary contract stays small, and a plugin carries only the features it uses. Some examples from the contracts:

Primary contract	Optional capability interface	What it adds
IndicatorBase	IChartCustomRender	Paint custom chart decorations beyond standard plot series (footprint cells, profiles, price lines).
IndicatorBase	IIndicatorPanelHint	Declare a default panel placement (price overlay or its own subpanel).
IndicatorBase	IIndicatorToolbarMenu	Contribute items to the chart toolbar menu.
IndicatorBase	IRepaintNotifier	Request a coalesced repaint when visual state changes off the bar cadence.
IAddOnWindow	IAddOnWindowState<TState>	Save and restore the window's state with the user's workspace.
ITradingProvider	IPositionRefresh	Fetch one position from the venue on demand.
IDataProvider	IContinuousFuturesSource	Serve continuous-futures history: the venue's own continuous series, or its dated contracts for the platform to stitch.
The shape in code: one indicator that is also a custom renderer. The lifecycle methods come from IndicatorBase; OnCustomRender and Layer come from the capability interface.

MyVolumeProfile.cs
using SabrTrader.Pipeline.Indicators;

// Primary contract: IndicatorBase makes it an indicator.
// Capability interface: IChartCustomRender opts into the chart paint pass.
public sealed class MyVolumeProfile : IndicatorBase, IChartCustomRender
{
    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        // ... accumulate per-price volume from ctx.Bars(0) ...
    }

    // Only called because the host saw `is IChartCustomRender`.
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        // ... draw the histogram into ctx.PlotArea ...
    }

    // Background decoration: bars draw on top. The default is AboveBars.
    public CustomRenderLayer Layer => CustomRenderLayer.BelowBars;
}
Reading a chapter. Find the primary contract for the kind you are building, then look through its optional interfaces and implement only the ones you need.
Series & bars-ago indexing
Bars and indicator outputs are exposed as series with bars-ago indexing: [0] is the most recent value, [1] the one before, and so on. For a bar series, [0] is the current bar (still forming when live, the most recent closed bar in history).

C#
var bars = ctx.Bars(0);          // the primary IBarSeries
Bar latest = bars[0];            // most recent bar
double prevClose = bars[1].Close; // the bar before it
int n = bars.Count;              // total bars ever observed (monotonic)
Two interfaces carry this. IBarSeries is the read-only OHLCV series with bars-ago indexing, a Count (total bars ever observed) and a Capacity (the ring size, the largest lookback it can serve). It also exposes price views such as Open, High, Low, Close, Median, Typical, Weighted, OHLC4 and Volume, each an ISeries<double>. ISeries<T> is the generic read-only series used for indicator inputs and plot outputs.

For your own output, the SDK provides ChunkedSeries<T> in SabrTrader.Pipeline.Series: a growable series with one writer and any number of readers. You Append one value per bar, rewrite the newest value with UpdateLast (or an older one with Set), and hand the series to a plot.

Warmup is NaN, not zero. In double series, bars that have no value yet hold double.NaN, so warmup bars render as gaps instead of a line at zero. Use TryGet to read an index that may not exist yet, and check for NaN wherever "no value" and "zero" must be told apart.
Ambient providers
Some plugins need a platform service at a point where no context has been handed to them, for example in a constructor, because the platform creates your type before any context exists. The SDK covers this with ambient providers: static accessors in the contracts that the platform sets at startup and your plugin reads when it needs them.

The contract assemblies include these ambient providers:

IndicatorResolverAmbient (SabrTrader.Pipeline.Indicators): its Resolver is the platform's indicator catalog (built-in and plugin indicators), so an indicator that hosts other indicators can list and create them by type.
TradingHoursAmbient (SabrTrader.Pipeline.Indicators.TradingHours): its Resolver and Schedule resolve trading-hours templates, so session-aware logic gets the right session windows for an instrument.
Each is a static class with a Resolver property that the platform sets through SetResolver, plus a Use(...) method that installs an implementation for the lifetime of the returned IDisposable, which is how unit tests substitute their own. A plugin reads these properties; setting them is for the platform and for tests.

Prefer the context. When a lifecycle context is available (IIndicatorContext, IStrategyContext), get shared data from it first: both expose TradingHours and TickSize, IIndicatorContext.Indicators is the indicator resolver, and IIndicatorContext.GetContractRolloversUtc returns futures contract-rollover dates. Use an ambient provider only where no context is in scope.
Lifecycle phases
Every plugin kind that processes market data goes through the same three phases, with method names that differ per kind.

Init. The platform has created your object and applied settings; validate parameters and allocate buffers. (Indicators: OnInit. Strategies: OnInitialize declares parameters and indicators, then OnStart runs once parameters hold their final values.)
History / data loaded. Backfill has arrived; compute over the whole history in one pass, oldest bar first. (Indicators: OnDataLoaded.)
Live updates. New data flows in; update incrementally so live values exactly match what the history pass produced. (Indicators: OnBarUpdate, once per closed bar with the default CalculationMode.OnBarClose, plus the optional OnMarketData per tick and OnMarketDepth / OnMarketByOrder for depth. Strategies: OnBar, OnFill, OnOrderUpdate, OnPositionUpdate.)
The flow below is the indicator lifecycle; strategies, bar builders and data providers follow the same init, history, live sequence.

Indicators and strategies also see the state of the live data connection behind them: read DataConnection (a DataConnectionStatus) at any time, and override OnDataConnectionChanged to react when the feed drops, reconnects or fails. Where there is no live connection, such as in a backtest, the status is NotApplicable. See Data connection.

flowchart TD
  A[Platform creates your type] --> B[Apply settings / parameters]
  B --> C[Init: validate & allocate]
  C --> D[History: compute over backfill, oldest first]
  D --> E{Live data}
  E -->|bar closes| F[Per-bar update]
  E -->|tick / depth| G[Per-tick / depth update]
  F --> E
  G --> E
  E -->|teardown| H[Dispose / stop]
History and live must agree. The value computed for a bar during the history pass and the value computed for the same bar live must be identical. Share the calculation between the two paths so they cannot drift apart.
Threading expectations
Data callbacks for one instance (OnBarUpdate, OnMarketData, OnBar, OnFill and the rest) are delivered one at a time, never re-entrantly, so state touched only by those callbacks needs no locking. Rendering is different: custom render callbacks run on the chart's UI thread, up to 60 times per second, while data callbacks run on the platform's calculation threads. Series follow a single-writer, multi-reader model for this reason: your data callbacks write the output, and the chart reads it from the render thread. Keep per-tick and render callbacks fast and free of allocations, and move slow or blocking work out of the callback. Where a contract has its own threading or delivery rules, its chapter and the reference state them.

Discovery & deployment at a glance
Build a net10.0 DLL and copy it into Documents\SabrTrader\Plugins (the SDK's MSBuild target can do this on every build). The platform loads the DLLs in that folder at startup and watches the folder while it runs, so a new or rebuilt DLL is loaded without a restart. It finds your types by the contracts they implement and registers them. The Building & deploying chapter covers the folder, reloading, dependencies and troubleshooting.

