Reference
Options
The venue-agnostic options surface: the reader seam, chain and contract records, quotes, snapshots, and symbol and expected-move helpers. The data model lives in SabrTrader.Pipeline.Contracts, namespace SabrTrader.Pipeline.Options, and comes with the SDK meta-package. Narrative: Options data & trading.

On this page
The reader seam
Chain & contract data
Quotes & snapshots
Symbols
Expected move & implied spot
Context access
Trading types
Venue SPI
The reader seam
Type	Members
IOptionChainReader	The read-only, venue-agnostic entry point, obtained from a context: bool IsAvailable; event Action? AvailabilityChanged (any thread; always unsubscribe on dispose); Task<IReadOnlyList<OptionUnderlying>> SearchUnderlyingsAsync(string query, CancellationToken ct = default); Task<IOptionChainView?> OpenChainAsync(string underlyingSymbol, CancellationToken ct = default), null when no connected venue serves the symbol.
IOptionChainView	: IDisposable. An open chain bound to the (hidden) venue that resolved it; provider ids are meaningful only within the same view. string UnderlyingSymbol; OptionChain Chain (full expiry list plus the venue's default nearest expiries); Task<OptionChain> GetContractsAsync(IReadOnlyList<DateOnly> expiries, CancellationToken ct = default); Task<OptionChainSnapshot> GetSnapshotAsync(OptionChainSnapshotRequest? request = null, CancellationToken ct = default); IDisposable SubscribeQuotes(IReadOnlyList<string> providerInstrumentIds, Action<OptionQuote> onQuote). Disposing tears down the view's quote subscriptions; the view lives only as long as its venue.
OptionChainSnapshotRequest	record; bounds for one snapshot capture, every field optional (null = host default; values are clamped bounds, not guarantees): IReadOnlyList<DateOnly>? Expiries, int? MaxExpiries, int? MaxContracts (centered on the money; a near-the-money floor of about 30 contracts per expiry is kept regardless), double? MinGreekCoverage (0 to 1), TimeSpan? GreekWaitTimeout, bool TradedContractsOnly (hint: only contracts that traded today are needed).
Chain & contract data
Type	Members
OptionChain	record, required init: string UnderlyingSymbol, string UnderlyingProviderInstrumentId (empty when the venue does not report one), IReadOnlyList<DateOnly> Expiries (always complete, ascending), IReadOnlyList<OptionContract> Contracts (only the fetched expiries); void Validate().
OptionContract	record, required init: string ProviderInstrumentId, string UnderlyingSymbol, DateOnly Expiry, decimal Strike, OptionRight Right; init: OptionExerciseStyle ExerciseStyle, decimal Multiplier (default 100), string? Currency (premium currency), string? OptionClass (OCC root / trading class, null when the venue does not distinguish classes), string NativeInstrumentType (empty when the source did not classify); computed string DisplaySymbol ("AAPL 18DEC26 150 C"); void Validate().
OptionRight	enum: Call, Put.
OptionExerciseStyle	enum: Unknown, American, European.
OptionUnderlying	record (string UnderlyingId, string Symbol, string Description): one underlying-search hit. UnderlyingId is the serving venue's id; pass Symbol to OpenChainAsync.
Quotes & snapshots
Type	Members
OptionQuote	record; one PARTIAL quote update, where a null field means "unchanged". Required init: string ProviderInstrumentId; init (all decimal?): Bid, Ask, Mid, Last, Delta, Gamma, Theta, Vega, ImpliedVol (a fraction: 0.25 = 25%), Volume, OpenInterest, NetChange, PercentChange; bool? AccessDenied (true only when the venue explicitly refused market data for the instrument). OptionQuote MergedWith(OptionQuote? previous) folds this update onto the last known quote, keeping every field the update does not carry; static (decimal? Bid, decimal? Ask) Uncross(decimal? bid, decimal? ask, bool bidIsFresh, bool askIsFresh) clears the stale side of a crossed pair.
OptionChainSnapshot	record, required init: IReadOnlyList<OptionChainSnapshotEntry> Entries, IReadOnlyList<DateOnly> Expiries, string UnderlyingProviderInstrumentId, string UnderlyingSymbol; init decimal? UnderlyingPrice (spot at capture time, when reported).
OptionChainSnapshotEntry	record (OptionContract Contract, OptionQuote Quote): a complete point-in-time reading of one contract.
OptionUnderlyingAnalytics
The per-underlying analytics record behind the host's options screener and Market Analyzer options columns: required UnderlyingSymbol, CapturedUtc; optional UnderlyingPrice, Iv30, IvRankPct, IvPercentilePct, IvHistorySessions, ExpectedMove30d / ExpectedMove30dPct, FrontExpiry, ThirtyDayExpiry, TermSlope, Skew25Delta, AtmSpreadPct, OpenInterest, OptionVolume, PutCallVolumeRatio, PutCallOpenInterestRatio, VolumeToOpenInterest. A null field means "not computable from what the venue served". No SDK member returns this record; compute your own values from a snapshot and ImpliedMoveMath.

Symbols
Type	Members
OccOptionSymbol	readonly record struct (string Root, DateOnly Expiry, bool IsCall, decimal Strike): the OCC/OSI concatenated form {root}{yyMMdd}{C|P}{strike×1000, 8 digits} (e.g. AAPL240119C00190000). string ToSymbol() (also ToString()); static OccOptionSymbol Parse(string symbol), bool TryParse(string? symbol, out OccOptionSymbol result) (strict full match, so a plain equity ticker is never accepted), string? RootOf(string? symbol) (the class from either the concatenated or the space-padded OSI form; null when neither).
IndexFutureProxies	static bool TryCashIndex(string futuresRoot, out string cashIndex): the cash index whose options are an index future's options (ES/MES → SPX, NQ/MNQ → NDX, RTY/M2K → RUT, YM/MYM → DJX). Use it to map a futures chart to an option board.
OptionUnderlyingSymbols	static, namespace SabrTrader.Pipeline.Venues.OptionStrategies: Root(string?), BareRoot(string?), SameUnderlying(a, b), MatchesTypedRoot(typedQuery, hitSymbol), PreferTypedRoot(typedQuery, hits). The underlying-identity rule, so a search for "SPX" resolves to SPX and not to a look-alike.
Expected move & implied spot
Two pure helpers in SabrTrader.Pipeline.Options, the same arithmetic the platform's chain window and gamma indicators use. Usage: Expected move & implied spot.

ImpliedMoveMath
public static class ImpliedMoveMath
{
    public const double DaysPerYear = 365.0;                    // IV is quoted on CALENDAR days
    public const double MeanAbsoluteDeviationRatio = 0.7978845608028654;   // sqrt(2/pi)
    public const double StraddleShare = 0.85;                   // expected move / ATM straddle
    public static readonly TimeOnly SettlementTimeNewYork;      // 16:00
    public static DateTime SettlementUtc(DateOnly expiry);      // 16:00 New York on that date, in UTC

    public static double OneSigma(double spot, double impliedVolatility, double years);   // S * sigma * sqrt(T)
    public static double OneSigmaFromDays(double spot, double impliedVolatility, double days);
    public static double ExpectedMoveFromSigma(double oneSigma);
    public static double ExpectedMoveFromStraddle(double atmStraddlePrice);
    public static (double High, double Low) Band(double spot, double magnitude);   // Low floored at 0

    public static double YearsTo(DateOnly expiry, DateOnly today);           // whole days
    public static double YearsUntil(DateTime expiryUtc, DateTime nowUtc);
    public static double YearsUntilExpiry(DateOnly expiry, DateTime nowUtc); // to SettlementUtc(expiry)
}
impliedVolatility is a fraction (0.15 = 15%). Functions return NaN for a missing or impossible input, so a board with no IV yields no band. The expected move is narrower than a 1σ band because it is the average size of a move (sqrt(2/pi) of the standard deviation), not its root-mean-square.

ChainImpliedSpot
public static class ChainImpliedSpot
{
    public readonly record struct ParityPoint(DateOnly Expiry, decimal Strike, decimal CallMid, decimal PutMid);

    public static decimal? Compute(IReadOnlyList<ParityPoint> points);
}
The underlying price implied by the board, for when no underlying quote is available. Put/call parity (S ≈ K + C − P) at the nearest expiry and the strike where |C − P| is smallest; the result is the forward. Returns null without at least one two-sided strike, or when the best pair's gap is too wide to be a price.

A last resort. Prefer a real underlying price whenever one exists, and present a derived spot as derived, not as a traded price.
Context access
Member	Where
IIndicatorContext.OptionChains	IOptionChainReader?, null when the host serves no options data. Namespace SabrTrader.Pipeline.Indicators.
IStrategyContext.OptionChains / Strategy.OptionChains	The same reader from strategy code, plus the strategy-side trading methods PlaceMultiLegOrderAsync (two overloads), PrecheckMultiLegOrderAsync and ExerciseOptionAsync. All are default-implemented; unsupported hosts answer through the result.
OptionChainReaderAmbient	static (namespace SabrTrader.Pipeline.Indicators): Reader, SetReader(reader), IDisposable Use(reader). The host installs the reader here and the context defaults read it. Plugins use the context member; tests use Use to scope a fake reader.
Trading types
The multi-leg order model (MultiLegOrderRequest, OptionLeg, PositionEffect, NetPriceDirection, MultiLegPlacement, MultiLegPrecheckResult) and the MultiLegOrders / ExerciseOptions capability flags live in SabrTrader.Pipeline.Venues.Trading and are listed in the Trading reference. Usage: Multi-leg orders & exercise.

Venue SPI
A venue plugin serves option chains by implementing IOptionChainSource (and optionally IOptionChainSnapshotSource) from SabrTrader.Pipeline.Venues.Options, returning the records on this page, and declaring its live-quote budget with OptionQuoteCapacity. That namespace is listed in the Venue seam reference.

