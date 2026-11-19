Reference
Venue seam
The venue SDK surface a plugin implements: plugin, descriptor and manifest; session, context and notices; the instrument catalog and symbol grammar; backfill composition; the depth/MBO ingest plane; the venue-neutral trading components; the chain SPI; and the trade-history and cash-activity contracts. Narrative: Venue plugins and Broker venues.

On this page
Plugin & descriptor
Manifest & capabilities
Session & context
Notices, failure & storage
Instruments & symbols
Backfill composition
Depth & MBO ingest
Venue-neutral trading components
Option chain SPI
Trade history & cash activity
Plugin & descriptor: SabrTrader.Pipeline.Venues
One plugin DLL exports IVenuePlugin; each venue it provides is one IVenueDescriptor. Narrative: The plugin & manifest.

Type	Members
IVenuePlugin	IReadOnlyList<IVenueDescriptor> Venues { get; }: called once at discovery; must be fast, pure and must never touch the network.
IVenueDescriptor	VenueManifest Manifest; IVenueSession CreateSession(VenueSessionContext); bool TryValidateSettings(IReadOnlyDictionary<string,string>, out string? error) (default true); Task<IReadOnlyList<VenueChoice>> GetDynamicChoicesAsync(string fieldKey, IReadOnlyDictionary<string,string> currentValues, CancellationToken) (default: the static choices); IVenueSymbolParser? CreateSymbolParser(ISharedSymbolParsers) (default null).
VenueChoice	record (string Label, string Value); static IReadOnlyList<VenueChoice> StaticChoicesFor(VenueManifest, string fieldKey).
Manifest & capabilities
Type	Members
VenueManifest	record (string TypeId, string DisplayName, VenueCategory Category, IReadOnlyList<VenueAssetClass> AssetClasses, VenueCapabilities Capabilities, IReadOnlyList<ProviderCredentialField> SettingsSchema, bool SupportsPaper = false, int MaxConcurrentSessions = 1, int FallbackPriority = VenuePriorityBands.Default, string? StateStorageFolder = null, string? AttributionBadge = null, bool AccountIdsIdentifyUser = false); void Validate() throws ArgumentException naming the venue. AccountIdsIdentifyUser: true when the venue assigns each account id to ONE customer (Rithmic), so the host may treat it as the user's identity for trial-farming detection; false for product constants and shared labels, which identify nobody.
VenueCategory	enum: MarketData, Levels, Decisions.
VenueAssetClass	enum: Unknown, Future, Option, Stock, Forex, Crypto, Index, Commodity, Metal, Bond, CryptoPerp.
VenueCapabilities	[Flags] enum (long). Data: LiveTrades, LiveQuotes, MarketDepth, MarketByOrder, OpenInterest, MarketSummary, Fundamentals. Historical: HistoricalBars, HistoricalTicks, HistoricalOrderFlow, NativeBars. Instruments: InstrumentCatalog, SymbolSearch. Trading: Trading, CashActivity, TradeHistoryBackfill. Extras: OptionChains, Levels, Replay.
VenuePriorityBands	static const: Primary = 100, Default = 500, Fallback = 900. Lower wins.
ProviderCredentialField	record (string Key, string DisplayName, ProviderCredentialKind Kind, string? Placeholder, bool Required = true, IReadOnlyList<string>? Choices, string? DefaultValue, string? HelpText, string? WarningText, bool Multiline = false, bool DynamicChoices = false, bool ReadOnly = false, string? StorageName = null, bool IsIdentity = false); init property ProviderFieldPresetHint? PresetHint. Namespace SabrTrader.Pipeline.Providers.
Two-layer rule. The manifest declares INTENT; the port bitmasks (ProviderCapabilities, TradingProviderCapabilities) are the runtime FACT and may be narrower per session, never wider.

Session & context
Type	Members
IVenueSession : IAsyncDisposable	ProviderConnectionStatus Status; event Action<ProviderConnectionStatus>? StatusChanged; VenueFailure? LastFailure; IDataProvider? Data; ITradingProvider? Trading; event Action<VenueStreams>? StreamsRestored; Task ConnectAsync(CancellationToken); Task DisconnectAsync(); T? GetPort<T>() where T : class (default this as T).
VenueStreams	[Flags] enum: None, MarketData, Trading, Account.
VenueFailure	record (string Message, bool Permanent, string? Code = null). Permanent stops the host supervisor retrying.
IVenueSessionDecorator	IVenueSession Inner { get; }: marker for wrapping sessions.
VenueSessionPorts	static: T? AsPort<T>(this IVenueSession?): walks decorators, asking each level's GetPort<T>.
VenueSessionContext	Profile, Settings (decrypted, keyed by schema field key), Notices, Storage, Credentials, Depth, BackfillComposer, IvHistory, Clock, plus the host services below.
VenueProfileInfo	record (Guid ProfileId, string ProfileName, bool Paper = false).
IVenueSessionAttribution
Optional session port: IReadOnlyList<string> SessionBadges: licence-mandated attribution this SESSION carries beyond the manifest's AttributionBadge (Rithmic's OMNE mark applies only while the OMNE library drives the connection). Read once per connected session; return a list that is stable for the session's life.

Host services on VenueSessionContext
Member	Meaning
Func<ICommissionModel>? CommissionModelOf	Per-fill commission model for a venue whose blotter reports Commission = 0. Invoke per mapped fill so a settings edit needs no reconnect. Null = history fills keep 0.
Func<string, decimal>? CommissionMultiplierOf	Instrument → currency-per-point for that model. Null falls back to 1 (raw points).
Action<IInstrumentMetadata, string>? InstrumentResolved	Callback when the venue resolves reference data for a wire symbol; the host merges tick / point / currency into the local catalog. Null = no catalog enrichment.
Notices, failure & storage
Type	Members
IVenueNoticeSink	void Post(VenueNotice), void Log(string). Non-blocking, never throws, available from construction.
VenueNotice	record (VenueNoticeSeverity Severity, string Message, string? Symbol = null, string? Code = null).
VenueNoticeSeverity	enum: Info, Warning, Error.
VenueNoticeCodes	const: EntitlementDenied = "entitlement-denied", DelayedData = "delayed-data".
IVenueStateStorage	string? ReadText(string), void WriteText(string, string), string? ReadLocalText(string), void WriteLocalText(string, string). The Local lane is guaranteed machine-local.
IVenueCredentialWriter	void Persist(IReadOnlyDictionary<string,string> updatedValues): plaintext keyed by schema field key; the host encrypts and saves.
VenueNotConnectedException	The one offline condition for both planes.
IVenueIvHistorySink	void Publish(string underlyingSymbol, IReadOnlyList<VenueIvHistoryPoint> points): venue-served native IV history, seeds IV Rank. Idempotent per session host-side; never throws.
VenueIvHistoryPoint	readonly record struct (DateOnly Session, double Iv30). Iv30 is a FRACTION (0.256 = 25.6%), the platform's IV unit.
LevelsVenueSession	Shipped IVenueSession for options-levels vendors: ctor (string displayName, Func<ILevelsFeed> feedFactory, IVenueNoticeSink? notices = null). Builds the feed inside ConnectAsync, resolves it through GetPort<ILevelsFeed>() only while Connected, reports failures NON-permanent. Data and Trading are always null.
Instruments & symbols
Type	Members
IVenueInstrumentCatalog	IReadOnlyList<VenueCatalogEntry> ListEntries(); event Action? EntriesChanged; string? LoadError (default null: why the last load came back empty or short). Probed with Data.AsCapability<T>(). The catalog surface: serves both the persistent sync and live exchange browse.
VenueCatalogEntry	readonly record struct (string InstrumentId, VenueAssetClass AssetClass, string? Exchange, string? Description, string? ProductCode, string? InstrumentType, string? Expiration).
IVenueInstrumentClassifier	VenueInstrumentClassification? Classify(string instrumentId): the asset class and symbol a venue means by one raw id, paired with IInstrumentDirectory's flat id list.
VenueInstrumentClassification	readonly record struct (VenueAssetClass AssetClass, string Symbol).
IVenueSymbolParser	VenueSymbolParse Parse(string rawSymbol). Pure, fast, never throws.
VenueSymbol	readonly record struct (VenueAssetClass AssetClass, string Root, int? ExpiryYear, int? ExpiryMonth); factories Future(root[, year, month]), Stock, Index, Forex, Crypto.
VenueSymbolParse	record (VenueSymbol? Best, VenueSymbolConfidence Confidence, IReadOnlyList<VenueSymbol>? Alternatives); static Unrecognized.
VenueSymbolConfidence	enum: Unrecognized, Low, Medium, High.
ISharedSymbolParsers	Futures, Equities, Forex, Crypto?: the platform's grammars, handed into CreateSymbolParser. Host-implemented.
FuturesContractCode & InstrumentCanonicalKey
Type	Members
FuturesContractCode	static: const string MonthCodes = "FGHJKMNQUVXZ", char MonthCode(int month) (throws outside 1 to 12, because a placeholder character would travel into a wire symbol), bool TryMonthFromCode(char, out int), bool IsDatedContract(string?), string? RootOf(string?). The platform's single ROOT + monthCode + year grammar; year is 1, 2 or 4 digits, the root may end in a digit (SR3), and null is the single "not a dated contract" answer.
InstrumentCanonicalKey	static: string Format(VenueSymbol), string Format(VenueAssetClass, string root, int? expiryYear = null, int? expiryMonth = null), bool TryParse(string?, out VenueSymbol), VenueAssetClass AssetClassOf(string?) (the asset class a key names, Unknown for no key and for one that names none; what an order screen passes to ITradingService.GetTimeInForceChoices). Forms and parses the provider-neutral identity carried on OrderRequest.InstrumentKey / Position.InstrumentKey (future:ES, future:ES:2026-06, stock:AAPL). Stamp it here instead of referencing a host type.
Backfill composition
Type	Members
IVenueBackfillComposer	ComposedVenueBackfill Compose(VenueBackfillParts parts). Host-implemented; arrives as context.BackfillComposer.
VenueBackfillParts	record (string ProviderKey, string VenueDisplayName, IHistoricalTimeBarSource TimeBars, IHistoricalTickSource Ticks, IInstrumentMetadata Instruments, IBackfillProvider DirectBackfill, ITimeBarPeriodSupport? PeriodSupport, TimeSpan DefaultLookback, Action<string>? Log = null, IHistoricalOrderFlowBarSource? OrderFlowBars = null, INativeBarSource? NativeBars = null).
ComposedVenueBackfill	record (IBackfillProvider Backfill, bool SupportsTickDerived).
DirectVenueBackfillComposer	static Instance: the no-host fallback: the venue's uncached direct backfill verbatim, SupportsTickDerived = false.
ISeamPriorityHistoricalTickSource	Opt-in on your tick source: Task<HistoryServed<Tick>> LoadTicksUncachedForSeam(HistoryRequest request, ct) serves the history→live seam's uncached fetch on a dedicated replay lane, so it is not starved behind bulk day backfills. Same contract as LoadTicks: say how far you served.
IOpenInterestAwareTimeBarSource	Opt-in on your time-bar source: Task<BackfillResultWithOpenInterest> LoadTimeBarsWithOpenInterest(HistoryRequest request, TimeSpan period, ct) reports per-bar open interest alongside native bars. The OI-aware load bypasses the on-disk bar cache (fixed-width OHLCV cannot hold OI).
ILiveHistoryProvider	Opt-in on your backfill provider: Task<LiveHistory> LoadLive(spec, LiveHistoryRequest request, ct) loads history up to request.AsOfUtc, the one instant the chart hands over to live ticks, and returns the bars, their order flow and the FormingReach of a still-forming last bar, so the history→live handoff neither under-counts nor double-counts. LiveHandoffPoint(At, HistoricalTradesAtInstant): every tick before At is history, and so are the first HistoricalTradesAtInstant trades stamped exactly At; build it with AfterLastOf(ticks), Before(end) or Through(snapshotInstant). A decorator forwards it only when the provider it wraps serves it and says so through ProvidesLiveHistory.
LadderTimeBarPeriodSupport	Shipped ITimeBarPeriodSupport for the common venue shape: a FIXED ladder of native grains (OANDA granularities, Saxo horizons, TWS bar sizes, cTrader trendbar periods). Must be the venue's REAL native set.
AnyWholeSecondPeriodSupport	Shipped ITimeBarPeriodSupport for a venue that publishes no period ladder: every whole-second period is native, anything else cannot be served. A singleton, read through AnyWholeSecondPeriodSupport.Instance.
IInstrumentAdjustmentResolver	Optional async companion to IInstrumentMetadata: resolves + caches an instrument's corporate-action adjustment version BEFORE it is read into a cache key (the synchronous TryGetAdjustmentVersion cannot do an async per-symbol lookup). Namespace SabrTrader.Pipeline.Storage.
Depth & MBO ingest: SabrTrader.Pipeline.MarketDepth
The host's ingest plane, reached through context.Depth. Narrative: Live ticks & depth.

Type	Members
IMarketDepthHost	IHostedDepthFeed CreateDepthFeed(IDepthUpstream), IHostedMboFeed CreateMboFeed(IMboUpstream).
IHostedDepthFeed : IMarketDepthFeed, IDisposable	void Inject(in DepthUpdate), void Reset() (scoped to this feed), void Reset(string instrumentId) (one book).
IHostedMboFeed : IMarketByOrderFeed, IDisposable	void Inject(in MboEvent), void BeginReimage(string), void TouchReimage(string) (another image frame arrived), void CompleteReimage(string), void FailImage(string, string reason), void Reset(), void Reset(string instrumentId).
IDepthUpstream	void OnSubscribe(string), void OnUnsubscribe(string). No defaults, on purpose.
IMboUpstream	void OnSubscribe(string), void OnUnsubscribe(string), void Reimage(string), void FailClosed(string, string reason).
MboConnectionDiagnosticLog	Venue-facing diagnostic sink for the end-to-end MBO connection lifecycle (venue subscribe → sequencer → hub → feed → consumers), wired by the host into the activity log. Static members: Enabled, Write, Log, LogAlways, LogOnce, LogAnomalyRateLimited (rate-limited anomaly lines), ClearOneShotPrefix and Format. Namespace SabrTrader.Pipeline.MarketDepth.Hub.
Inject perf contract. Synchronous, void, by in, allocation-free, never blocks, safe from any venue thread. Upstream calls arrive on host threads and must return quickly: queue venue I/O, never perform it inline.
Venue-neutral trading components: SabrTrader.Pipeline.Venues.Trading
Shared machinery any venue composes. Narrative: The trading port.

Type	Members
OrderRevisionTracker	static string Stamp(OrderId, int revision), bool TryParse(string?, out OrderId, out int); instance int Claim(OrderId), void Rollback(OrderId, int), void Forget(OrderId), void Reset(), bool IsSupersededRevisionCancel(OrderId, string? clientReference, bool isNonExecutingCancelOrExpiry).
StreamGenerationGuard<TKey> : notnull	void Touch(TKey), Dictionary<TKey,long> Snapshot(), bool WasTouchedSince(TKey, IReadOnlyDictionary<TKey,long>), void Reset().
AmbiguousPlacementResolver	static DefaultProbeDelay (2s); Task ResolveAsync<TUpdate>(probeDelay, delay, abandoned, fetchOpenOrders, isPendingOrdersTwin, adoptVenueTruth, isStillPending, rejectAsNeverLanded, diagnostic): venue specifics arrive as delegates.
WorkingOrderProbe<TOrder,TUpdate>	ctor (snapshotCandidates, lookup, fold, abandoned, diagnostic); DefaultInterval (60s); void Start(TimeSpan?), Task RunOnceAsync(), void Dispose(). Single-flight.
PositionSnapshotReconciler	static List<Position> SelectRetirees<TKey>(tracked, venueKeyOf, reportedKeys, guard, generationsBeforeRoundTrip, keptDiagnostic), Position AsRetired(Position, DateTime). Only licensed by a COMPLETE snapshot.
OrderUpdateFold + VenueOrderReport	static FoldResult Apply(Order before, in VenueOrderReport, bool wasLocallyCancelled); FoldResult = (bool Dropped, Order After, Fill? IncrementalFill), with static FoldResult.DroppedResult for a report the fold discards. VenueOrderReport = readonly record struct (OrderState? State, decimal FilledQuantity, DateTime TimestampUtc, decimal AverageFillPrice = 0, string? VenueOrderId = null, string? RejectReason = null, decimal? LastFillQuantity = null, decimal? LastFillPrice = null, decimal LastFillCommission = 0, bool LastFillCommissionIsAuthoritative = false); the last flag becomes the emitted fill's CommissionIsAuthoritative.
OrderQuantitySnapper + OrderSizingRules	static bool TrySnap(OrderRequest, OrderSizingRules, out OrderRequest?, out string?); rules record (decimal? PriceStep, QuantityStep, MinQuantity, MaxQuantity, MinNotional) + None.
QuantityStepMath	Pure quantity-grid math for INTERACTIVE editing (a quantity stepper): round to the NEAREST step and clamp into [min, max], and move by whole increments. Distinct from OrderQuantitySnapper, which floors DOWN and REJECTS out-of-bounds: the right behaviour for PLACEMENT, the wrong one while typing.
ITradingProviderDecorator	ITradingProvider Inner { get; }.
TradingProviderCapabilityProbes	static T? AsCapability<T>(this ITradingProvider?): walks decorators.
ITimeInForceOffer	IReadOnlyList<TimeInForceChoice> TimeInForceChoices(VenueAssetClass assetClass): the time-in-force choices the venue takes for an asset class, its default first. See Optional facets.
IPositionPropagationDelay	TimeSpan PositionPropagationDelay { get; }: how long the venue's position row may lag a fill.
IPositionRefresh	Task<Position> RefreshPositionAsync(AccountId account, string instrument, ct): ask the venue for one position on the spot. A flat Position when the venue holds none; throws when the venue could not be asked.
IVenueCommissionRateSource	CommissionRate? TryGetVenueCommissionRate(AccountId account, string productRoot), IReadOnlyList<VenueCommissionRate> VenueCommissionRates(AccountId account): the venue's published per-account rates, priced ahead of the trader's settings. Thread-safe. Namespace SabrTrader.Pipeline.Venues.Trading.Commissions; see Venue-reported fees.
IOcoLegGateway	Task<OrderId> PlaceLegAsync(OrderRequest, AccountId, ct), Task<bool> CancelLegAsync(OrderId, ct), Task<bool> ResizeLegAsync(OrderId, decimal, ct), bool TryGetOrder(OrderId, out Order). Namespace SabrTrader.Pipeline.Venues.Trading.Oco.
The full order / account / position / event surface is on the Trading reference.

Print & point-value helpers
Small components a venue plugin composes in its market-data and trading code. The plugin owns and calls them; the host does not. The first three live in SabrTrader.Pipeline.Venues.Prints, the last in SabrTrader.Pipeline.Venues.Instruments.

Type	Members
DayVolumeCounter	Turns a venue's running day-volume total into the volume traded between two observations: the print size for a venue that publishes only a last-trade snapshot (level-1 quote streams, polled quotes). A snapshot carries the size of the most recent trade only, so bars built from it undercount volume; the growth of the day total counts every trade. long Advance(double dayVolume) records the latest day total and returns the volume traded after the previous observation. It returns 0 for the first observation (which only sets the baseline), for a total that falls (the venue started a session or restated its count) and for an unusable total (NaN, infinite, negative), which keeps the baseline. A fractional total is floored. void Reset() forgets the baseline: call it after a feed outage, so the volume traded while the feed was down does not land in the first live bar as one lump. Not thread-safe: one instance per symbol, called by whoever already serialises that symbol's updates.
PolledLastTrade	The print rule for a venue whose only trade data is the last trade on a polled quote. One instance per symbol. bool Observe(string lastTradeSignature, double dayVolume, out long size) folds one polled quote in: true when it is a print, which is when the last trade moved (its signature changed) or the day volume grew. size is the day volume traded after the previous poll, counted with a DayVolumeCounter. An unchanged poll is no print, so re-reading the same snapshot never paints phantom prints. A last-trade move without volume growth is a print of size 0, which keeps the price live where the venue reports no volume (an index). void Reset(): call it after a failed poll, so the volume traded while the venue could not be read is not counted as one print. Not thread-safe: the venue's single poll loop owns it.
RecentPrintIds	Remembers the last Capacity venue print ids of one symbol, so a print the venue delivers more than once is counted once. RecentPrintIds() uses const int DefaultCapacity = 512; RecentPrintIds(int capacity) sets the window. bool TryAdd(long id) is true the first time an id is seen within the window and false for a repeat delivery. The window is bounded, oldest id evicted first, so it must cover the distance between a print and its repeat. TryAdd allocates nothing. Not thread-safe: one instance per symbol, called under the owner's lock or on the symbol's single feed thread.
PointValueLookup	PointValueLookup(IInstrumentMetadata venue, IInstrumentMetadata? catalog); decimal? Of(string instrument): the point value (currency per point) of an instrument, for marking a position's P&L and pricing its commission. It asks the venue's own reference data first and the host's local instrument catalog second (VenueSessionContext.Instruments; pass null in a host without one). It answers null when neither knows the instrument, never 1: a caller that receives 1 computes P&L and risk from it without noticing. It is not the order-price tick grid: a venue snaps an order price only onto its own reported grid. Thread-safe when both sources are; a lookup allocates nothing.
Option chain SPI: SabrTrader.Pipeline.Venues.Options
The venue-side SPI, exposed as a session port and probed with AsPort<IOptionChainSource>(). The data records it serves live in SabrTrader.Pipeline.Options (package SabrTrader.Pipeline.Contracts) and are listed in the Options reference.

Type	Members
IOptionChainSource	Task<IReadOnlyList<OptionUnderlying>> SearchUnderlyingsAsync(string query, ct); Task<OptionChain> GetChainAsync(string underlyingId, IReadOnlyList<DateOnly>? expiries, ct) (+ a strikesPerExpiry overload); IDisposable SubscribeQuotes(IReadOnlyList<string> providerInstrumentIds, Action<OptionQuote> handler); string? TryGetPositionSymbol(string providerInstrumentId).
IOptionChainSource.LiveQuoteCapacity	OptionQuoteCapacity: how many live option contracts this venue can carry at once. Declare it: an undeclared venue gets the host's conservative default, which can starve a polled venue with no such limit down to a few strikes per name.
OptionQuoteCapacity · OptionQuoteCapacityKind	readonly record struct (OptionQuoteCapacityKind Kind, int MaxContracts) with static Undeclared (host applies its conservative default), Unbounded (no per-contract cost) and Bounded(maxContracts); enum Undeclared, Unbounded, Bounded.
IOptionChainSnapshotSource	Bounded chain snapshots for the screener / analytics path. Task<OptionChainSnapshot> GetChainSnapshotAsync(string underlyingId, IReadOnlyList<DateOnly>? expiries, ct), plus a request-shaped overload taking an OptionChainSnapshotRequest. The overload's default honours only Expiries; override it when your venue can serve a cheaper capture for TradedContractsOnly. Bounds the host applies itself (max expiries / contracts) are not your concern.
Trade history & cash activity: SabrTrader.Pipeline.Venues.TradeHistory
Type	Members
IVenueTradeHistorySource	bool Supports(AccountId account): true only for accounts behind THIS login; Task<VenueTradeHistoryPage> GetFillsAsync(AccountId, DateTime fromUtc, DateTime toUtc, ct): the window's fills, oldest-first, UTC. Session port; declare VenueCapabilities.TradeHistoryBackfill. Read-only: the venue reports fills, the host writes the store.
VenueTradeHistoryPage	record (IReadOnlyList<PersistedFill> Fills, bool IsComplete = true); static Empty (nothing here, authoritatively) and Partial(fills) (served part of the window; it stays uncovered so the next refresh retries it). IsComplete is the contract: a window reported complete is never requested again.
IVenueCashActivitySource	Task<IReadOnlyList<VenueCashActivity>> GetCashActivityAsync(DateTime sinceUtc, ct). Session port; declare VenueCapabilities.CashActivity.
VenueCashActivity	record (string AccountId, CashActivityKind Kind, decimal Amount, string Currency, DateTime TimestampUtc, string VenueTransactionId). Sign is NORMATIVE: positive = collected, negative = paid. VenueTransactionId is the dedup identity; a venue without one cannot expose this port.
IOrderHistorySource	bool Supports(AccountId); Task<IReadOnlyList<HistoricalOrder>> GetOrdersAsync(AccountId, DateTime fromUtc, DateTime toUtc, ct): the orders an account placed over a window, as the venue remembers them. The journal folds these into its order lifecycle for trades from before the live recorder ran. Read-only: nothing here touches the order path. Throw when the venue cannot answer; never return a partial page as complete.
HistoricalOrder · HistoricalOrderStatus	record (AccountId Account, string OrderId, string Instrument, OrderSide Side, OrderType Type, decimal Quantity, decimal? LimitPrice, decimal? StopPrice, decimal FilledQuantity, decimal? AvgFillPrice, HistoricalOrderStatus Status, DateTime PlacedUtc, DateTime? FilledUtc, DateTime? CancelledUtc, string? Tag = null); enum Unknown, Working, Filled, Cancelled, Rejected.
PersistedFill · CashActivityKind	The fill record a venue source reports and the cash-activity classification. A venue source sets PersistedFill.ExecutionId when the venue has its own fill id and CommissionIsAuthoritative when Commission is the venue's own fee: on a fill-key collision an authoritative fee replaces the stored one. See PersistedFill.
