Reference
Indicators
The contract surface for building indicators: the IIndicator lifecycle, the IIndicatorContext runtime view, the IndicatorBase base class, the declaration attributes, the cadence enums, the optional render / interaction capability interfaces, and the chart trader panel controls. Namespaces: SabrTrader.Pipeline.Indicators and its .Search and .Statistics sub-namespaces, plus SabrTrader.Pipeline.Panels. Trading hours and sessions (.TradingHours, .Sessions) are in the Alerts & sessions reference; contract rollovers (.ContractRollover) and the series types are in Bars & series; PlotColors (.Builtin) is in Plots & rendering.

On this page
IIndicator · IIndicatorContext · Data connection · IndicatorBase
CalculationMode · CalculationCadence
Attributes · Metadata & identity · Input sources
Capability interfaces · Render context & menus · Helpers
Resolver & ambients · Alerts · Chart trader panels
IIndicator INTERFACE
The lifecycle contract every indicator implements (it extends IDisposable). The runtime calls the hooks; indicators communicate through child indicators and the shared bar/series storage, and never call each other's hooks. Callbacks are single-threaded per instance: no two hooks fire in parallel for the same indicator.

Lifecycle ordering: OnInit once → OnDataLoaded once (with all historical bars already in storage) → OnBarUpdate per closed/intra-bar event at the Calculate cadence → OnMarketData per live tick (with ProcessesMarketData) → OnDataSeriesReady when an added series finishes backfill → OnDispose once. Live hooks fire only after backfill.

Properties
Member	Description
string ContentKey { get; }	Stable identity across runs, derived from (Type, ParameterValues, InputSeriesSpecs). Two requests with the same key resolve to the same instance (diamond-dependency dedup).
CalculationMode Calculate { get; }	Cadence the runtime fires OnBarUpdate on the primary series. Must be stable for the indicator's lifetime. Default OnBarClose.
bool ProcessesMarketData { get; }	Opt into raw OnMarketData tick delivery. Default false, which keeps per-tick dispatch cost at zero for the indicator. Independent of Calculate.
bool ProcessesMarketDepth { get; }	Opt into the order-book callbacks: OnMarketDepth, OnMarketByOrder, OnMarketByOrderLoss and OnOrderBookLifecycle. Works on every host (chart, strategy live and backtest, Market Analyzer) and at any depth of the indicator tree; the framework claims the host's book for you. Default-interface member, default false.
IReadOnlyList<IPlot> Plots { get; }	Renderable outputs: one IPlot per line (SMA = 1, MACD = 3). Stable list reference; allocate plots in the ctor/OnInit and never resize. Plot properties are mutable at runtime.
IReadOnlyList<IIndicatorLine> Lines { get; }	Horizontal threshold lines (RSI 30/70, ADX 20/40). Default-interface member returns empty.
bool ShowPriceMarkers { get; }	Master toggle overriding every plot's ShowPriceMarker. Default true (per-plot setting wins).
int Displacement { get; }	Purely visual horizontal shift of plots in bars (positive = right/future). The computed value series is never altered. Default 0.
ImmutableArray<IIndicator> Children { get; }	Live snapshot of attached child indicators in registration order. Lock-free (atomic swap).
Lifecycle & child-tree methods
Signature	Description
void OnInit(IIndicatorContext ctx)	Once after construction. Declare secondary series, instantiate known children, read parameters. ctx.Bars(0) is available.
void OnDataLoaded(IIndicatorContext ctx)	Once after all historical bars are loaded. Compute historical state in a single (typically vectorised) pass.
void OnBarUpdate(IIndicatorContext ctx)	A live bar closed/updated on one of the indicator's series; ctx.BarsInProgress identifies which.
void OnMarketData(in Tick tick, IIndicatorContext ctx)	Per live tick, only when ProcessesMarketData is true.
void OnMarketDepth(in DepthUpdate update, IIndicatorContext ctx)	Per aggregated L2 depth event (ProcessesMarketDepth). The book is applied before this fires. Default no-op.
void OnMarketByOrder(in MboEvent evt, IIndicatorContext ctx)	One Market-By-Order event (ProcessesMarketDepth). The book is applied before this fires. Sampled live: the host hands over the newest queued event per worker pass and announces the rest through OnMarketByOrderLoss. Default no-op. See delivery contract.
void OnMarketByOrderLoss(long lostEvents, IIndicatorContext ctx)	MBO events addressed to this indicator were not delivered; fires before the first event that follows the gap. Exact for the events the host's latest-per-pass delivery skipped; ring overflow is added to the same count. The shared book is unaffected. Default no-op.
void OnOrderBookLifecycle(OrderBookLifecycleEvent evt, IIndicatorContext ctx)	The shared book was reset, cleared for a venue re-image, finished that re-image, or changed its OrderBookDepthMode. The book already reflects the transition. Never concurrent with the other order-book callbacks. Default no-op.
void OnDataConnectionChanged(DataConnectionStatus status, IIndicatorContext ctx)	The live market-data connection feeding the chart changed state: it dropped, is reconnecting, came back, or a connect attempt failed. status equals ctx.DataConnection for the duration of the call, and ctx.IsLive is true, so an alert raised here fires. Rapid transitions are coalesced; you always end on the current status. Needs no opt-in. Default no-op. See Data connection.
void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx)	An AddDataSeries series finished backfilling; ctx.Bars(seriesIndex) now returns real bars.
void OnDispose()	Once on teardown. Release non-managed resources; children are disposed automatically.
void AttachChild(IIndicator child)	Registers a child; thread-safe. The child's own constructor calls it (through IndicatorBase), so indicator code has no reason to.
void DetachChild(IIndicator child)	Detach without disposing (call child.Dispose() instead, which routes here). No-op if not attached.
IStrategy: public interface IStrategy : IIndicator marks a strategy, so consumers can type-check "indicator vs strategy". See the Strategies reference.
IIndicatorContext INTERFACE
The runtime view an indicator has of its environment, passed to every lifecycle hook. Single-threaded with respect to the indicator. Do not cache it across threads or persist it past a callback. Many members are default-interface methods, so a test stub or third-party host implements only the core members.

Series & per-callback metadata
Member	Description
IBarSeries Bars(int seriesIndex)	Bars for a series; 0 = primary. Added series live at the indices returned by AddDataSeries.
int SeriesCount { get; }	Number of subscribed series (primary + added).
int CurrentBar { get; }	Last-bar index on the firing series (Bars(BarsInProgress).Count - 1); -1 in warmup.
CurrentBarsAccessor CurrentBars { get; }	Per-series last-bar indexer (ctx.CurrentBars[1]) for MTF indicators. Zero-allocation struct.
int BarsInProgress { get; }	Which series this callback is firing for (0 = primary; always 0 on OnDataLoaded).
bool IsFirstTickOfBar { get; }	True when the current tick opened a new bar on BarsInProgress.
bool IsBarClosed { get; }	True for a closed-bar OnBarUpdate; false intra-bar. Default true.
long SequenceNumber { get; }	Monotonic event sequence for deterministic logging/replay.
bool IsHistorical { get; }	True during backfill/catch-up (!IsLive). Gate alerts/broker calls on this.
bool IsLive { get; }	True during realtime dispatch. Default false (conservative).
DataConnectionStatus DataConnection { get; }	State of the live market-data connection feeding this chart. Unlike IsLive, which describes the current callback, this describes the connection and can be read from any hook. Default NotApplicable. See Data connection.
bool IsDataConnected { get; }	True when DataConnection is Connected. False while disconnected, connecting, lost or reconnecting, and also on hosts with no live connection; check DataConnection for NotApplicable when that difference matters.
Resources, orderflow & instrument info
Member	Description
Task<int> AddDataSeries(BarSpecification spec)	Subscribe an additional series, loaded as bars: its historical order flow is empty. Idempotent for an already-subscribed spec; OnDataSeriesReady fires when bars are available.
Task<int> AddDataSeries(BarSpecification spec, OrderFlowDetail orderFlow)	The same, for an indicator that reads OrderFlow of the series it adds. OrderFlowDetail.Totals: per-bar totals only, loaded from the venue's own bars where they carry the buy/sell split. OrderFlowDetail.PriceLevels: the full footprint. The host honours it on a chart that loads order flow, which [RequiresOrderFlow] makes it do. A request never shares a series that was loaded with less, so read the index this call returns.
Task<IReadOnlyList<Bar>> LoadHistoricalBars(BarSpecification spec, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)	One-shot read of closed bars in [fromUtc, toUtcExclusive), independent of the chart's own history: it subscribes nothing and creates no series slot. Resolves spec like AddDataSeries (same instrument inherits the primary's trading hours and continuous root). Completes on another thread: fold the result in through IRecalculateNotifier, never from the continuation. Default: faulted with NotSupportedException.
void RemoveDataSeries(int seriesIndex)	Drop an added series. Index 0 throws InvalidOperationException.
IIndicatorContext CreateChildContext(IBarSeries childPrimary, string childIdentity)	Build a per-child context (called by AttachChild). Default returns this.
void BindOwner(IIndicator owner)	Host plumbing: tells the context which indicator it serves. IndicatorBase calls it.
IDecisionClient Decisions { get; }	The decision-model client (SabrTrader.Pipeline.Decisions). DecisionClient.None until a chart host wires one. IndicatorBase exposes it as the protected Decisions property and delivers answers to OnDecision. See Decision models.
IAiClient Ai { get; }	The AI client (SabrTrader.Pipeline.AiRequests). AiClient.None where the host does not ask the AI. IndicatorBase exposes it as the protected Ai property. See AI requests.
INewsClient News { get; }	The news client (SabrTrader.Pipeline.News). NewsClient.None where the host has no news: backtests, the Strategy Analyzer and the Market Analyzer. On a live chart it also works during the historical pass: you can request past headlines, and live headlines arrive once the chart is live. IndicatorBase exposes it as the protected News property. See News and Reference › News.
IIndicatorResolver? Indicators { get; }	Host catalog of instantiable indicator types (built-ins + plugins), or null. Tolerate null.
ISeries<OrderFlowBar> OrderFlow(int seriesIndex)	Per-bar orderflow. Only indicators marked [RequiresOrderFlow] may call it (else throws).
ISeries<double> OpenInterest(int seriesIndex)	Per-bar OI scalar series (NaN where unreported). Default empty series; read defensively.
IOrderBook? OrderBook { get; }	The host's live depth book (chart, strategy run or Market Analyzer row), or null. Applied before depth/MBO callbacks. For price levels read a leased view instead (LeaseOrderBook). Default null.
void ReadOrderBook(Action<IOrderBook> reader)	Invoke reader against the live depth book under the host's read guard (null-safe no-op when no book). Prefer this over caching OrderBook across a callback.
bool TryReadOrderBook(Action<IOrderBook> reader)	The same read, but it tells you whether it happened: false means there was no book and reader was never invoked. Use it wherever "never ran" and "ran against an empty book" mean different things (seeding state from the book, reporting a fault); ReadOrderBook treats a cold start with no depth feed and a broken host the same way.
OrderBookLease? LeaseOrderBook(in OrderBookNeed need)	Claim the host's order book. OrderBookNeed.View(levels) also gives you the book's published, immutable view (LatestView(), CurrentView, SubscribeViews): the lock-free way to read price levels. While a lease is open the host subscribes the venue's depth and market-by-order streams. Take it in OnInit, dispose it in OnDispose. Null on a host without a book. See OrderBookLease.
IRecordedDepthCatalog? RecordedDepth { get; }	Read access to the L2 / MBO tapes the platform's recorder wrote, with their reset, re-image and gap markers, or null when this host records none. Unlike the sampled callbacks, it holds every event. See IRecordedDepthCatalog.
double? TickSize { get; }	Minimum price increment, or null. Default null.
double? PointValue { get; }	Currency value of one full point, or null. Default null.
double? QuantityStep { get; }	Smallest tradable quantity increment for the chart's instrument (1 contract, 1 share, 0.0001 BTC), or null when the host could not resolve one. Default null.
IOptionChainReader? OptionChains { get; }	The read-only options data surface (SabrTrader.Pipeline.Options): chains, per-contract quotes and greeks, routed to whichever connected venue serves the underlying. Null when the host serves no options data. Availability changes as venues connect, so keep the reader and watch IsAvailable / AvailabilityChanged. Default reads OptionChainReaderAmbient.Reader. See the Options reference.
TimeZoneInfo? DisplayTimeZone { get; }	The chart's display time zone. Bar.StartUtc / EndUtc stay true venue UTC; the chart timeline is Bar.Start / End. Convert an external UTC instant with ChartDisplayTime.FromUtc(utc, DisplayTimeZone). Default null (treat as already on the bar timeline).
Trading hours, rollovers & diagnostics
Member	Description
TradingHoursTemplate? TradingHours { get; }	Session/holiday calendar for the instrument, or null (treat as 24/7).
ISessionIterator? CreateSessionIterator(TradingHoursTemplate? template = null)	A fresh stateful session iterator over template (the instrument's own when null). Default null.
bool IsFirstBarOfSession { get; }	True when the current OnBarUpdate bar opens a new session. Default false.
IReadOnlyList<DateTime> GetContractRolloversUtc(DateTime fromUtc, DateTime toUtc)	Futures rollover instants as true venue UTC (same clock as Bar.StartUtc). Default empty.
void Print(string message)	Dev trace to the host log window (like NinjaTrader's Print()).
void LogWarning(string message)	Non-fatal anomaly; indicator keeps running.
void LogError(string message, Exception? exception = null)	Fatal-for-this-callback condition; runtime keeps dispatching.
void Alert(string message)	Raise a user-facing alert (live only). Message doubles as the rearm id.
void Alert(string id, string message, string? soundPath = null, AlertSeverity severity = Info, double rearmSeconds = 0)	Full-control alert. The same id fires at most once per rearmSeconds. Default no-op.
Data connection
An indicator can tell whether the chart it runs on has a live data connection, and react when that changes. Read ctx.DataConnection (or the shortcut ctx.IsDataConnected) from any hook, and override OnDataConnectionChanged to hear about transitions. The status follows the chart's data venue as a whole, so it keeps tracking the right connection when the platform replaces a venue session after a reconnect. For a practical walk-through, see Showing the connection state.

DataConnectionStatus.cs
namespace SabrTrader.Pipeline.Providers;

public enum DataConnectionStatus
{
    NotApplicable  = 0,
    Disconnected   = 1,
    Connecting     = 2,
    Connected      = 3,
    ConnectionLost = 4,
    Reconnecting   = 5,
    Failed         = 6,
}
Value	Meaning
NotApplicable	This host has no live connection to report. Treat it as "the question does not apply" rather than as a disconnect.
Disconnected	No connection and none in progress: the venue is idle or not configured.
Connecting	A first connect attempt is in progress.
Connected	Connected. Live data can flow.
ConnectionLost	Was connected, and the link dropped unexpectedly. No reconnect is running yet.
Reconnecting	Was connected, the link dropped, and a reconnect attempt is in progress.
Failed	The most recent connect attempt failed and the venue has not connected since. A rejected login stays here until the user fixes the connection.
Where you get NotApplicable
Only an indicator on a chart follows a data venue. Everywhere else the context reports NotApplicable and OnDataConnectionChanged never fires: backtests and optimizer passes, historical-only analysis, indicators hosted inside a strategy (the strategy reads its own DataConnection), SuperDOM and Market Analyzer column hosts, test rigs, and any third-party IIndicatorContext that keeps the default. Treat NotApplicable like "connected" for display purposes: a "disconnected" badge in a backtest would be wrong.

Delivery rules
OnDataConnectionChanged runs on the same serialised path as the other callbacks, never concurrently with them, with ctx.IsLive true, so ctx.Alert(...) fires from it.
Inside the callback, ctx.DataConnection already returns the new status.
Rapid transitions are coalesced: you always end on the current status, but you may not see every intermediate one (a quick drop and reconnect can arrive as just Connected).
It fires on transitions only. Read ctx.DataConnection in OnInit for the starting status; the usual pattern is one field set in both places.
No ticks arrive while the connection is down, so an indicator that draws the status implements IRepaintNotifier and raises RepaintRequested from the callback.
No opt-in attribute is needed. A child indicator hosted by a composite reads DataConnection from its own context; the callback goes to the indicators the host dispatches to.
IndicatorBase BASE CLASS
The class plugin authors derive from. It implements IIndicator, IIndicatorHost (host-side plumbing, see below), IOrderFlowResolutionAware and IInputSourceAware, and provides empty virtual lifecycle hooks (override only what you need), lazy ContentKey, idempotent Dispose, the parent-owned child tree, and the plot/line registration helpers.

SimpleAverage.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorDescription("Simple moving average of the input series.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
[IndicatorPlot("SMA", IsOverlay = true, Kind = PlotKind.Overlay, Unit = PlotValueUnit.Price)]
public sealed class SimpleAverage : IndicatorBase
{
    private readonly ChunkedSeries<double> _sma = new();

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = 500, Step = 1)]
    public int Period { get; set; } = 14;

    public SimpleAverage(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot("SMA", _sma, PlotStyle.Line, new ChartColor(33, 150, 243), 1.5));
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        for (int barsAgo = Input.Count - 1; barsAgo >= 0; barsAgo--)
            _sma.Append(Average(barsAgo));
    }

    public override void OnBarUpdate(IIndicatorContext ctx) => _sma.Append(Average(0));

    private double Average(int barsAgo)
    {
        int n = Math.Min(Period, Input.Count - barsAgo);
        double sum = 0;
        for (int i = 0; i < n; i++) sum += Input[barsAgo + i];
        return sum / n;
    }
}
Construction
Every subclass exposes one ctor of the shape Xxx(IIndicator? parent = null, ISeries<double>? input = null, /* params */) : base(parent, input). The base ctor stores Input/Bars and, when parent is non-null, registers as a child. The child's own OnInit/OnDataLoaded are deferred to the first dispatch event so the derived ctor body sets Period, plots, etc. before lifecycle math runs.

protected IndicatorBase(IIndicator? parent = null, ISeries<double>? input = null, IBarSeries? bars = null);
Public & protected members
Member	Description
ISeries<double> Input { get; set; }	Scalar source for SMA/EMA/RSI-style indicators; null defaults to primary Bars(0).Close at init.
IBarSeries Bars { get; set; }	Bar source for OHLCV indicators (ATR/ADX/VWAP); null defaults to primary Bars(0).
InputSourceDescriptor InputSource { get; set; }	The universal "Input series" parameter backing store (surfaces in the editor only for Scalar input kind).
OrderFlowResolution OrderFlowResolution { get; set; }	The universal orderflow "Resolution" backing store (surfaces only for [RequiresOrderFlow] indicators). Default Auto.
bool ShowPriceMarkers { get; set; }	Master price-marker toggle (attributed parameter, group "Display").
int Displacement { get; set; }	Visual bar shift (attributed parameter, ContentKeyDefault = 0).
IReadOnlyList<IPlot> Plots { get; } / Lines { get; }	The registered plots and threshold lines.
IIndicatorContext Context { get; }	Protected. The most recent context the runtime supplied. Do not cache across threads.
bool TryGetPlot(string name, out IPlot plot)	Find a registered plot by its exact name. False when this indicator has no plot with that name.
IDecisionClient Decisions { get; }	Protected. The decision-model client from Context.Decisions; DecisionClient.None until a chart host wires one. See Decision models.
IAiClient Ai { get; }	Protected. The AI client from Context.Ai: send the AI instructions with bars, plots, your own data or the chart picture, and get the answer back as your own type. AiClient.None where the host does not ask the AI. See AI requests.
INewsClient News { get; }	Protected. The news client from Context.News: live and past headlines and story text, with handlers on the indicator's thread. NewsClient.None where the host has no news. See News and Reference › News.
const int MaxChildDepth = 16	Maximum parent→child chain depth (cycle + depth guarded by AttachChild).
protected void AddPlot(IPlot plot)	Register a renderable output (append-only; earlier entries render behind later ones).
protected void AddLine(IIndicatorLine line)	Register a horizontal threshold line.
protected void AddLine(string name, double value, ChartColor color, double lineWidthPx = 1.0, PlotStyle style = Line, bool participatesInAutoScale = true)	Convenience overload constructing the default IndicatorLine.
protected void AddLine(string name, double value, ChartColor color, double lineWidthPx, PlotStyle style, bool participatesInAutoScale, bool isParameterDriven)	The same, for a line whose level comes from one of the indicator's parameters (IIndicatorLine.IsParameterDriven). Every argument is required.
ImmutableArray<IIndicator> Children { get; }	The attached child indicators, in registration order.
void AttachChild(IIndicator child) / void DetachChild(IIndicator child)	The IIndicator child-tree members. The base constructor attaches a child to its parent; Dispose() on a child detaches it.
void Dispose()	Idempotent teardown: disposes the children (newest first), runs OnDispose, then detaches from the parent.
protected void AdvanceChildren(IIndicatorContext ctx)	Runs the attached children for the current update, for an indicator you drive yourself (parent: null, no host). Under a host it does nothing: the host runs every child before its parent. See Driving an indicator yourself.
protected virtual void OnReset(IIndicatorContext ctx)	Reset hook for recalc: clears clearable plot series + disposes children. Override to drop custom accumulators (call base first).
Overridable virtuals
Signature	Description
virtual void OnInit(IIndicatorContext ctx)	Base seeds Context, resolves InputSource, and defaults Input/Bars. Call base.OnInit(ctx) first.
virtual void OnDataLoaded(IIndicatorContext ctx)	No-op by default. Vectorised backfill pass.
virtual void OnBarUpdate(IIndicatorContext ctx)	No-op by default. BIP filtering is handled by the framework wrapper.
virtual void OnMarketData(in Tick, …) / OnMarketDepth / OnMarketByOrder / OnMarketByOrderLoss / OnOrderBookLifecycle / OnDataSeriesReady / OnDispose	All no-op by default; override what you need.
virtual void OnDataConnectionChanged(DataConnectionStatus status, IIndicatorContext ctx)	Public virtual, no-op by default. Override to react when the chart's data connection changes; see Data connection.
protected virtual void OnDecision(DecisionAnswer answer)	No-op by default. Receives a completed Decisions.Ask. The chart host calls it on the chart's session worker, serialised with bar dispatch and recalculation, never on the thread the answer arrived on.
virtual string ContentKey { get; }	Lazily computed via IndicatorMetadata.For(GetType()).ComputeContentKey(this).
virtual CalculationMode Calculate { get; protected set; }	Default OnBarClose. Set in your ctor to opt into per-tick updates.
virtual bool ProcessesMarketData { get; protected set; } / ProcessesMarketDepth	Default false; set in your ctor to receive tick / depth callbacks.
CalculationMode ENUM
Frequency at which the runtime dispatches OnBarUpdate on the primary series (the same three modes as NinjaTrader). It controls only the bar-callback cadence; raw-tick delivery is the separate ProcessesMarketData opt-in.

Value	Meaning
OnBarClose = 0	Fire once per closed bar (default). Lowest CPU; the right choice for EMA/SMA/RSI.
OnPriceChange = 1	Fire whenever the bar's Close changes; skips repeat-price quotes.
OnEachTick = 2	Fire on every tick. Highest CPU; for tick-precision VWAP, footprint and volume-reactive indicators.
CalculationCadence STATIC HELPER
Decides whether a callback fires for a given CalculationMode and update. The indicator runtime and the strategy runner both use it, and a custom host can too.

public static bool ShouldFire(CalculationMode mode, bool isClosed, bool closeChanged);
//  OnBarClose    => isClosed
//  OnPriceChange => isClosed || closeChanged
//  OnEachTick    => true
Attributes
Class- and property-level attributes the framework reads by reflection to compute content keys, render the parameter editor, persist values, and route data. All are Inherited = true except [IndicatorHotkey].

IndicatorParameter (property)
Marks a property as a user-tunable parameter. Constraints are advisory (the editor enforces them; programmatic sets are not clamped).

Member	Description
string? DisplayName	Editor label (defaults to the property name, humanised).
string? Description	Tooltip text.
string? Group	Logical group; the editor renders a group together.
int Order	Display order within the group (lower first; ties break by declaration order).
object? MinValue, MaxValue, Step	Advisory numeric bounds + spinner increment.
object? ContentKeyDefault	Omit-when-default sentinel: the parameter contributes no content-key term while equal to this value, so adding a parameter leaves the keys of saved instances unchanged.
ParameterEditorKind Editor	Editor control hint. Default Auto.
string? FileFilter	File-dialog filter for FilePicker, in the standard "Name (*.ext)|*.ext|All files (*.*)|*.*" form. Null shows all files; other editor kinds ignore it.
bool Primary	When any parameter is marked primary, AI schema projection surfaces only primary knobs (plus the universal Input / Resolution parameters). The editor, codec and content-key behaviour are unchanged.
string? VisibleWhen	Conditional editor visibility: "Param=Value" or "Param=Val1|Val2". Null/empty = always visible. Editor-only and fails open (unknown left-hand side keeps the row visible).
bool Bold	Draws the setting's label bold in the settings list. Default false. Editor-only, like the two fields below: the value, persistence and the content key are unchanged.
ParameterLabelSize LabelSize	The size the setting's label is drawn at: Normal (default) or Large.
bool SeparatorBefore	Draws a separator line above the setting. Default false.
ParameterLabelSize enum: Normal = 0, Large = 1. ParameterFormat is the readonly record struct the metadata carries the three formatting fields in: ParameterFormat(bool Bold = false, ParameterLabelSize LabelSize = Normal, bool SeparatorBefore = false), with bool IsLarge. Its default value is the plain row. Strategy parameters use the same type.

ParameterEditorKind enum: Auto = 0 (host picks from the CLR type), FilePicker = 1 (path field + Browse), SymbolPicker = 2 (shared instrument picker for a benchmark/comparison symbol), Hidden = 3 (no settings-dialog row; the value still persists and still contributes to the content key), FontPicker = 4 (on a string holding a font family name: the same font list the chart properties use; the box stays editable so a family this machine lacks can still be typed, an empty value keeps the host's default font, and a host without a font list shows a plain text box).

IndicatorPlot (class, repeatable)
Declares a named plot the indicator produces and what its values mean. Declare one per output (MACD line, signal and histogram each get one). IndicatorMetadata.Plots exposes them as PlotDescriptors, with the semantics in PlotDescriptor.Semantics.

Member	Description
string Name (ctor)	The plot name.
string? Description	Optional description.
bool IsOverlay	Overlay on the price panel vs a separate panel.
PlotKind Kind	Shape (defaults to Overlay when IsOverlay, else Unspecified).
double RangeMin, RangeMax	Inclusive value bounds; double.NaN = unbounded (mapped to null).
PlotValueUnit Unit	Value unit.
PlotBullishDirection BullishDirection	Whether rising = bullish/bearish/neither.
string? Reference	Notable levels, e.g. "30/70 overbought/oversold".
Class-level marker & metadata attributes
Attribute	What it declares
[IndicatorDescription(string)]	Human-readable description shown in the Add Indicator and settings dialogs. The ctor throws ArgumentException on a null or blank string.
[IndicatorCategory(IndicatorCategory)]	The indicator's family (see enum below).
[IndicatorDisplayName(string)]	The name the user sees, instead of the one derived from the class name (type name, trailing "Indicator" trimmed, PascalCase split into words). Use it when the class name is not what a trader calls the indicator. The class name stays the indicator's identity in workspaces, templates and the AI catalog, so the on-screen name can change without breaking anything saved. The declared name is the English fallback: a translation under Indicator.{TypeName}.Name wins when present. Blank is ignored. (Class: IndicatorDisplayNameAttribute.)
[IndicatorFolder(string)]	Lists the indicator under a folder of your choosing in the indicator pickers ("My Scalping Tools") instead of under its category folder. Only where it is listed changes: IndicatorCategory still says what kind of indicator it is, so declare both. Indicators naming the same folder (compared case-insensitively) share it; the name is shown as written, is not translated, and is one level only (a "/" is part of the name). Blank is ignored. (Class: IndicatorFolderAttribute.)
[IndicatorInput(IndicatorInputKind)]	How the indicator consumes data: Scalar (surfaces the Input-series picker), Bars, or None.
[RequiresOrderFlow], [RequiresOrderFlow(OrderFlowDetail.Totals)]	Opt into pre-aggregated orderflow delivery + the synthesized "Resolution" parameter. Without an argument: the full footprint. With OrderFlowDetail.Totals: the indicator reads per-bar totals only (volume, buy volume, sell volume, trade count, delta), and the host loads the venue's own bars where they carry the buy/sell split.
[RequiresOpenInterest]	Opt into the per-bar Open-Interest channel (routing hint only; the channel is always reachable, all-NaN without an OI provider).
[SingleInstancePerChart]	At most one instance of this concrete type per chart; the chart ignores a second add. The limit applies per concrete type, so tagging a base class caps each derived type separately.
[ParameterContainer(Type)]	Delegate parameters to a separate settings object of type ContainerType. By default only its [IndicatorParameter] properties are parameters; AllProperties = true surfaces every public read/write property of a persistable type (bool, enum, numeric, string, ChartColor, time/date). The indicator must also implement IParameterContainerProvider.
Enums: IndicatorCategory = Unspecified, Trend, Momentum, Volume, Volatility, OrderFlow, Structure, Custom. IndicatorInputKind = Scalar = 0, Bars = 1, None = 2.

Keyboard shortcuts
An indicator declares the shortcuts it wants, one attribute per action, and implements IIndicatorHotkeyTarget to receive them. The declarations sit on the type, not an instance, so the host reads them from the catalog at start-up and the action can be bound before any chart has the indicator on it, the same way drawing-tool shortcuts work.

hotkeys
using System.Collections.Generic;
using SabrTrader.Pipeline.Indicators;

// Ids are dotted lower-case and PERSISTED in the user's binding file - never rename one.
[IndicatorHotkey("myzones.toggle", "Toggle zones", defaultGesture: "Ctrl+Alt+Z")]
[IndicatorHotkey("myzones.clear", "Clear zones")]   // no default: unbound until the user assigns one
public sealed class MyZones : IndicatorBase, IIndicatorHotkeyTarget
{
    private readonly List<double> _zones = new();
    private bool _show = true;

    public bool InvokeHotkey(string hotkeyId)
    {
        switch (hotkeyId)
        {
            case "myzones.toggle": _show = !_show; return true;
            case "myzones.clear":  _zones.Clear(); return true;
            default:               return false;    // not mine
        }
    }
}
IndicatorHotkeyAttribute(string id, string displayName, string? defaultGesture = null) (AllowMultiple = true, Inherited = false), with read-only Id, DisplayName and DefaultGesture. DisplayName is what the Keyboard Shortcuts dialog shows, and DefaultGesture uses the host's chord syntax (Ctrl+Alt+B); null leaves the action unbound until the user assigns one. When the key is pressed, every indicator on the active chart that implements IIndicatorHotkeyTarget is offered the id, so return false for an id that is not yours and expect other listeners.

Metadata & identity
Reflection-extracted, per-type cached data driving content keys, the parameter editor, and discovery.

Member	Description
IndicatorMetadata.For(Type) / For<T>()	Cached metadata for a type. Synthesizes the "Input series" and orderflow "Resolution" parameters when applicable.
string ComputeContentKey(object instance)	Deterministic, locale-invariant content key from name-sorted parameter values (honours ContentKeyDefault).
static object ResolveParameterTarget(object indicator, ParameterDescriptor)	The object a descriptor's get/set targets (the indicator itself, or its [ParameterContainer] object).
IReadOnlyList<ParameterDescriptor> Parameters / IReadOnlyList<PlotDescriptor> Plots	Declared parameters and plots.
Type IndicatorType, string Description, IndicatorCategory Category	Type and descriptive metadata.
string DisplayName / string Folder	The invariant user-facing name (from [IndicatorDisplayName], else derived from the type name) and the folder from [IndicatorFolder] (empty when none is declared, in which case pickers file the indicator under Category). Both are local facts of the type and never come from a cloud spec; identity stays on the CLR type name.
bool RequiresOrderFlow, RequiresOpenInterest, SingleInstancePerChart, IsInert	Structural flags. IsInert is true for a premium indicator whose definition is withheld (empty editor; it still constructs and can be removed).
IndicatorInputKind? InputKind	From [IndicatorInput]; null when the type declares none.
Records: ParameterDescriptor(PropertyInfo Property, string Name, string DisplayName, string Description, string Group, int Order, object? MinValue, object? MaxValue, object? Step, ParameterEditorKind Editor = Auto, string FileFilter = "") with init-only ContentKeyDefault, bool IsPrimary, string? VisibleWhen, ParameterFormat Format; PlotDescriptor(string Name, string Description, bool IsOverlay) with init-only Semantics. Both are sealed records the metadata builds; you read them, you never construct them.

IParameterContainerProvider: object ParameterContainer { get; } returns the live settings instance for a [ParameterContainer] indicator; never null once the indicator is constructed.

PlotSemantics
The machine-readable meaning of a plot's value, so a consumer can interpret RSI = 71.6 vs EMA = 5012 without recognising the indicator (a sealed record). PlotSemantics.Default has everything unspecified.

public sealed record PlotSemantics(
    PlotKind Kind, double? RangeMin, double? RangeMax,
    PlotValueUnit Unit, PlotBullishDirection BullishDirection, string? Reference);
// bool HasRange => RangeMin.HasValue && RangeMax.HasValue;
Enum	Values
PlotKind	Unspecified, Overlay, Oscillator, Histogram, Signal, Band
PlotValueUnit	Unspecified, Price, Percent, Ratio, Ticks, Raw
PlotBullishDirection	None, Up, Down
The reflected definition
What the host sees when it inspects an indicator type without instantiating it: the settings dialog, the indicator picker and the AI builder read this rather than your fields. It is produced from your attributes (or served as a premium spec, see below), so indicator code only reads it.

Interface	Members
IIndicatorDefinitionSpec	IndicatorCategory Category, string Description, IndicatorInputKind? InputKind, bool RequiresOrderFlow, bool RequiresOpenInterest, IReadOnlyList<IIndicatorSpecParameter> Parameters, IReadOnlyList<IIndicatorSpecPlot> Plots.
IIndicatorSpecParameter	Name, DisplayName, Description, Group, Order, ParameterEditorKind Editor, MinValue / MaxValue / Step (boxed), FileFilter, VisibleWhen: one [IndicatorParameter] as the dialog renders it.
IIndicatorSpecPlot	Name, Description, bool IsOverlay, PlotSemantics Semantics: one declared plot, with the semantics you gave it.
IIndicatorHost	The host side of an indicator instance: void SetContext(IIndicatorContext), bool EnsureInitialized(), void ResetForRecalc(), void DeliverDecision(DecisionAnswer) (runs the indicator's OnDecision on the chart's session worker, under the gate that serialises bar dispatch and recalculation), bool IsInitialized, IIndicatorContext? InstanceContext, IndicatorBase? InputChainChild, bool HostWalksChildren { get; set; }. IndicatorBase implements it; only a host calls it.
Input sources
IInputSourceAware / IOrderFlowResolutionAware: backing-store contracts (InputSourceDescriptor InputSource { get; set; } and OrderFlowResolution OrderFlowResolution { get; set; }) that IndicatorBase implements so the editor/codec descriptors are synthesized only for the right indicators.

InputPriceType enum (the standard price types plus the volume and open-interest channels): Close = 0, Open = 1, High = 2, Low = 3, Median = 4, Typical = 5, Weighted = 6, OHLC4 = 7, Volume = 8, OpenInterest = 9.

InputSourceDescriptor: a serializable value type (readonly struct) describing where the scalar Input comes from: a price type, or another indicator's plot (a private configured copy). Its canonical string is a frozen, persisted grammar; structural equality is semantic equality.

Member	Description
static InputSourceDescriptor Close { get; }	The default (equals default(InputSourceDescriptor)).
static InputSourceDescriptor Price(InputPriceType type)	A price-source descriptor.
static InputSourceDescriptor Indicator(string catalogKey, IReadOnlyDictionary<string,string>? parameters = null, string? plotName = null)	An indicator-plot source (parameters canonicalized).
InputPriceType PriceType · string? IndicatorKey, IndicatorParametersText, PlotName	The descriptor's components.
bool IsPrice, IsDefault	Classification helpers.
IReadOnlyDictionary<string,string> GetParameterMap()	The source indicator's overrides as a name→value map.
string ToString() / ToDisplayString()	Canonical (persisted) form / human-readable form.
static InputSourceDescriptor Parse(string) / bool TryParse(string?, out …)	Parse from string (throwing / non-throwing).
Optional capability interfaces
An indicator opts into extra host integration by ALSO implementing one of these alongside IIndicator, so only the indicators that need a capability take the dependency.

Interface	Opts into
IIndicatorPanelHint	Default panel placement via PanelPlacement DefaultPanel { get; } (PriceOverlay or NewSubpanel). Non-implementers default to PriceOverlay.
IChartCustomRender	A custom paint pass on the indicator's own panel: void OnCustomRender(IIndicatorRenderContext), plus CustomRenderLayer Layer, bool SuppressDefaultBarRendering (default false; polled every frame) and int ReservedRightPx (default 0: pixels the overlay occupies against the price axis; the chart reserves the widest value so candles end at the overlay's left edge).
IChartBackgroundRender	An extra below-bars paint pass: void OnRenderBackground(IIndicatorRenderContext), for dual-layer studies (volume profile backdrop plus foreground).
IPricePanelOverlayRender	Painting onto the main price panel even from a sub-panel: void OnRenderPricePanel(IIndicatorRenderContext) + CustomRenderLayer PricePanelLayer (like NinjaTrader's DrawOnPricePanel).
IBarColorSource	Per-bar candle recolouring (like NinjaTrader paint bars): bool PaintBarsEnabled { get; } and bool TryGetBarColor(int barIndex, in Bar bar, out ChartColor color). Last enabled source wins.
IChartClickReceiver	React to a left-click in the plot area: bool OnChartClick(ChartClickContext ctx) (return true to consume). Pairs with a custom render that drew the control.
IChartMouseMoveReceiver	Hover-driven decorations: void OnChartMouseMove(ChartMouseMoveContext ctx) (a NaN position = mouse left the chart).
IRepaintNotifier	Request a frame off-cadence (timer/async): event Action? RepaintRequested + void RequestRepaint(). The chart coalesces N requests to one paint.
IRecalculateNotifier	Ask the host to re-run history (not just repaint) when math-affecting state changed: event Action? RecalculateRequested.
IIndicatorToolbarMenu	Contribute a chart-toolbar dropdown: string MenuTitle, IReadOnlyList<ToolbarMenuItem> BuildMenu(), event Action? MenuChanged.
IIndicatorContextMenu	Contribute right-click menu items at a click location: IReadOnlyList<ToolbarMenuItem> BuildContextMenu(IndicatorContextMenuLocation) + event Action? ContextMenuChanged.
IDynamicOrderFlowRequirement	Runtime twin of [RequiresOrderFlow] when the need depends on live config: bool RequiresOrderFlow { get; } + event Action? RequirementChanged.
IIndicatorCellSearch	Make the indicator's own values searchable from the chart's search box: string SearchTitle, IReadOnlyList<IndicatorSearchField> SearchFields (what the user can search on) and IReadOnlyList<IndicatorSearchHit> Search(IndicatorSearchQuery query). The indicator decides what a hit means for its study; the host lists the hits and navigates to the one the user picks.
IIndicatorBarStatistics	Contribute rows to the per-bar statistics panel: string StatisticsTitle + IReadOnlyList<IndicatorStatisticRow> GetBarStatistics(int absBarIndex). Called for the bar under the cursor, so keep it allocation-light and side-effect free.
IIndicatorHotkeyTarget	Receive the keyboard shortcuts the indicator declared with [IndicatorHotkey]: bool InvokeHotkey(string hotkeyId) (return true when handled). The id is the attribute's, so one method switches over them.
ISubpanelAutoScaleSource	Custom-render subpanel contributes to Y auto-fit: bool TryGetAutoScaleRange(TimeSpan visibleSpan, out double min, out double max). Pass TimeSpan.Zero to fit over everything.
Search types (SabrTrader.Pipeline.Indicators.Search): IIndicatorCellSearch (above); IndicatorSearchField(string Id, string Name), one searchable field; IndicatorSearchQuery(string FieldId, double MinValue, IndicatorSearchLocation Location = Anywhere, IndicatorSearchDirection Direction = Any, int LookbackBars = 0, int MaxHits = 500), the user's search (LookbackBars = 0 searches all bars); IndicatorSearchHit(int AbsBarIndex, DateTime TimeUtc, double Price, double Value, string Summary), where Summary is what the user reads in the result list. IndicatorSearchLocation enum = Anywhere = 0, High = 1, Low = 2, Body = 3, Wick = 4; IndicatorSearchDirection enum = Any = 0, Up = 1, Down = 2. All are readonly record structs except the enums and the interface.
Statistics types (SabrTrader.Pipeline.Indicators.Statistics): IIndicatorBarStatistics (above); IndicatorStatisticRow(string Id, string Name, string Value, IndicatorStatisticTone Tone), where Value is pre-formatted text so the indicator owns its units; IndicatorStatisticTone enum = Neutral = 0, Positive = 1, Negative = 2 (sets the colour only).

Context structs: ChartClickContext(double X, double Y); ChartMouseMoveContext(double X, double Y) with IsInside + Outside; IndicatorContextMenuLocation(int BarIndex, double Price, DateTime Time, double PixelX, double PixelY). CustomRenderLayer enum = BelowBars = 0, AboveBars = 1 (Layer and PricePanelLayer default to AboveBars). PanelPlacement enum = PriceOverlay = 0, NewSubpanel = 1.

IIndicatorRenderContext & menu items
The per-frame context passed to the render capability methods. Single-threaded (the chart's UI thread); do not cache it across frames.

Member	Description
IChartRenderer Renderer { get; }	Active renderer (DrawLine/DrawText/FillRect/…).
ChartViewport Viewport { get; }	Bar↔pixel + price↔pixel transforms for the current paint.
IBarSeries? Bars { get; }	Primary bar series being rendered (null on an empty tab).
DateTime Now { get; }	Wall-clock at frame start, on the chart timeline (same clock as Bar.End; for BarTimer-style countdowns; pinnable in tests).
PlotAreaRect PlotArea { get; }	Pixel rect of the candle pane (excludes axis strips). PlotAreaRect(X, Y, Width, Height) with Right/Bottom.
double HeaderInsetPx { get; } / double ContentTopPx { get; }	Top chrome reserved by the legend; safe starting Y for top-anchored content. Default 0.
TimeZoneInfo? DisplayTimeZone { get; }	The chart's display zone. Time-of-day shading uses Bar.Start / Bar.End (already stamped); convert an external UTC source with ChartDisplayTime.FromUtc. Default null.
IReadOnlyList<IDrawingTool> Drawings { get; }	User-placed drawing tools (read-only). Default empty.
double? SizeMultiplier { get; }	Units per one raw volume/size unit for the chart's instrument, or null when sizes are already whole (futures, equities). Crypto sizes are carried as scaled integers (Bar.Volume and Tick.Size are long), so a render pass that prints a volume multiplies by this. Default null.
PerfScope BeginScope(string category, string? detail = null)	Nested perf sub-scope for the Task Manager. Default no-op.
ToolbarMenuItem hierarchy
A closed record hierarchy (the host pattern-matches the concrete types) reused by both IIndicatorToolbarMenu and IIndicatorContextMenu. The host asks for a fresh list each time the menu opens, so build it on demand and do not cache it.

Record	Use
ToolbarMenuItem(string Label)	Abstract base.
ToolbarCommand(Label, Action Invoke, bool IsEnabled = true)	Click-to-invoke action.
ToolbarCheckItem(Label, Func<bool> IsChecked, Action<bool> Toggle, bool IsEnabled = true)	Two-state toggle (re-queried each open).
ToolbarSubmenu(Label, IReadOnlyList<ToolbarMenuItem> Items, Func<bool>? IsChecked = null)	Nested submenu.
ToolbarSeparator()	Visual divider.
ToolbarNumberInput(Label, Func<double> GetValue, Action<double> SetValue, double Min, double Max, int Decimals = 0, bool IsEnabled = true)	Inline numeric field (clamped, rounded).
ToolbarSlider(…same shape as ToolbarNumberInput…)	Inline draggable slider.
ToolbarTextInput(Label, Func<string> GetText, Action<string> SetText, bool IsEnabled = true)	Inline free-text field (raw string pushed; indicator validates).
ToolbarDescription(string Text)	Greyed-out help line.
ToolbarRadioGroup(Label, IReadOnlyList<ToolbarRadioOption> Options)	Mutually-exclusive option group. ToolbarRadioOption(Label, Func<bool> IsSelected, Action Select, bool IsEnabled = true).
ToolbarColorPicker(Label, Func<ChartColor> GetColor, Action<ChartColor> SetColor, bool IsEnabled = true)	Reversible colour-picker row.
Helpers
Type	Description
CurrentBarsAccessor	Zero-allocation struct returned by ctx.CurrentBars (public ctor CurrentBarsAccessor(IIndicatorContext ctx)); this[int seriesIndex] is the last-bar index on that local series (-1 if empty).
IntraBarRefreshGate	Throttle for expensive intra-bar refresh work: IntraBarRefreshGate(int intervalMs = 250, Func<long>? clockMs = null), bool ShouldRefresh(bool force = false), const int DefaultIntervalMs = 250. Single-threaded; pass clockMs to pin the clock in tests.
BarTimeProjection (static)	Time-axis math for custom rendering: double FractionalBarIndex(DateTime t, IReadOnlyList<DateTime> barStarts, TimeSpan barDuration), TimeSpan EstimateBarDuration(IReadOnlyList<DateTime> barStarts, int sample = 64), and the nested BarTimeProjection.BarStartList(IBarSeries bars), a zero-copy IReadOnlyList<DateTime> view of a series' bar start times.
IndicatorOutputSeries (SabrTrader.Pipeline.Series)	Wraps an indicator's output series with the bar series it was computed from: new IndicatorOutputSeries(ISeries<double> inner, IBarSeries sourceBars). A downstream indicator that takes the wrapped output as its input reads SourceBars (IBarSourcedSeries) to seed its own primary series. See Child indicators.
Resolver & ambients
Type	Members
IIndicatorResolver	Type-keyed catalog (built-ins + plugins): IReadOnlyList<Type> AvailableTypes, IIndicator? Create(Type, IReadOnlyDictionary<string,object>? overrides = null) (returns null when unknown). Reachable via ctx.Indicators or IndicatorResolverAmbient.Resolver.
IndicatorResolverAmbient (static)	Process-wide resolver the host installs: IIndicatorResolver? Resolver, SetResolver(IIndicatorResolver?), and the scoped IDisposable Use(IIndicatorResolver?) for tests. Resolver is null until a host installs one.
OptionChainReaderAmbient (static)	Process-wide options data reader behind ctx.OptionChains: IOptionChainReader? Reader, SetReader(IOptionChainReader?), scoped IDisposable Use(IOptionChainReader?). The host installs it; indicators read ctx.OptionChains.
Premium definition seam
Lets the host serve a premium indicator's editor metadata from a cloud spec, or withhold it, per entitlement. A process that installs no source sees local reflection for every type. It is host-side only, and content keys are the same under every verdict.

Type	Members
IIndicatorDefinitionSource	DefinitionResult Resolve(Type indicatorType) and long Generation (bumped whenever a verdict can change, so IndicatorMetadata rebuilds its cached entry). Implementations read in-memory state only; no I/O on the call.
DefinitionVerdict (enum)	Reflection = 0 (metadata from your attributes), Spec = 1 (metadata from a served IIndicatorDefinitionSpec), Withheld = 2 (empty editor; the indicator still constructs, see IndicatorMetadata.IsInert).
DefinitionResult (readonly record struct)	DefinitionResult(DefinitionVerdict Verdict, IIndicatorDefinitionSpec? Spec, long Generation) with the factories Reflection(long generation), FromSpec(IIndicatorDefinitionSpec spec, long generation) and Withheld(long generation).
IndicatorDefinitionAmbient (static)	IIndicatorDefinitionSource? Source, SetSource(…), scoped IDisposable Use(…), event Action? Changed and RaiseChanged(). The host raises it after an entitlement or spec change so charts re-evaluate indicators already placed; it can fire on a background thread.
Alerts
An indicator raises a user-facing alert with ctx.Alert(…), passing an id, a message, an optional sound, an AlertSeverity and a re-arm window. The call is live-only and returns at once; the host routes the alert and delivers it to the channels the user has enabled. Guide: Alerts & channels; types: Alerts & sessions.

Chart trader panels
An indicator or strategy that implements IChartPanelProvider contributes its own tab to the chart trader panel. The contracts live in SabrTrader.Pipeline.Panels. Guides: Indicators › Chart trader panel and Strategies › Chart trader panel.

public interface IChartPanelProvider
{
    ChartPanel? CreateChartPanel();                   // null = contribute no tab
    void OnChartPanelReleased(ChartPanel panel) { }   // the tab is gone; drop your element refs
}

public abstract class PanelSurface                    // shared root of ChartPanel and AddOnView
{
    public IReadOnlyList<ChartPanelElement> Elements { get; }      // structure, fixed for its life
    public event Action<ChartPanelElement>? ElementChanged;       // host-facing
    public void Release();                                        // host-facing
}

public sealed class ChartPanel : PanelSurface
{
    public ChartPanel(string title, params ChartPanelElement[] elements);
    public string Title { get; }                                  // tab header, fixed for its life
}

public abstract class ChartPanelElement
{
    public bool IsEnabled { get; set; }               // greyed out and non-interactive while false
    public bool IsVisible { get; set; }               // out of the layout entirely while false
    public ChartPanelAccent Accent { get; set; }      // Neutral | Positive | Negative | Warning | Info
    protected void Invalidate();                      // tell the host to re-read this element
}

public abstract class ChartPanelContainer : ChartPanelElement   // base of Row and Group
{
    public IReadOnlyList<ChartPanelElement> Children { get; }      // fixed at construction
}
ElementChanged is raised on whichever thread wrote the element; the host marshals and coalesces. Release() is called by the host once the tab has left the screen: it detaches every element (so you can reuse the instances in the next panel you build) and stops ElementChanged. ChartPanelAccent values: Neutral = 0, Positive = 1, Negative = 2, Warning = 3, Info = 4, resolved against the user's theme. Hiding a container hides its children.

The element kinds, all of them writable at any time from any thread:

new ChartPanelButton(string text, Action onClick);
new ChartPanelToggle(string text, Action<bool> onToggled, bool isOn = false);
new ChartPanelCheckBox(string text, Action<bool> onChanged, bool isChecked = false);
new ChartPanelLabel(string caption, string text = "");
new ChartPanelChoice(string caption, IReadOnlyList<string> options, Action<int> onSelected,
                     int selectedIndex = 0);
new ChartPanelNumber(string caption, Action<double> onChanged, double value = 0,
                     double minimum = 0, double maximum = double.MaxValue,
                     double step = 1, int decimals = 0);
new ChartPanelText(string caption, Action<string> onChanged, string text = "");
new ChartPanelSeparator();
new ChartPanelRow(params ChartPanelElement[] children);
new ChartPanelGroup(string caption, params ChartPanelElement[] children);
Setting a value property (Text, IsOn, IsChecked, SelectedIndex, Value) updates the control without raising your callback, so reflecting a state you changed yourself cannot re-enter your own handler. Read-only members: Caption on the captioned kinds, ChartPanelChoice.Options and SelectedOption (the selected text), and ChartPanelNumber.Minimum / Maximum / Step / Decimals.

The host applies user input through ChartPanelButton.Click(), ChartPanelToggle.Toggle(bool), ChartPanelCheckBox.Check(bool), ChartPanelChoice.Select(int), ChartPanelNumber.Commit(double) (clamps and rounds first) and ChartPanelText.Commit(string). Each stores the value and raises your callback on the plugin's callback thread; plugin code uses the value properties instead. An element belongs to one panel at a time: passing it to a second panel throws until the first is released.

