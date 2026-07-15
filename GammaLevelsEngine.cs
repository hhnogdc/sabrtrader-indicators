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
    /// Resolve a feed covering <paramref name="instrumentId"/> from the registry and start
    /// tracking its vendor symbol. Returns false (and stays unbound) when no connected feed
    /// covers the instrument — call again on the registry's FeedsChanged.
    /// </summary>
    public bool TryBind(ILevelsRegistry registry, string instrumentId)
    {
        if (registry is null || string.IsNullOrWhiteSpace(instrumentId)) return false;

        var feed = registry.ResolveFeed(instrumentId);
        var symbol = feed?.MapInstrument(instrumentId);
        if (feed is null || symbol is null) return false;

        _feed = feed;
        _vendorSymbol = symbol;
        Reset();
        feed.Track(symbol);
        return true;
    }

    /// <summary>Drop the feed binding (feed disconnected); values become empty until rebound.</summary>
    public void Unbind()
    {
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

        if (!ReferenceEquals(snapshot, _cachedSource) || session != _cachedSession)
        {
            double ratio = ResolveRatio(snapshot, chartClose, autoConvert);
            _cached = Project(snapshot, ratio);
            _cachedSource = snapshot;
            _cachedSession = session;
            _lastApplied = snapshot;
        }
        return _cached;
    }

    private static double ResolveRatio(GammaLevelSnapshot snapshot, double chartClose, bool autoConvert)
    {
        if (!autoConvert) return 1.0;
        if (snapshot.UnderlyingPrice is not (> 0 and var underlying)) return 1.0;
        if (chartClose <= 0) return 1.0;

        double ratio = chartClose / underlying;
        bool genuinelyDifferentScale = ratio >= ConversionMinRatio || ratio <= 1.0 / ConversionMinRatio;
        return genuinelyDifferentScale ? ratio : 1.0;
    }

    private static GammaLevelsBarValues Project(GammaLevelSnapshot snapshot, double ratio)
    {
        double callResistance = double.NaN, putSupport = double.NaN, hvl = double.NaN;
        double emHigh = double.NaN, emLow = double.NaN;
        List<GammaExtraLevel>? extras = null;

        foreach (var level in snapshot.Levels)
        {
            double price = level.Price * ratio;
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
        };
    }
}
