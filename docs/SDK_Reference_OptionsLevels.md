Reference
Options levels
The public surface of SabrTrader.Pipeline.Levels: gamma and options-levels feeds, snapshots, strike profiles, expiration selection, the registry and ambient, and the helpers a levels vendor composes. Ships in SabrTrader.Pipeline.Contracts (bundled by SabrTrader.Sdk). Guide: Options levels.

On this page
Values & enums
Feed & registry
Building a levels venue
Strike profiles
Intraday history
Expiration selection
Values & enums
public readonly record struct GammaLevel(
    GammaLevelType Type, double Price, int Rank, string? Label, string? VendorTag);

public sealed record GammaLevelSnapshot
{
    public required string Symbol { get; init; }
    public required string ProviderKey { get; init; }
    public required DateOnly SessionDate { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<GammaLevel> Levels { get; init; }   // never null; any order
    public GammaScalars? Scalars { get; init; }
    public double? UnderlyingPrice { get; init; }     // same scale as Price; null when not supplied
    public ExpirySelection Expiry { get; init; }      // the boards these levels represent; default VendorDefault
    public LevelsFreshness? Freshness { get; init; }  // how current the vendor says it is; null when it does not say
}

public enum GammaLevelType
{
    CallResistance = 0, PutSupport = 1, HVL = 2,
    ZeroDteCallResistance = 3, ZeroDtePutSupport = 4, GexLevel = 5,
    ExpectedMoveHigh = 6, ExpectedMoveLow = 7, BlindSpot = 8,
    SwingLevel = 9, GammaScalpLevel = 10, VendorSpecific = 11,
    SecondaryCallResistance = 12, SecondaryPutSupport = 13, PinStrike = 14, MaxGammaStrike = 15,
}

public enum GammaRegime { Unknown = 0, Positive = 1, Negative = 2 }

public sealed record GammaScalars
{
    public double? Iv { get; init; }              // annualised, a fraction (0.18 = 18%)
    public double? IvRank { get; init; }          // 0..100
    public double? NetDex { get; init; }
    public double? NetGex { get; init; }          // vendor notional units, typically $ per 1% move
    public double? PutCallRatioOi { get; init; }
    public GammaRegime Regime { get; init; }      // default Unknown
    public GammaProfileState ProfileState { get; init; }   // default Unknown; where spot sits in the profile
    public bool? LevelsOutOfOrder { get; init; }  // vendor flags its own levels as out of order; null = not said
    public double? NetGexPreviousClose { get; init; }      // same units as NetGex
    public double? NetDexPreviousClose { get; init; }      // same units as NetDex
}

public enum GammaProfileState
{
    Unknown = 0, LowerExtension = 1, Negative = 2, NegativeTransition = 3,
    PositiveTransition = 4, Positive = 5, UpperExtension = 6,
}
Price is in the vendor symbol's own price scale. Rank is 0 when unranked; for GexLevel it orders the levels with 1 as the strongest, and for the Secondary* walls it is the vendor's wall rung, starting at 2. Label is an optional display label from the feed; VendorTag names the vendor family of a VendorSpecific level.

CallResistance / PutSupport are the strikes with the heaviest net call and put gamma (call and put walls). HVL is the zero-gamma flip where net dealer gamma changes sign. The ZeroDte* members are the same walls computed from same-day expiries only. ExpectedMoveHigh/Low bound the options-implied one-day move. SecondaryCallResistance / SecondaryPutSupport are the walls the vendor ranks below the headline ones: Rank is the vendor's rung, 2 being the strongest after the CallResistance / PutSupport wall, which is rung 1 and keeps its own type. PinStrike is the strike that price is expected to settle toward into expiry, and MaxGammaStrike the strike the vendor ranks as carrying the most gamma exposure, calls and puts together. BlindSpot, SwingLevel and GammaScalpLevel are vendor families carried under a neutral name; anything else is VendorSpecific with VendorTag. GammaRegime.Positive means dealer hedging dampens moves, Negative that it amplifies them.

Members are added to GammaLevelType as vendors publish new families, so treat a value you do not know the way you treat VendorSpecific, or skip it; never throw on one. The numeric values are fixed and never reused, because feeds persist snapshots with the number.

Freshness
public sealed record LevelsFreshness
{
    public static readonly TimeSpan UnconfirmedLimit;           // 30 minutes
    public required DateTimeOffset CheckedAtUtc { get; init; }  // when the vendor made this statement
    public DateTimeOffset? SourceAsOfUtc { get; init; }         // when the market data behind the levels was observed
    public DateTimeOffset? StaleAfterUtc { get; init; }         // the vendor's deadline; null = no update expected now
    public LevelsFreshnessStatus Evaluate(DateTimeOffset nowUtc);
    public TimeSpan? AgeAt(DateTimeOffset nowUtc);               // never negative; null without SourceAsOfUtc
}

public enum LevelsFreshnessStatus { Current = 0, Stale = 1, NoUpdatesExpected = 2 }
A feed advertising LevelsCapabilities.Freshness stamps each snapshot with what its vendor reports, and restamps the current snapshot on every successful refresh, also when the levels did not change. Evaluate turns that into a verdict at the moment of use: Stale once the deadline has passed (the vendor stopped publishing or the feed stopped fetching), Current before it, and NoUpdatesExpected when there is no deadline because the market is closed. A "no updates expected" statement holds for UnconfirmedLimit after CheckedAtUtc; once nobody has confirmed it for that long, the feed has lost the vendor and the reading is Stale. Call Evaluate when you draw or report, never store its answer: the same snapshot is current at 10:00 and stale at 10:20. AgeAt measures from the market observation, which is what shows a delayed data feed: it publishes on time, from data minutes old.

Trading session
public static class LevelsSessionCalendar
{
    public static DateOnly SessionDateOf(DateTimeOffset instant);   // its US Eastern calendar date
}
The session an instant belongs to is its New York calendar date. Stamp GammaLevelSnapshot.SessionDate with it, pass it as the session to ExpirySelection.Resolve, and compare your bars against it, so your feed agrees with every other feed and consumer on what "today" and "0DTE" mean. It is not the futures settlement day, which rolls at 17:00.

Feed & registry
public interface ILevelsFeed
{
    string ProviderKey { get; }                 // stable lower-case vendor key
    string DisplayName { get; }
    LevelsCapabilities Capabilities { get; }
    int HistoryDays { get; }                    // 0 without LevelsCapabilities.History
    event Action<string>? SnapshotChanged;      // vendor symbol; arbitrary thread
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync();
    void Track(string symbol);                  // idempotent; safe before connect
    GammaLevelSnapshot? GetSnapshot(string symbol);
    GammaLevelSnapshot? GetSnapshot(string symbol, DateOnly sessionDate);
    string? MapInstrument(string instrumentId); // platform instrument id to vendor symbol, or null
}

public interface ILevelsRegistry
{
    IReadOnlyList<ILevelsFeed> Feeds { get; }
    event Action? FeedsChanged;                 // arbitrary thread
    ILevelsFeed? ResolveFeed(string instrumentId);                   // first covering feed, connect order
    IReadOnlyList<ILevelsFeed> ResolveFeeds(string instrumentId);   // every covering feed
}

public static class LevelsAmbient
{
    public static ILevelsRegistry Registry { get; }           // never null
    public static void SetRegistry(ILevelsRegistry? registry); // host: install (null restores the empty default)
    public static IDisposable Use(ILevelsRegistry registry);   // tests: scoped install
}

[Flags]
public enum LevelsCapabilities
{
    None = 0, NamedLevels = 1, FuturesNative = 2, Intraday = 4, History = 8, StrikeProfile = 16,
    ExpirySelection = 32, IntradayHistory = 64,
    WallLadder = 128, PinStrike = 256, MaxGammaStrike = 512, Freshness = 1024,
    StrikeSeries = 2048,
}

public sealed class NullLevelsRegistry : ILevelsRegistry   // the ambient's empty default
{
    public static NullLevelsRegistry Instance { get; }
    public IReadOnlyList<ILevelsFeed> Feeds { get; }
    public event Action? FeedsChanged;
    public ILevelsFeed? ResolveFeed(string instrumentId);
    public IReadOnlyList<ILevelsFeed> ResolveFeeds(string instrumentId);
}

public static class LevelsSymbolMapping
{
    public static bool IsFuturesContract(string instrumentId);   // dated futures contract vs cash instrument
    public static string? NormalizeToRoot(string instrumentId);  // "rithmic:MNQU6" -> "NQ"
}
NormalizeToRoot strips the provider prefix, a contract-month suffix and a leading slash, and maps micro contracts to their parent (MNQ to NQ, MES to ES) so a micro chart picks up the parent's levels. GetSnapshot and MapInstrument are lock-free and do no I/O.

WallLadder, PinStrike and MaxGammaStrike declare that the feed publishes those level families, and Freshness that it stamps GammaLevelSnapshot.Freshness. A consumer reads the flags to decide what to offer before the first reading lands (the Gamma Levels toolbar lists a row per advertised family and none for a feed that does not publish it), and reads the snapshot for the values themselves. A flag is a promise about the feed, not about every snapshot: a value the vendor could not resolve is simply absent, and a reading sliced by expiration carries only what the vendor computes for that slice. Families without a flag (expected move, 0DTE walls, ranked GEX levels) are discovered from the snapshots.

Building a levels venue
A levels vendor is a venue plugin like any other: VenueCategory.Levels on its manifest, VenueCapabilities.Levels declared, and one ILevelsFeed published as a session port. The session has the same shape for every vendor, so the SDK ships it as LevelsVenueSession (namespace SabrTrader.Pipeline.Venues) and a levels plugin is a descriptor plus a feed.

public sealed class LevelsVenueSession : IVenueSession
{
    public LevelsVenueSession(
        string displayName,
        Func<ILevelsFeed> feedFactory,          // called once per connect attempt
        IVenueNoticeSink? notices = null);

    public IDataProvider? Data { get; }         // null: a levels venue serves no charts
    public ITradingProvider? Trading { get; }   // null: and routes no orders

    public T? GetPort<T>() where T : class;     // resolves ILevelsFeed while Connected
}
public IVenueSession CreateSession(VenueSessionContext context)
{
    string apiKey = context.Settings["ApiKey"];
    Func<ILevelsFeed> factory = () => new MyLevelsFeed(apiKey, context.Notices.Log);
    return new LevelsVenueSession(displayName: "My Levels", feedFactory: factory, notices: context.Notices);
}
Two behaviours to know. The feed is built INSIDE ConnectAsync, not at session creation: vendor feed constructors validate credentials and throw on a blank key, and the host must be able to create and abandon a session from any profile without a throw. The port resolves only while Connected. Connect failures are reported as non-permanent, because a levels vendor cannot tell a rejected key from a service hiccup, so the host's auto-retry keeps trying.
LevelsFetchOutcomeReporter
Reports the fetch outcome that would otherwise pass silently: the vendor answered, nothing threw, and there was no snapshot to publish. Compose it in your fetch loop so the notice reads the same for every vendor.

public sealed class LevelsFetchOutcomeReporter
{
    public LevelsFetchOutcomeReporter(string vendor, Action<string> log, Func<DateTimeOffset> clock);

    public void ServedNothing(string symbol, GammaLevelSnapshot? showing);  // vendor answered with nothing
    public void Published(string symbol);                                   // publishing again
    public void Reset();                                                    // on disconnect
}
Call ServedNothing instead of returning quietly when a completed fetch produced no snapshot, passing the snapshot consumers still see (restored from your cache, or null). A fetch that threw is logged by its own handler and does not come here. The reporter writes one line on the transition into the condition and one on recovery (Published), not one per refresh cycle, because the activity log is customer-facing. The line states how old the reading still on screen is, measured with clock.

Strike profiles
public interface ILevelsStrikeProfile
{
    StrikeProfileSnapshot? GetStrikeProfile(string symbol);   // lock-free, no I/O
}

public sealed record StrikeProfileSnapshot
{
    public required string Symbol { get; init; }
    public required string ProviderKey { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<StrikeExposure> Strikes { get; init; }   // no guaranteed order
    public double? UnderlyingPrice { get; init; }             // same scale as Strike
    public ExpirySelection Expiry { get; init; }
    public IReadOnlyList<TimeSpan> PriorHorizons { get; init; }   // e.g. [1m, 5m, 10m, 30m]; empty when unused
}

public readonly record struct StrikeExposure(double Strike, double NetGex, double CallGex, double PutGex)
{
    public double? OpenInterestGex { get; init; }        // exposure from OPEN INTEREST, not traded volume
    public double? OpenInterestContracts { get; init; }  // open interest in CONTRACTS
    public IReadOnlyList<double>? Priors { get; init; }  // NetGex at each PriorHorizons entry, same order
    public double? NetDex { get; init; }                 // net delta exposure
    public double? NetOpenInterest { get; init; }        // call minus put open interest, CONTRACTS
    public double? NetVolume { get; init; }              // call minus put traded volume, CONTRACTS
    public double? VolumeGex { get; init; }              // gamma exposure from traded volume, vendor's own scale
    public double? ValueOf(StrikeSeries series);         // the member a series names; null when absent
}

public enum StrikeSeries { NetGex = 0, NetDex = 1, NetOpenInterest = 2, NetVolume = 3, VolumeGex = 4 }

public interface ILevelsStrikeSeries   // advertised by LevelsCapabilities.StrikeSeries
{
    IReadOnlyList<StrikeSeries> AvailableSeries { get; }   // besides NetGex; fixed for the feed's life
    IDisposable Track(string symbol, StrikeSeries series);  // keep it on the symbol's profiles until disposed
}
Three readings, kept apart. NetGex is signed (positive: dealers long gamma at this strike) and reflects the day's flow, so it moves continuously; OpenInterestGex reflects the cleared position and steps once a day. They disagree by design, so draw the second as a SEPARATE mark. OpenInterestContracts is a count, not a notional; label it as such. Priors is the same strike's NetGex at each PriorHorizons entry, in the same order and length, so a strike that is building reads differently from one that is decaying. Label what you draw with its horizon.
Series on request. NetDex, NetOpenInterest, NetVolume and VolumeGex are filled only while a consumer holds them through ILevelsStrikeSeries.Track: a vendor may charge a request per series per board, so it fetches only what someone draws. The first profile carrying a newly held series follows the feed's next refresh, announced through SnapshotChanged. Each series is its own measurement: scale it on its own and never add VolumeGex to NetGex.
Intraday history
Optional capability on an ILevelsFeed: the snapshots this session already published, so a consumer can draw where the levels WERE. Advertise LevelsCapabilities.IntradayHistory AND implement the interface; consumers gate on the flag and cast.
public interface ILevelsIntradayHistory
{
    // Current session only, oldest first, empty when nothing is published yet.
    // Lock-free: callable from a chart render thread.
    IReadOnlyList<GammaLevelSnapshot> IntradayHistory(string symbol, ExpirySelection selection);
}

// Vendor helper: the session's changes per symbol and selection.
public sealed class IntradayLevelHistory
{
    public const int DefaultCapacity = 2000;

    public IntradayLevelHistory(int capacity = DefaultCapacity);

    public bool Record(string symbol, ExpirySelection selection, GammaLevelSnapshot snapshot);
    public IReadOnlyList<GammaLevelSnapshot> Read(string symbol, ExpirySelection selection);
    public void Drop(string symbol, ExpirySelection selection);
}
Record returns false for a reading whose levels equal the last kept one: only CHANGES are stored, because a vendor re-stamps its calculation on every publish while the levels sit still. A snapshot for a new trading session drops the older session; earlier sessions come from ILevelsFeed.GetSnapshot(symbol, sessionDate). Each (symbol, selection) keeps at most capacity entries. Entries are immutable arrays swapped by reference, so writers never block readers. Drop forgets a selection nothing holds any more.

Expiration selection
Implement ILevelsExpirySelection when your vendor can compute its levels from a chosen subset of option expirations, and advertise LevelsCapabilities.ExpirySelection. A vendor that publishes pre-computed levels leaves both out.
public interface ILevelsExpirySelection
{
    IReadOnlyList<DateOnly> GetAvailableExpiries(string symbol);   // ascending; empty before the first fetch
    IDisposable Track(string symbol, ExpirySelection selection);   // hold until disposed
    GammaLevelSnapshot? GetSnapshot(string symbol, ExpirySelection selection);
    StrikeProfileSnapshot? GetStrikeProfile(string symbol, ExpirySelection selection);   // null without StrikeProfile
}

public enum ExpirySelectionKind
{
    VendorDefault = 0, AllExpiries, Nearest, WithinDays, MonthlyOpex, OnDate, NthExpiry, NthWeekly,
}

public readonly record struct ExpirySelection
{
    public ExpirySelectionKind Kind { get; }
    public int Days { get; }        // WithinDays horizon, or the 1-based ordinal for NthExpiry / NthWeekly
    public DateOnly Date { get; }   // OnDate

    public static ExpirySelection VendorDefault { get; }   // default(ExpirySelection)
    public static ExpirySelection AllExpiries { get; }
    public static ExpirySelection Nearest { get; }
    public static ExpirySelection ZeroDte { get; }        // WithinDays(0)
    public static ExpirySelection MonthlyOpex { get; }    // third-Friday board (Thursday on a holiday)
    public static ExpirySelection WithinDays(int days);   // cumulative; throws when negative
    public static ExpirySelection On(DateOnly date);
    public static ExpirySelection NthExpiry(int ordinal);   // 1-based; 1 == Nearest
    public static ExpirySelection NthWeekly(int ordinal);   // 1-based, counted by WEEK

    public IReadOnlyList<DateOnly> Resolve(IReadOnlyCollection<DateOnly> available, DateOnly session);
    public string ToToken();
    public static bool TryParse(string? token, out ExpirySelection selection);
}

// Vendor helper: which selections are live per symbol, reference counted.
public sealed class ExpirySelectionTracker
{
    public IDisposable Track(string symbol, ExpirySelection selection, Action? onFirstHold = null);
    public IReadOnlyList<ExpirySelection> Selections(string symbol);
    public IReadOnlyList<string> Symbols();
    public event Action<string, ExpirySelection>? SelectionReleased;   // last holder gone
}
NthExpiry(n) is the nth open board by date (1 is Nearest, 2 is the board after the front one), which lets a chart look past a 0DTE expiry without pinning a date that is wrong tomorrow. NthWeekly(n) is the last expiration in each of the next n calendar weeks; counting by WEEK keeps "2nd weekly" stable on a chain that also lists Monday and Wednesday boards. Both are 1-based and throw below 1. ToToken and TryParse round-trip a selection as a short string for settings.

ExpirySelectionTracker.Track returns a disposable hold and runs onFirstHold the first time a (symbol, selection) pair is held: the cue to start computing it. When the last holder disposes, SelectionReleased fires and you drop that projection. Implement your ILevelsExpirySelection.Track on top of it rather than your own dictionary, so a user stepping through the expiration menu does not leave a projection behind per click.

Intent, not mechanism. A selection says WHICH boards the caller wants, not how you get them. A vendor whose endpoint returns every expiration satisfies it by filtering what it holds; a vendor with a per-expiration endpoint fetches that slice. Fetching the whole chain is not cheap for every vendor.
One rule for the boards. Resolve is the single implementation of what "0 DTE" or "monthly OPEX" means, and every feed calls it. Pass the session date in rather than reading a clock, so the answer stays deterministic. Expirations before the session never contribute, and an unsatisfiable selection resolves to an empty list.
Say what you actually produced. Stamp GammaLevelSnapshot.Expiry and StrikeProfileSnapshot.Expiry with the selection the numbers REALLY represent, not the one you were asked for. A feed that cannot slice reports VendorDefault and consumers label the line plainly; a full-chain wall under a "0 DTE" label is a trading error, so the snapshot is the authority. A selection you cannot satisfy publishes an EMPTY level set that still states its selection; a neighbouring board is never a substitute. If you also advertise StrikeProfile, the profile honours the same selection, so a histogram and the lines drawn over it describe the same boards.
Track is the lifetime. Do the work a selection costs inside Track, on your own thread, and keep it warm until the handle is disposed. Both GetSnapshot overloads stay lock-free and cheap enough for the chart render thread, so serve a precomputed projection. Drop a projection once no handle holds it. Disposing twice, or after disconnect, is normal teardown and must be safe.
