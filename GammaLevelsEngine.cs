using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Levels;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>One projected extra (non-named) level: price in CHART units + display label.</summary>
internal readonly record struct GammaExtraLevel(double Price, string Label);

/// <summary>
/// The per-bar output of <see cref="GammaLevelsEngine.Compute"/>: named level slots
/// (NaN = absent) plus up to <see cref="GammaLevelsEngine.MaxExtraLevels"/> extra levels,
/// all already converted to chart prices. One instance is cached per session — treat as
/// immutable.
/// </summary>
internal sealed class GammaLevelsBarValues
{
    public static readonly GammaLevelsBarValues Empty = new();

    public double CallResistance { get; init; } = double.NaN;
    public double PutSupport { get; init; } = double.NaN;
    public double Hvl { get; init; } = double.NaN;
    public double ExpectedMoveHigh { get; init; } = double.NaN;
    public double ExpectedMoveLow { get; init; } = double.NaN;
    public IReadOnlyList<GammaExtraLevel> Extras { get; init; } = Array.Empty<GammaExtraLevel>();
    public GammaRegime Regime { get; init; } = GammaRegime.Unknown;
    public GammaLevelSnapshot? Source { get; init; }

    /// <summary>
    /// The index→chart price factor these values were converted with (1.0 = no conversion).
    /// Published so the strike-profile histogram scales its strikes exactly like the lines,
    /// instead of re-deriving a ratio that could drift from them.
    /// </summary>
    public double Ratio { get; init; } = 1.0;

    /// <summary>
    /// The price OFFSET applied after <see cref="Ratio"/> (0 = none). Carries the cash→futures
    /// basis when index levels are drawn on a futures chart, where the two quote on the same
    /// scale and differ by carry rather than by a factor.
    /// </summary>
    public double Offset { get; init; }
}

/// <summary>
/// The pure projection core of the Gamma Levels indicator: binds a chart instrument to a
/// connected <see cref="ILevelsFeed"/>, looks a bar's trading session up in the feed
/// (carrying the last-known snapshot forward through sessions the vendor hasn't published
/// yet), converts index-scale levels to chart prices via the snapshot's underlying price,
/// and assigns levels to fixed plot slots. Kept free of indicator lifecycle/rendering so
/// it is unit-testable in isolation.
///
/// <para><b>Price conversion.</b> When the levels were computed on a proxy underlying
/// (SPX/QQQ levels shown on an ES/NQ chart) the level prices live in the proxy's scale.
/// With auto-convert on, the engine scales by <c>chartClose / UnderlyingPrice</c>, locked
/// at the FIRST bar a snapshot is applied to a session so the lines stay put intra-session
/// instead of wobbling with the basis. Conversion only engages when the scales genuinely
/// differ (ratio beyond <see cref="ConversionMinRatio"/> in either direction) — a chart
/// trading near the underlying IS the underlying and renders the levels unscaled.</para>
/// </summary>
internal sealed class GammaLevelsEngine
{
    /// <summary>Maximum number of non-named levels projected into extra plot slots.</summary>
    public const int MaxExtraLevels = 5;

    /// <summary>
    /// Minimum ratio (either direction) before index→future conversion kicks in. Real proxy
    /// scale factors are large (ES/SPY ≈ 10×, NQ/QQQ ≈ 40×); a chart trading within this
    /// band of the underlying IS the underlying (basis/roll gaps included) and must render
    /// the levels unscaled — scaling by a near-1 ratio would silently shift every level by
    /// the basis and put walls at fictitious prices.
    /// </summary>
    public const double ConversionMinRatio = 1.5;

    private ILevelsFeed? _feed;
    private string? _vendorSymbol;
    private bool _chartIsFuturesContract;
    private ExpirySelection _expiry = ExpirySelection.VendorDefault;

    /// <summary>The hold on the feed's per-selection projection; disposing it lets the feed
    /// stop keeping that projection warm. Null whenever the default reading is in use.</summary>
    private IDisposable? _expiryHold;

    // Session-level cache: values are recomputed only when the session or its source
    // snapshot changes, so per-bar Compute is an O(1) reference return on the hot path.
    private DateOnly _cachedSession;
    private GammaLevelSnapshot? _cachedSource;
    private GammaLevelsBarValues _cached = GammaLevelsBarValues.Empty;
    private GammaLevelSnapshot? _lastApplied;

    public bool IsBound => _feed != null && _vendorSymbol != null;
    public ILevelsFeed? Feed => _feed;
    public string? VendorSymbol => _vendorSymbol;

    /// <summary>
    /// The bound feed's current per-strike profile, or null when the vendor cannot serve one.
    /// Probed through the capability flag + optional interface, never a vendor-name check, so a
    /// feed that only publishes named levels simply yields null and the histogram stays off.
    /// </summary>
    /// <summary>
    /// Resolves the CHART's close at a given UTC instant, or null when that instant lies outside
    /// the loaded bars. Load-bearing for the cash→futures basis: the vendor's underlying price is
    /// stamped at <see cref="GammaLevelSnapshot.AsOfUtc"/>, and a cash index stops printing when
    /// its session closes while the future keeps trading. Measuring the basis against the CURRENT
    /// bar would therefore fold every tick the future moved since that stamp into the "basis"
    /// (live-observed: a 70pt carry read as 165pt overnight). Both sides must be sampled at the
    /// same instant. Null resolver = no lookup available; the current bar is used as before.
    /// </summary>
    public Func<DateTimeOffset, double?>? ReferenceCloseAt { get; set; }

    public StrikeProfileSnapshot? StrikeProfile
    {
        get
        {
            var feed = _feed;
            var symbol = _vendorSymbol;
            if (feed is null || symbol is null) return null;
            if (!feed.Capabilities.HasFlag(LevelsCapabilities.StrikeProfile)) return null;
            // Under a selection the histogram MUST come from the same boards as the lines,
            // so it is read through the selection surface rather than the default one.
            if (SelectingExpiry(feed) is { } selecting)
                return selecting.GetStrikeProfile(symbol, _expiry);
            return (feed as ILevelsStrikeProfile)?.GetStrikeProfile(symbol);
        }
    }

    /// <summary>
    /// The feed's expiration-selection surface when one is BOTH advertised and actually in
    /// use. Null for the default reading, which deliberately keeps every existing consumer
    /// on the exact code path it had before selection existed.
    /// </summary>
    private ILevelsExpirySelection? SelectingExpiry(ILevelsFeed feed)
        => _expiry.Kind != ExpirySelectionKind.VendorDefault
           && feed.Capabilities.HasFlag(LevelsCapabilities.ExpirySelection)
            ? feed as ILevelsExpirySelection
            : null;

    /// <summary>The expiration selection currently bound, as the feed will report it.</summary>
    public ExpirySelection Expiry => _expiry;

    /// <summary>
    /// The feed's CURRENT snapshot for this binding, read through whichever surface is in
    /// use. Callers comparing "is this the live snapshot" must come here rather than calling
    /// the feed directly: under a selection the live snapshot is a different instance from
    /// the default reading, so a direct comparison silently answers no forever.
    /// </summary>
    public GammaLevelSnapshot? CurrentSnapshot
    {
        get
        {
            var feed = _feed;
            var symbol = _vendorSymbol;
            if (feed is null || symbol is null) return null;
            return SelectingExpiry(feed) is { } selecting
                ? selecting.GetSnapshot(symbol, _expiry)
                : feed.GetSnapshot(symbol);
        }
    }

    /// <summary>
    /// Resolve a feed covering <paramref name="instrumentId"/> from the registry and start
    /// tracking its vendor symbol. Returns false (and stays unbound) when no connected feed
    /// covers the instrument — call again on the registry's FeedsChanged.
    /// </summary>
    /// <param name="continuousFutures">True when the chart is a CONTINUOUS futures series. Its
    /// instrument id may be a venue alias ("MES", "contfut:ES") with no month-code suffix, so
    /// <see cref="LevelsSymbolMapping.IsFuturesContract"/> alone cannot see that it is a future
    /// and the cash→futures basis would silently not be applied.</param>
    /// <param name="preferredProviderKey">The vendor the user pinned for this chart
    /// (<see cref="ILevelsFeed.ProviderKey"/>); null or empty takes the registry's default.
    /// A pin no connected feed satisfies binds NOTHING rather than falling back to another
    /// vendor: drawing someone else's walls under the name the user picked is precisely the
    /// silent-wrong-vendor failure this parameter exists to end.</param>
    /// <param name="expiry">Which option expirations the levels should be computed from.
    /// <see cref="ExpirySelectionKind.VendorDefault"/> (the default) takes whatever the vendor
    /// publishes, on exactly the code path that existed before selection was added. A
    /// selection a feed cannot serve is NOT silently downgraded to the default: the feed
    /// reports what it actually produced and the caller labels it accordingly.</param>
    public bool TryBind(ILevelsRegistry registry, string instrumentId, bool continuousFutures = false,
        string? preferredProviderKey = null, ExpirySelection expiry = default)
    {
        if (registry is null || string.IsNullOrWhiteSpace(instrumentId)) return false;

        var feed = SelectFeed(registry, instrumentId, preferredProviderKey);
        var symbol = feed?.MapInstrument(instrumentId);
        if (feed is null || symbol is null) return false;

        // Release any hold from a previous binding BEFORE taking a new one. Rebinding without
        // an intervening Unbind is normal (OnFeedsChanged fires on every vendor connect and
        // disconnect), and overwriting the field instead would orphan the old hold, pinning a
        // projection in the feed for the rest of the process with nothing left to release it.
        _expiryHold?.Dispose();
        _expiryHold = null;

        _feed = feed;
        _vendorSymbol = symbol;
        _expiry = expiry;
        // A dated futures chart quotes the cash index plus carry; a cash chart does not.
        // Captured at bind time because it depends only on the instrument.
        _chartIsFuturesContract =
            continuousFutures || LevelsSymbolMapping.IsFuturesContract(instrumentId);
        Reset();

        if (SelectingExpiry(feed) is { } selecting)
            _expiryHold = selecting.Track(symbol, expiry);
        else
            feed.Track(symbol);
        return true;
    }

    /// <summary>
    /// The pinned vendor among the feeds covering the instrument, or the registry's default
    /// when nothing is pinned. Returns null for a pin that is not connected (or no longer
    /// covers the instrument) so the caller can say so instead of substituting a vendor.
    /// </summary>
    private static ILevelsFeed? SelectFeed(
        ILevelsRegistry registry, string instrumentId, string? preferredProviderKey)
    {
        if (string.IsNullOrWhiteSpace(preferredProviderKey))
            return registry.ResolveFeed(instrumentId);

        foreach (var candidate in registry.ResolveFeeds(instrumentId))
            if (string.Equals(candidate.ProviderKey, preferredProviderKey, StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }

    /// <summary>Drop the feed binding (feed disconnected); values become empty until rebound.</summary>
    public void Unbind()
    {
        // Release the projection hold FIRST: a feed keeps a per-selection projection warm for
        // as long as anyone holds it, so an indicator that unbinds without disposing would
        // make the feed pay for a selection nothing is looking at, for the rest of the session.
        _expiryHold?.Dispose();
        _expiryHold = null;
        _expiry = ExpirySelection.VendorDefault;
        _feed = null;
        _vendorSymbol = null;
        Reset();
    }

    /// <summary>Invalidate the session cache (new snapshot arrived) so the next Compute re-projects.</summary>
    public void Invalidate()
    {
        _cachedSource = null;
        _cachedSession = default;
        _lastApplied = null;
    }

    private void Reset()
    {
        _cached = GammaLevelsBarValues.Empty;
        Invalidate();
    }

    /// <summary>
    /// The projected level values for a bar in <paramref name="session"/>.
    /// <paramref name="chartClose"/> is the bar's close in chart units (used once per
    /// session to lock the conversion ratio). <paramref name="isLatestBarSession"/> marks
    /// bars of the chart's NEWEST session. Never throws; unbound/unknown → Empty.
    /// </summary>
    public GammaLevelsBarValues Compute(DateOnly session, double chartClose, bool autoConvert, bool isLatestBarSession)
    {
        var feed = _feed;
        var symbol = _vendorSymbol;
        if (feed is null || symbol is null) return GammaLevelsBarValues.Empty;

        // A selection has no per-session history: the feed keeps only the CURRENT expiration
        // board in memory, so there is nothing to slice yesterday's levels out of. Rather
        // than paint all-expiration levels onto historical bars under a label that names one
        // board, a selected reading applies to the chart's newest session and leaves history
        // blank. Blank is recoverable by switching back; a mislabelled wall is not.
        if (SelectingExpiry(feed) is { } selecting)
            return isLatestBarSession
                ? ComputeFrom(selecting.GetSnapshot(symbol, _expiry), session, chartClose, autoConvert)
                : GammaLevelsBarValues.Empty;

        var snapshot = feed.GetSnapshot(symbol, session) ?? _lastApplied;
        if (snapshot is null)
        {
            // Nothing published for this session yet and nothing to carry forward — fall back
            // to the feed's latest snapshot. An OLDER latest applies as carry-forward. A
            // NEWER latest (vendor computed levels while the market is closed — weekends,
            // holidays — and stamped the next session) applies ONLY to the chart's newest
            // session: those are today's levels for the price action the chart ends with.
            // It must never paint onto genuinely historical sessions mid-replay.
            snapshot = feed.GetSnapshot(symbol);
            if (snapshot is null) return GammaLevelsBarValues.Empty;
            if (snapshot.SessionDate > session && !isLatestBarSession) return GammaLevelsBarValues.Empty;
        }

        // Keyed on the vendor's OWN identity for the data (AsOfUtc), not on object identity: the
        // feed hands out a fresh snapshot instance on every fetch even when nothing changed, and
        // re-measuring the basis against a chart price that has moved since the stamp made the
        // conversion drift with the market (live-observed 17.03 → 27.03 in four seconds on one
        // unchanged snapshot). Measure once per published stamp, then hold it.
        return ComputeFrom(snapshot, session, chartClose, autoConvert);
    }

    /// <summary>
    /// Project one snapshot for a session, reusing the cached result while the vendor's own
    /// stamp is unchanged. Shared by the default and selected paths so the conversion is
    /// measured once per published stamp in both.
    /// </summary>
    private GammaLevelsBarValues ComputeFrom(
        GammaLevelSnapshot? snapshot, DateOnly session, double chartClose, bool autoConvert)
    {
        if (snapshot is null) return GammaLevelsBarValues.Empty;

        // Keyed on the vendor's stamp, NOT object identity: see Compute. Switching selection
        // cannot alias here because a selection change rebinds, and Reset clears this cache.
        bool sameData = _cachedSource is not null
                        && _cachedSource.AsOfUtc == snapshot.AsOfUtc
                        && session == _cachedSession;
        if (!sameData)
        {
            var (ratio, offset) = ResolveConversion(snapshot, chartClose, autoConvert, session);
            _cached = Project(snapshot, ratio, offset);
            _cachedSource = snapshot;
            _cachedSession = session;
            _lastApplied = snapshot;
        }
        return _cached;
    }

    /// <summary>
    /// Resolve how vendor prices map onto chart prices. Two genuinely different situations:
    ///
    /// <list type="bullet">
    ///   <item><b>Different scale</b> (SPY levels on ES ≈ 1:10, QQQ on NQ ≈ 1:40) — a RATIO.
    ///     Because the ratio is measured against the live chart close it already absorbs the
    ///     basis proportionally, so no extra offset is wanted.</item>
    ///   <item><b>Same scale, different instrument</b> (SPX levels on an ES chart) — an OFFSET.
    ///     A future quotes its cash index plus carry (ES ≈ SPX + ~58pt two months out), which is
    ///     an addition, not a factor; scaling by the near-1 ratio would stretch every level away
    ///     from spot instead of shifting it.</item>
    /// </list>
    ///
    /// <para>The offset is applied ONLY when all of these hold: the vendor is NOT futures-native
    /// (native levels already sit in the chart instrument's own scale, so shifting them would
    /// just bake in snapshot-vs-now drift), AND the chart is a dated futures contract (on the
    /// cash index itself that same near-1 gap IS only drift, and the levels must render at
    /// their exact vendor prices).</para>
    /// </summary>
    private (double Ratio, double Offset) ResolveConversion(
        GammaLevelSnapshot snapshot, double chartClose, bool autoConvert, DateOnly session)
    {
        if (!autoConvert) return (1.0, 0.0);
        if (snapshot.UnderlyingPrice is not (> 0 and var underlying)) return (1.0, 0.0);
        if (chartClose <= 0) return (1.0, 0.0);

        // Sample the chart at the SAME instant the vendor sampled the underlying.
        double? contemporaneous = ReferenceCloseAt?.Invoke(snapshot.AsOfUtc);
        bool canMeasureBasis = ReferenceCloseAt is null || contemporaneous is > 0;
        double reference = contemporaneous is > 0 and var c ? c : chartClose;

        double ratio = reference / underlying;
        bool genuinelyDifferentScale = ratio >= ConversionMinRatio || ratio <= 1.0 / ConversionMinRatio;
        if (genuinelyDifferentScale) return (ratio, 0.0);

        // The snapshot predates the loaded bars: an honest basis cannot be measured, and a
        // guessed one shifts every level. Draw the vendor's own prices instead.
        if (!canMeasureBasis) return (1.0, 0.0);

        bool vendorIsFuturesNative =
            _feed?.Capabilities.HasFlag(LevelsCapabilities.FuturesNative) == true;

        return _chartIsFuturesContract && !vendorIsFuturesNative
            // Settled per vendor symbol across the whole process, so two charts of one instrument
            // agree and a chart reload does not restart the estimate from a single sample.
            ? (1.0, GammaBasisEstimator.Settle(_vendorSymbol ?? string.Empty, session, reference - underlying))
            : (1.0, 0.0);
    }

    private static GammaLevelsBarValues Project(GammaLevelSnapshot snapshot, double ratio, double offset)
    {
        double callResistance = double.NaN, putSupport = double.NaN, hvl = double.NaN;
        double emHigh = double.NaN, emLow = double.NaN;
        List<GammaExtraLevel>? extras = null;

        foreach (var level in snapshot.Levels)
        {
            double price = level.Price * ratio + offset;
            switch (level.Type)
            {
                case GammaLevelType.CallResistance: callResistance = price; break;
                case GammaLevelType.PutSupport: putSupport = price; break;
                case GammaLevelType.HVL: hvl = price; break;
                case GammaLevelType.ExpectedMoveHigh: emHigh = price; break;
                case GammaLevelType.ExpectedMoveLow: emLow = price; break;
                default:
                    extras ??= new List<GammaExtraLevel>(MaxExtraLevels);
                    if (extras.Count < MaxExtraLevels)
                        extras.Add(new GammaExtraLevel(price, level.Label ?? level.Type.ToString()));
                    break;
            }
        }

        return new GammaLevelsBarValues
        {
            CallResistance = callResistance,
            PutSupport = putSupport,
            Hvl = hvl,
            ExpectedMoveHigh = emHigh,
            ExpectedMoveLow = emLow,
            Extras = (IReadOnlyList<GammaExtraLevel>?)extras ?? Array.Empty<GammaExtraLevel>(),
            Regime = snapshot.Scalars?.Regime ?? GammaRegime.Unknown,
            Source = snapshot,
            Ratio = ratio,
            Offset = offset,
        };
    }
}
