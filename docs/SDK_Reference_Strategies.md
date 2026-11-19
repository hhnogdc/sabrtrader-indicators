Reference
Strategies
The automated-trading surface: the Strategy base class and its context, parameter and indicator declarations, the managed-order and ATM layers, the host-side seams, the catalog, and the performance analytics.

On this page
Strategy & context
Order-flow-aware strategies
Live data connection
Decision models
Recorded order book
Parameters & indicators
Managed orders & ATM
Host seams
Catalog
Performance analytics
Strategy & context: SabrTrader.Pipeline.Strategies
Derive from Strategy, declare parameters and indicators in OnInitialize, and act in OnBar through the protected helpers. Every helper reads from or writes to the IStrategyContext the runner binds before the first callback. Quantity units are explained under Instrument metadata & sizing.

Type	Members
Strategy (abstract)	Run and instrument: Instrument, Account, StrategyName, State, Calculate (public get, protected set), TickSize, PointValue, QuantityStep, MinQuantity, WireUnitsPerQuantity, ToWireQuantity(quantity).
Bars: BarCount, CurrentBar, GetBar(barsAgo), TryGetBar(barsAgo, out bar), IsBarClosed, IsFirstTickOfBar, TryGetOrderFlowBar(barsAgo, out orderFlow), TryGetBookView(out view), RecordedDepth.
Sessions: TradingHours, Session, CreateSessionIterator(template), IsFirstBarOfSession, InSession.
Position and account: Position, IsFlat, IsLong, IsShort, AccountSnapshot, WorkingOrders, RiskStateRegistry, PositionOnTheWay (true while Position does not show a fill heard yet and the venue's settle time has not passed: wait before sizing an order from it).
Data connection: DataConnection, IsDataConnected (below).
Raw orders: PlaceOrder(request), CancelOrder(orderId), ModifyOrder(orderId, modification), Flatten().
Managed orders: EnterLong(quantity), EnterShort(quantity), ExitLong(), ExitShort(), HasPendingManagedOrder.
ATM: UseAtm(AtmStrategy, double? tickSize = null, double? pointValue = null), HasAtmBracket, SetAtmBracketGeometry(double stopOffset, double targetOffset), SetNextAtmStopPrice(double? stopPrice), ClaimPositionManagement().
Options: OptionChains (IOptionChainReader?, null when the host serves no options data), Task<MultiLegPlacement> PlaceMultiLegOrderAsync(MultiLegOrderRequest, ct) (plus an overload taking the leg OptionContracts and an optional name, which also records the placement as one named option strategy), Task<MultiLegPrecheckResult> PrecheckMultiLegOrderAsync(MultiLegOrderRequest, ct), Task<bool> ExerciseOptionAsync(instrument, quantity, ct) (false when refused or unsupported); see Multi-leg orders & exercise.
Diagnostics: Log(message), NotifyEntryBlocked(reason).
Decisions: Decisions (IDecisionClient, below).
AI: Ai (IAiClient, SabrTrader.Pipeline.AiRequests; AiClient.None in backtests). See AI requests.
News: News (INewsClient, SabrTrader.Pipeline.News; live and past headlines and story text, handlers on the strategy pump; NewsClient.None in backtests and until the host wires one). See News and Reference › News.
Declare: IntParameter, DoubleParameter, BoolParameter, StringParameter, FilePathParameter (below), DeclareIndicator<T>(Func<T> factory).
Overrides: OnInitialize, OnStart, OnBar, OnOrderUpdate(Order), OnFill(Fill), OnPositionUpdate(Position), OnManagedOrderFailed(string clientTag, Order? order, Exception? error) (a managed order whose placement call failed, see Managed orders & ATM), OnStop, OnDecision(DecisionAnswer), OnDataConnectionChanged(DataConnectionStatus), and with ProcessesMarketDepth set OnMarketByOrder(in MboEvent), OnMarketDepth(in DepthUpdate), OnOrderBookLifecycle(OrderBookLifecycleEvent), OnMarketByOrderLoss(long) (below).
IStrategyContext	The runtime seam the host implements; strategy code reaches it only through the Strategy helpers. Required members: Instrument, Account, StrategyName, State, BarCount, CurrentBar, GetBar, TryGetBar, Position, AccountSnapshot, WorkingOrders, PlaceOrder(request, Action<Exception>? onFault = null), CancelOrder(orderId, onFault), ModifyOrder(orderId, modification, onFault), Flatten(), Log(message). Default-implemented members: TickSize, PointValue, QuantityStep, MinQuantity (null; the last two in venue units), WireUnitsPerQuantity (1), TryGetOrderFlowBar, TryGetOrderBook and TryGetBookView (false), IsBarClosed (true), IsFirstTickOfBar (false), DataConnection (NotApplicable), IsDataConnected (DataConnection == Connected), TradingHours (null), CreateSessionIterator(template?) (null), IsFirstBarOfSession (false), IsPaused (false; true while the host has paused the run for feed recovery), OptionChains and RecordedDepth (the host-installed ambient readers), the options calls (report "unsupported"), RiskStateRegistry (null), PositionOnTheWay (false: a context with no run has nothing on its way), NotifyEntryBlocked(reason) (logs with an "Entry blocked: " prefix), CreateManagedOrderController() and CreateAtmService(AtmStrategy, double tickSize, double pointValue) (throw NotSupportedException), ClaimPositionManagement() (no-op), Decisions (DecisionClient.None), Ai (AiClient.None), News (NewsClient.None: backtests, the Strategy Analyzer and hosts without news leave it).
StrategyState	enum: Created, Initialized, Running, Stopped, Faulted. Stopped and Faulted are terminal.
RenderingStrategy	abstract Strategy that implements IChartCustomRender and IRepaintNotifier, for a strategy that draws on the chart it is attached to: protected virtual void OnCustomRender(IIndicatorRenderContext context) (runs on the chart render thread; publish state to it through an immutable snapshot in one volatile field), protected virtual CustomRenderLayer RenderLayer (default AboveBars), protected void RequestChartRepaint(), event Action? RepaintRequested. Anchor drawings by price and time: the render context's bars belong to the chart. See Custom rendering.
Order-flow-aware strategies
The host loads per-bar order flow for a run when the strategy implements IOrderFlowAwareStrategy and returns true, or declares an indicator marked [RequiresOrderFlow]. TryGetOrderFlowBar then returns data. The host calls RequiresOrderFlow on a throwaway instance before OnInitialize, passing the run's parameter overrides (null or empty means defaults); it must be free of side effects, must not throw, and should return false for a configuration that needs no order flow. See Order flow and the order book.

public interface IOrderFlowAwareStrategy
{
    bool RequiresOrderFlow(IReadOnlyDictionary<string, object>? parameterValues);
}
Live data connection
DataConnection is the state of the market-data connection that feeds the run's instrument: the data venue behind the chart, which is not necessarily the broker the orders go to. It follows that venue through drops and reconnects, including when the platform replaces the session. IsDataConnected is true only while it is Connected.

OnDataConnectionChanged(DataConnectionStatus status) runs on a live run when that state changes. Inside the call, DataConnection already returns status. It runs on the strategy's callback queue like OnBar, never at the same time as another callback, and a throw faults the run. Quick flips are coalesced, so you always end on the current status but may skip one in between. It does not fire for the status the run starts with (read DataConnection in OnStart), and it never fires in a backtest.

The callback is for you to react on top of the host's own policy, which still stops the run or holds new managed entries (IStrategyContext.IsPaused) on a lost feed. A typical use is cancelling a working entry order while prices are stale.

DataConnectionStatus (SabrTrader.Pipeline.Providers)	Meaning
NotApplicable	No live connection to report: a backtest, an optimizer pass, or a host that tracks no connection. Read it as "does not apply".
Disconnected	No connection and none in progress. The venue is idle or not configured.
Connecting	A first connect attempt is in progress.
Connected	Connected. Live data can flow.
ConnectionLost	Was connected and the link dropped. No reconnect is running yet.
Reconnecting	Was connected, the link dropped, and a reconnect attempt is in progress.
Failed	The last connect attempt failed and the venue has not connected since. A rejected login stays here until the user fixes the connection.
C#
protected override void OnDataConnectionChanged(DataConnectionStatus status)
{
    // Flat means every working order is an entry; a position's stop and target stay put.
    if (IsFlat && status is (DataConnectionStatus.ConnectionLost or DataConnectionStatus.Reconnecting))
    {
        foreach (Order order in WorkingOrders)
            CancelOrder(order.Id);
        Log("Data feed " + status + ": cancelled working entry orders.");
    }
}
Decision models: SabrTrader.Pipeline.Decisions
A strategy asks a connected decision model through Decisions.Ask(request, state) and gets the answer in OnDecision. A decision provider plugs in as a venue plugin with VenueCategory.Decisions. The guide, with a full strategy and a full provider, is Decision models. This is the type list.

Type	Members
IDecisionClient	DecisionAskStatus Ask(DecisionRequest request, DecisionStateBuilder state). Queues the ask and returns at once.
DecisionClient	static IDecisionClient None: every Ask returns NotAvailableHere. The default on hosts that deliver no decisions.
DecisionAskStatus	enum: Accepted, NotEntitled, NoConnection, InvalidState, NotAvailableHere. Only Accepted is followed by OnDecision.
DecisionRequest	(string name, IReadOnlyList<DecisionQuestion> questions): Name, Questions. 1 to 16 questions with unique names. Build once, reuse.
DecisionQuestion	abstract: Name, Instructions. Sealed kinds: ChoiceQuestion(name, instructions, IReadOnlyList<DecisionOption> options) with Options; ScoreQuestion(name, instructions, IReadOnlyList<string> levels) with Levels; YesNoQuestion(name, instructions).
DecisionOption	(string key, string criterion): Key, Criterion.
DecisionStateBuilder	Add(name, string | bool | long | double), BeginState(name), EndState(), Reset(), HasFault, Length, Written. A broken rule sets HasFault instead of throwing.
DecisionAnswer	RequestName, BarCount, ProviderId, ModelVersion, Status, TryGetChoice(name, out ChoiceAnswer), TryGetScore(name, out ScoreAnswer), TryGetYesNo(name, out YesNoAnswer). Public constructor (requestName, barCount, providerId, modelVersion, status, choices?, scores?, yesNo?), where each list holds (string Name, TAnswer Answer) tuples, for tests.
DecisionStatus	enum: Answered, Superseded, Unavailable, Faulted.
ChoiceAnswer	OptionKey, OptionIndex, Probability, Confidence, TryGetProbability(optionKey, out probability).
ScoreAnswer	Score (level units), LevelIndex, Confidence, TryGetLevelProbability(levelIndex, out probability).
YesNoAnswer	Probability.
OptionProbability	readonly struct (string optionKey, double probability): OptionKey, Probability.
DecisionLimits	static constants: MaxQuestions 16, MinChoiceOptions 2, MaxChoiceOptions 255, MinScoreLevels 2, MaxScoreLevels 10, MinNameLength 1, MaxNameLength 64, MaxStringUtf8Bytes 1024, MaxStateBytes 16384, MaxEntries 256, MaxDepth 4, MaxRequestsPerClient 32.
IDecisionProvider	Provider side: DecisionProviderProfile Profile, ValueTask<DecisionProviderResult> AskAsync(DecisionProviderRequest request, CancellationToken cancellationToken). Exposed by a connected venue session through GetPort<IDecisionProvider>().
DecisionProviderProfile	(providerId, modelId, modelVersion, int maxChoiceOptions, int concurrentCalls, TimeSpan callTimeout): the same properties, plus MinConcurrentCalls 1, MaxConcurrentCalls 8, MinCallTimeout 1 s, MaxCallTimeout 120 s.
DecisionProviderRequest	(string modelId, DecisionRequest request, ReadOnlyMemory<byte> canonicalState): ModelId, Request, CanonicalState.
DecisionStateReader	(ReadOnlyMemory<byte> canonicalState), bool TryRead(out DecisionStateEntry entry).
DecisionStateEntry	readonly struct: Kind, Name, Text, Bool, Int64, Double.
DecisionValueKind	enum: String, Bool, Int64, Double, BeginState, EndState.
DecisionProviderResult	factories FromAnswer(ProviderAnswer), Transient(reason, TimeSpan? retryAfter = null), Unauthorized(reason), Unreachable(reason), Invalid(reason); properties Kind, Answer, Reason, RetryAfter.
DecisionProviderKind	enum: Answered, Transient, Unauthorized, Unreachable, Invalid.
ProviderAnswer	(string modelVersion, IReadOnlyList<ProviderQuestionAnswer> questions): ModelVersion, Questions.
ProviderQuestionAnswer	abstract: QuestionName. Kinds: ProviderChoiceAnswer(questionName, optionKey, confidence, IReadOnlyList<OptionProbability> probabilities) with OptionKey, Confidence, Probabilities; ProviderScoreAnswer(questionName, score, confidence, IReadOnlyList<double> levelProbabilities) with Score, Confidence, LevelProbabilities (empty when the model has no per-level distribution); ProviderYesNoAnswer(questionName, probability) with Probability.
The order book
A strategy that reads the book sets ProcessesMarketDepth = true in its constructor, and BookLevels to the price levels per side it reads (0, the default, means the callbacks only). BookOrderDetail = true adds the orders per level, at a cost on every update, so set it only when the strategy reads orders. The run then gets the book:

Live, from the venue's book for the run's instrument. The venue's depth and market-by-order streams are subscribed while the run reads the book and let go when it stops.
In a backtest, an EveryTickReal run with a recorded MBO or L2 tape streams the tape into the run's book in timestamp order with the ticks.
Read it with TryGetBookView(out DomBookView? view): an immutable view of the book down to BookLevels, lock-free, the same call live and in a backtest. See DomBookView. The strategy's indicators read the same book, and every book event reaches them before the strategy, as bars do. TryGetOrderBook(out book) is obsolete: it hands out the book object itself, which only a backtest can do safely; read the view instead.

These hooks fire, all after the book has been updated and never during warmup bars:

Hook	Fires
OnMarketByOrder(in MboEvent evt)	Per MBO event. A tape replay is complete: every recorded event fires, in order. Live, a run that falls behind is handed the newest events and told how many it skipped (OnMarketByOrderLoss).
OnMarketDepth(in DepthUpdate update)	Per L2 event: live from a venue that serves L2 depth, in a backtest when the tape is L2 (no MBO).
OnMarketByOrderLoss(long lostEvents)	Live only, before the MBO event that follows a skip: how many events the run was not handed. The book itself is complete; a strategy that builds state from the callbacks knows it saw a sample.
OnOrderBookLifecycle(OrderBookLifecycleEvent evt)	Per book transition, never skipped. Live: the venue's reset and re-image. In a backtest, per control marker on the tape, in stream order: Reset, CaptureGap (a hole the recorder wrote; the book was cleared and is incomplete until the next re-image), RebuildStarted / RebuildCompleted around a venue image. See OrderBookLifecycleEvent.
Outside a run, RecordedDepth (IRecordedDepthCatalog?, null when the host records nothing) opens the same tapes directly: see IRecordedDepthCatalog.

Parameters & indicators
Parameters are typed, optionally optimizable, and convert implicitly to their value. A declared indicator returns a handle whose Instance the runner builds, backfills and updates each bar. See Parameters and Using indicators.

Type	Members
Declaration helpers on Strategy	IntParameter(name, int default), IntParameter(name, int default, int min, int max, int step), DoubleParameter(name, double default), DoubleParameter(name, double default, double min, double max, double step), BoolParameter(name, bool default), StringParameter(name, string default), FilePathParameter(name, string default, string fileFilter). Each returns a StrategyParameter<T>; the five-argument overloads are optimizable. WithFormat(bool bold = false, ParameterLabelSize labelSize = Normal, bool separatorBefore = false) on the returned parameter sets how its settings row is drawn and returns the parameter; the base class exposes the result as ParameterFormat Format.
StrategyParameter (abstract)	string Name, OptimizationRange? OptimizationRange, bool IsOptimizable, ParameterEditorKind EditorKind, string? FileFilter, object BoxedValue, Type ValueType.
StrategyParameter<T>	T DefaultValue, T Value, ValueType, BoxedValue, ToString() ("Name=Value"); implicit operator T(StrategyParameter<T>).
ParameterSet	IReadOnlyList<StrategyParameter> All (declaration order), int Count, StrategyParameter? Find(string name) (case-insensitive), bool TrySetValue(string name, object? value) (false for an unknown name or a value that does not convert). The host reaches a strategy's set through IStrategyHost.ParameterSet.
OptimizationRange	record (decimal Min, decimal Max, decimal Step); Validated(string parameterName) returns the range or throws ArgumentException when Min > Max or Step is not positive.
StrategyIndicator<T>	T Instance (throws InvalidOperationException before the runner has built the indicator), bool IsReady; where T : IndicatorBase.
Managed orders & ATM
The Strategy base class routes its managed and ATM helpers through two interfaces it obtains from IStrategyContext. Strategy code uses the helpers; a custom host implements these interfaces to change how intent becomes orders or how brackets are managed.

Type	Members
IManagedOrderController	bool HasPendingOrder, void EnterLong(decimal quantity), void EnterShort(decimal quantity), void ExitLong(), void ExitShort(), void HandleOrderUpdate(Order order) (called with every order update before OnOrderUpdate), event Action<string, Order?, Exception?>? Failed (raised once, on the strategy thread, when a managed order's placement call faulted or was cancelled, so no order exists and no order update will come; carries the client tag, no order and the failure, with HasPendingOrder already false; the default never raises it). The Strategy base forwards Failed to OnManagedOrderFailed. An order the venue rejects is not reported there: it reaches OnOrderUpdate as a rejected order. Created on first use by IStrategyContext.CreateManagedOrderController().
IAtmService	bool HasBracket, void UpdateBracketGeometry(decimal stopOffset, decimal targetOffset), void SetNextBracketStopPrice(decimal? stopPrice), void OnBar() (after the strategy's closing OnBar), void OnOrderUpdate(Order order), void OnPositionUpdate(Position position) (before the strategy's hook). Created by IStrategyContext.CreateAtmService when the strategy calls UseAtm.
See also. AtmStrategy and the bracket model live in the trading contracts: see Trading → ATM and Advanced trade management.
Host seams
These types connect a strategy to the runner that hosts it. Plugin strategies use none of them directly; they matter for a custom host and for unit tests.

Type	Members
IStrategyHost	The runner's side of a strategy: Bind(IStrategyContext), ParameterSet, IndicatorDeclarations, and the pumps RaiseInitialize, RaiseStart, RaiseBar, RaiseIntrabar, RaiseOrderUpdate(Order), RaiseFill(Fill), RaisePositionUpdate(Position), RaiseMarketByOrder(in MboEvent), RaiseMarketDepth(in DepthUpdate), RaiseOrderBookLifecycle(OrderBookLifecycleEvent), RaiseDataConnectionChanged(DataConnectionStatus), RaiseDecision(DecisionAnswer), RaiseStop. Strategy implements it explicitly and each pump runs the matching hook, so a unit test can cast a strategy to IStrategyHost, Bind a fake IStrategyContext, and push bars, fills, a status change or a hand-built DecisionAnswer into it.
IndicatorDeclaration	One DeclareIndicator call as the runner sees it: IndicatorBase Create() runs your factory, void Resolve(IndicatorBase instance) binds the built and backfilled instance to your StrategyIndicator<T> handle.
Catalog: SabrTrader.Pipeline.Strategies.Catalog
Decorate a strategy with [StrategyMetadata] for a friendly name and description. The host discovers strategies by reflection and shows that name and description in its strategy lists. See Discovery & metadata.

Type	Members
StrategyMetadataAttribute	[AttributeUsage(Class, AllowMultiple = false, Inherited = false)]: string? DisplayName, string? Description (init).
Performance analytics
The host reports a finished backtest's figures as a PerformanceReport (namespaces SabrTrader.Pipeline.Analytics and SabrTrader.Pipeline.Analytics.Metrics). Nullable ratios are null when undefined. See Reading the result.

Type	Members
PerformanceReport	record with required init properties: Pnl, Trades, Drawdown, RiskAdjusted, Excursion, Exposure, TradeList, EquityCurve; computed HasTrades; static Empty(decimal startingCash).
Trade	record, one round trip: Number, Direction, EntryTimeUtc, ExitTimeUtc, EntryPrice, ExitPrice, Quantity, GrossPnL, Commission, NetPnL, IsWinner, IsLoser, IsOpen, Duration, BarsInTrade, MaeCurrency, MaePrice, MfeCurrency, MfePrice, EndTradeDrawdown, ExitTag (why it closed, see TradeExitRoles), ExecutionIds.
TradeExitRoles	static string constants for Trade.ExitTag: Stop ("bracket-sl"), Target ("bracket-tp"), BracketExit ("bracket-exit"), Strategy ("strategy"). Compare against the constants rather than the literals.
TradeDirection	enum: Long, Short.
EquityPoint	readonly record struct (DateTime TimeUtc, decimal Equity).
PnLSummary	StartingCash, NetProfit, GrossProfit, GrossLoss, TotalCommission, ProfitFactor (double?), ReturnPct, EndingEquity; static Empty(decimal startingCash).
TradeStatistics	TotalTrades, WinningTrades, LosingTrades, BreakEvenTrades, LongTrades, ShortTrades, LongWinningTrades, ShortWinningTrades, WinRatePct, LongWinRatePct, ShortWinRatePct, AverageTrade, AverageWinner, AverageLoser, LargestWinner, LargestLoser, MaxConsecutiveWinners, MaxConsecutiveLosers, WinLossRatio (double?), Sqn (double?), AverageBarsInTrade; static Empty.
DrawdownMetrics	MaxDrawdown, MaxDrawdownPct, AverageDrawdown, LongestDrawdownDuration, MaxRunup, UlcerIndex, RecoveryFactor (double?); static Empty.
RiskAdjustedMetrics	SharpeRatio, SortinoRatio, CalmarRatio, DailyReturnStdDevPct (all double?), AnnualReturnPct; static Empty.
ExcursionMetrics	AverageMae, AverageMfe, WorstMae, BestMfe, AverageEndTradeDrawdown, AverageTradeEfficiencyPct (double?); static Empty.
ExposureMetrics	TimeInMarketPct, TotalContractsTraded, AveragePositionSize; static Empty.
The replay resolution and intra-bar tie-break of a backtest are settings of the host's Strategy Analyzer, described under Replay resolution & tie-break.

