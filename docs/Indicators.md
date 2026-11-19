Indicators
Overview & lifecycle
What an indicator is in SabrTrader, the contract you implement, and the order in which the framework calls your code. Every other indicator page builds on this one.

Learn from real code. The source of SabrTrader's standard indicators, from a plain moving average to multi-timeframe orderflow studies, is public on GitHub under the MIT license. Find an indicator close to what you want and read how it is built. Browse the indicator source on GitHub ↗
On this page
The indicator model
The IIndicator contract
Lifecycle hooks
The lifecycle in one picture
Opt-in capabilities
Where to go next
The indicator model
An indicator is a stateful object that reads a bar series and produces output: usually one or more plotted lines, sometimes a headless filter or an alert. You implement it by subclassing IndicatorBase, the SDK's implementation of the IIndicator contract (both in the SabrTrader.Pipeline.Indicators namespace). The base class provides empty virtual lifecycle hooks, the plot and line lists, child-indicator management and idempotent disposal, so you override only the methods you need.

An indicator instance is single-threaded with respect to its callbacks: the runtime never runs two lifecycle methods of the same instance at the same time. Store rolling state (running sums, buffers, the previous bar's value) in plain instance fields, with no locks and no volatile.

The IIndicator contract
You subclass IndicatorBase rather than implement IIndicator directly, but every page in this chapter maps back to a member of the contract. Members with a body are default interface implementations.

IIndicator.cs
public interface IIndicator : IDisposable
{
    string ContentKey { get; }                  // stable identity for dedup + persistence
    CalculationMode Calculate { get; }          // OnBarClose (default) / OnPriceChange / OnEachTick
    bool ProcessesMarketData { get; }           // opt into raw OnMarketData ticks
    bool ProcessesMarketDepth => false;         // opt into depth / MBO callbacks

    IReadOnlyList<IPlot> Plots { get; }          // renderable outputs
    IReadOnlyList<IIndicatorLine> Lines => Array.Empty<IIndicatorLine>();  // threshold lines
    bool ShowPriceMarkers => true;              // master gate for the plots' price-axis markers
    int Displacement => 0;                      // visual horizontal shift of the plots, in bars

    void OnInit(IIndicatorContext ctx);
    void OnDataLoaded(IIndicatorContext ctx);
    void OnBarUpdate(IIndicatorContext ctx);
    void OnMarketData(in Tick tick, IIndicatorContext ctx);
    void OnMarketDepth(in DepthUpdate update, IIndicatorContext ctx) { }
    void OnMarketByOrder(in MboEvent evt, IIndicatorContext ctx) { }
    void OnMarketByOrderLoss(long lostEvents, IIndicatorContext ctx) { }
    void OnOrderBookLifecycle(OrderBookLifecycleEvent evt, IIndicatorContext ctx) { }
    void OnDataConnectionChanged(DataConnectionStatus status, IIndicatorContext ctx) { }
    void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx);
    void OnDispose();

    ImmutableArray<IIndicator> Children { get; }  // parent-owned child tree
    void AttachChild(IIndicator child);
    void DetachChild(IIndicator child);
}
IndicatorBase implements all of these. It exposes two source-series properties: Input (an ISeries<double>, defaults to Bars(0).Close) and Bars (an IBarSeries, defaults to Bars(0)), plus the protected helpers AddPlot(...) and AddLine(...). Series, instrument metadata, logging and alerts come from the IIndicatorContext passed to each hook, which the base class also keeps in the protected Context property.

Lifecycle hooks
The framework drives your indicator through a fixed sequence of callbacks. Override the ones you need; the rest stay as the base class's no-ops. The order is what lets you compute history in one pass and then react to live bars one at a time.

Hook	When it fires	What you do in it
OnInit(ctx)	Before any other hook, after construction and after saved parameter values are applied. Runs again after OnReset.	Validate parameters and allocate state (buffers, rolling windows). Call base.OnInit(ctx) first so Input and Bars are defaulted before your code reads them.
OnDataLoaded(ctx)	Right after OnInit, with the full history already loaded into the primary series.	Compute every historical bar in a single pass. History arrives here in one call; it is not replayed bar by bar through OnBarUpdate.
OnBarUpdate(ctx)	For live bars only, at the cadence set by Calculate.	Compute the newest value.
OnMarketData(in tick, ctx)	Per live tick, when ProcessesMarketData is true.	Raw trade and quote handling for tape, delta and similar studies.
OnMarketDepth / OnMarketByOrder / OnMarketByOrderLoss / OnOrderBookLifecycle	Per depth or MBO event, when ProcessesMarketDepth is true.	DOM ladders, heatmaps. Default no-ops (details).
OnDataSeriesReady(i, ctx)	When a series added via ctx.AddDataSeries finishes backfilling.	Start reading the secondary series at index i.
OnDataConnectionChanged(status, ctx)	When the chart's live data connection drops, reconnects, comes back or fails. Needs no opt-in.	Show or clear a "disconnected" state, pause signals. Read ctx.DataConnection in OnInit for the starting status (example).
OnReset(ctx)	When a parameter edit or a recalculation request (IRecalculateNotifier) forces a full recompute. OnInit and OnDataLoaded follow.	Drop your accumulators. The base implementation clears every plot series and disposes the child indicators: call base.OnReset(ctx), then clear your own state. OnReset is a protected virtual on IndicatorBase, not part of IIndicator.
OnDispose()	Once, at teardown.	Release unmanaged resources. Child indicators are disposed for you.
Backfill and live. OnDataLoaded hands you the entire history at once so you can loop over it; OnBarUpdate then delivers each live bar. Gate side effects that must only happen live (sounds, broker calls, messages) on ctx.IsLive (or its inverse, ctx.IsHistorical). ctx.Alert already applies this gate for you.
The lifecycle in one picture
graph TD;
      A["new MyIndicator()"] --> B["OnInit(ctx)"];
      B --> C["OnDataLoaded(ctx): full history, one pass"];
      C --> D{"live event"};
      D -->|"bar closes / ticks (per Calculate)"| E["OnBarUpdate(ctx)"];
      D -->|"raw tick (if ProcessesMarketData)"| F["OnMarketData(in tick, ctx)"];
      D -->|"added series ready"| G["OnDataSeriesReady(i, ctx)"];
      E --> D; F --> D; G --> D;
      D -->|"chart closed / disposed"| H["OnDispose()"];
    
Opt-in capabilities
Everything else an indicator can do (custom painting, recolouring candles, a toolbar dropdown, multi-timeframe data, orderflow) is an opt-in: you implement an extra interface or set a property. Only indicators that implement IChartCustomRender take part in the custom-render pass, and only those with ProcessesMarketData = true are called on the tick path. Each opt-in has its own page in this chapter.

Where to go next
The rest of this chapter covers one subject per page. Reading them in order works well, and each page also stands on its own.

Page	Covers
Your first indicator	A complete SMA, line by line: the template for most line indicators.
Reading bars & series	Bars-ago indexing, OHLC access, price projections and older history.
Parameters	User-tunable inputs, editors, groups and the content key.
Plots & styles	Lines, histograms, areas, candles, per-bar colours and runtime restyling.
Panel placement	Overlay the price panel or open a subpanel.
Threshold lines	Reference levels like RSI 30/70.
Calculation & market data	Per-bar or per-tick updates, raw trades, depth, alerts and sessions.
Child indicators	Compose indicators (MACD from two EMAs).
Multi-timeframe	Add secondary series and read higher timeframes.
Orderflow	Per-price-level bid/ask volume for footprint tools.
Custom rendering	Paint anything on the chart, recolour candles, handle clicks.
Repaint requests	Drive smooth, coalesced repaints on your own schedule.
Chart toolbar menu	Add a toolbar dropdown and a right-click context menu.
Chart trader panel	Give the indicator its own tab on the chart trader panel.
