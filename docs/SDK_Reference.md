Reference
Reference overview
The public types a plugin compiles against, as short lookup pages derived from the contract source. They complement the narrative chapters.

How the SDK is packaged
Install one NuGet package, SabrTrader.Sdk. It is a meta-package with no code of its own; it depends on the four contract packages below, so their types are available to your project:

Bundled assembly	Covers
SabrTrader.Pipeline.Contracts	Indicators, bars & series, ticks, plots & rendering, order-flow & depth, drawing tools, the data-provider surface, market-analyzer columns, alerts, sessions, options levels, and the venue-agnostic options data model (chain reader & view, contracts, quotes, bounded snapshots, the OCC symbol helper).
SabrTrader.Pipeline.Venues.Contracts	The venue seam (plugin, descriptor, manifest, session, context, notices, catalog, symbol grammar, backfill composition) plus orders, brackets, fills, positions, accounts, ATM strategies, risk, commissions, client-side OCO, broker facts, multi-leg option orders and option strategies, the option chain SPI, and trade history and cash activity.
SabrTrader.Pipeline.Strategies.Contracts	The Strategy base class and its context, parameters, the strategy catalog, and backtest analytics and metrics.
SabrTrader.Pipeline.AddOns.Contracts	Add-ons: the plugin export and manifest, window kinds, windows and workspace state, the window service, and the host services an add-on reads accounts, history, live ticks and instruments through.
The meta-package also ships an MSBuild .targets file: set <DeploySabrTraderPlugin>true</DeploySabrTraderPlugin> in your plugin .csproj to copy the built DLL into Documents\SabrTrader\Plugins on every build (see Where to deploy). Every contract assembly targets net10.0, and so does your plugin.

Platform-internal namespaces in Pipeline.Contracts. A few public namespaces serve the platform's own components and are not meant for plugins to implement or call. They have no narrative chapters: SabrTrader.Pipeline.Perf (feed-pipeline timing: IPerfRecorder, PerfEvent, PerfStats, PerfStatsKey, PerfStatsSnapshot; PerfScope is the value IIndicatorRenderContext.BeginScope returns, which a render callback may use to time its own work), SabrTrader.Pipeline.Async (AsyncSerialRequestQueue and RequestPriority, a queue that keeps one venue request in flight at a time) and SabrTrader.Pipeline.MarketDepth.Hub (MboConnectionDiagnosticLog, which traces depth publication). A plugin reads recorded data through IRecordedDepthCatalog. Everything a plugin implements or consumes has a chapter and a reference page.
Types without a consumer. FlattenOutcome and FlattenResult in SabrTrader.Pipeline.Venues.Trading are not returned or accepted by any contract member; ITradingProvider.FlattenAsync returns Task<bool>. They are therefore not covered on the trading reference page.
Namespaces. Every contract namespace sits under SabrTrader.Pipeline. The venue seam and its trading, chain and trade-history types live under SabrTrader.Pipeline.Venues. A type's namespace does not always match the folder its source file sits in: rendering and viewport types, for example, declare SabrTrader.Pipeline.Charts.Rendering and SabrTrader.Pipeline.Charts.Viewport. Use the namespace shown on each reference page.
Namespace map
Namespace	Assembly	Reference page
SabrTrader.Pipeline.Bars · .Series · .Time	Pipeline.Contracts	Bars & series
SabrTrader.Pipeline.Ticks	Pipeline.Contracts	Ticks
SabrTrader.Pipeline.Indicators · .Indicators.Search · .Indicators.Statistics	Pipeline.Contracts	Indicators
SabrTrader.Pipeline.Indicators.ContractRollover	Pipeline.Contracts	Core concepts: ambient providers
SabrTrader.Pipeline.Plots · .Indicators.Builtin · .Charts.Rendering · .Charts.Viewport · .Panels	Pipeline.Contracts	Plots & rendering
SabrTrader.Pipeline.OrderFlow · .MarketDepth · .Storage (recorded data)	Pipeline.Contracts	Orderflow & depth
SabrTrader.Pipeline.Drawing · .Drawing.Tools	Pipeline.Contracts	Drawing
SabrTrader.Pipeline.Providers · .Runtime · .Services · .Storage (storage seam)	Pipeline.Contracts	Providers
SabrTrader.Pipeline.MarketAnalyzer	Pipeline.Contracts	Market Analyzer
SabrTrader.Pipeline.Alerts · .Indicators.TradingHours · .Indicators.Sessions	Pipeline.Contracts	Alerts & sessions
SabrTrader.Pipeline.Options	Pipeline.Contracts	Options
SabrTrader.Pipeline.Levels	Pipeline.Contracts	Options levels
SabrTrader.Pipeline.Decisions	Pipeline.Contracts	Strategies: decision models
SabrTrader.Pipeline.Perf · .Replay · .Async · .MarketDepth.Hub	Pipeline.Contracts	Platform-internal (see the note above)
SabrTrader.Pipeline.Venues · .Venues.Options · .Venues.TradeHistory	Venues.Contracts	Venue seam
SabrTrader.Pipeline.Venues.Trading · .Atm · .Brackets · .Commissions · .Facts · .Oco · .Risk · .Sim	Venues.Contracts	Trading
SabrTrader.Pipeline.Strategies · .Strategies.Catalog · SabrTrader.Pipeline.Analytics · .Analytics.Metrics	Strategies.Contracts	Strategies
SabrTrader.Pipeline.AddOns	AddOns.Contracts	Add-ons
Reference pages
Each page documents one area of the SDK. Pick the one that matches what you are building, or page through them in order.

Bars & series
The OHLCV Bar (UTC identity plus chart-display Start/End), bar-specification records, builders, instrument metadata, and the barsAgo series.

Ticks
The raw Tick record struct and its TickFlags bitmask.

Indicators
The indicator base class, runtime context, lifecycle, attributes, and optional capability interfaces.

Plots & rendering
Plot / line models, ChartColor, the immediate-mode renderer, candle geometry and the viewport.

Orderflow & depth
Footprint bars and stats plus L2/L3 depth feeds, the order book and depth/MBO events.

Drawing
The drawing-tool surface, anchors, render context, geometry helpers, metadata attributes and Fib levels.

Providers
The data-provider surface, capabilities, backfill flavours and tick sources.

Trading
Orders, brackets, fills, positions, accounts and ATM strategies.

Strategies
The Strategy base class, parameters, the strategy catalog, data feeds, and backtest results and metrics.

Market Analyzer
Column definitions, value production and registration for the market-analyzer grid.

Alerts & sessions
Alert channels and severities plus trading-hours / session contracts.

Options levels
Gamma / options-levels feeds, snapshots, strike profiles, expiration selection and the ambient registry.

Venue seam
Plugin, descriptor, manifest, session and context; the depth ingest plane; the venue-neutral trading components; the chain SPI; trade history and cash activity.

Options
The venue-agnostic chain reader and view, contracts, quotes, bounded snapshots, and option strategies.

Add-ons
The add-on export and manifest, window kinds, windows and their views, workspace state and the add-on host services.

Signatures throughout the reference are quoted from the contract source; the authoritative surface is the SDK assemblies you reference. A public-API snapshot test guards every contract assembly against unintended changes, so the API of a given SDK version is fixed.

