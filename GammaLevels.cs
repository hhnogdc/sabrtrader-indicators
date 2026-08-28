using System;
using System.Collections.Generic;
using System.Linq;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Levels;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Gamma Levels — options-dealer positioning levels (call wall, put wall, gamma flip/HVL,
/// expected move, ranked GEX strikes) on the price chart, fed by a connected Levels
/// provider (Connections → Levels, e.g. FlashAlpha). Levels step per trading session:
/// historical bars show the levels that applied to THEIR session, so past reactions are
/// reviewable. When the provider computes levels on an index/ETF proxy (SPX/QQQ for an
/// ES/NQ chart) the prices are auto-converted to chart scale via the snapshot's underlying
/// price. The chart shades the gamma-flip pivot zone (green above the flip = dealers dampen
/// moves, red below = dealers amplify them), fading with distance from the flip.
/// </summary>
[IndicatorDescription("Options dealer-positioning levels from a connected Levels provider (Connections → Levels): call wall (resistance), put wall (support), gamma flip / HVL (volatility regime pivot), expected move band and additional vendor levels, stepped per trading session. Above the flip dealers dampen moves; below it they amplify them.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class GammaLevels : IndicatorBase,
    IChartCustomRender, IChartBackgroundRender, IIndicatorToolbarMenu, IRepaintNotifier, IRecalculateNotifier
{
    private static readonly TimeZoneInfo? Eastern = ResolveEastern();

    private readonly GammaLevelsEngine _engine = new();
    private readonly ChunkedSeries<double> _callWall = new(), _putWall = new(), _hvl = new();
    private readonly ChunkedSeries<double> _emHigh = new(), _emLow = new();
    private readonly ChunkedSeries<double>[] _extras;
    private ILevelsRegistry? _registry;
    private ILevelsFeed? _subscribedFeed;
    private GammaLevelsBarValues _latest = GammaLevelsBarValues.Empty;
    private string _levelsProvider = string.Empty;
    private string _levelExpiry = string.Empty;

    /// <summary>
    /// The registry to read from. <see cref="_registry"/> is claimed (and hooked for late
    /// connects) by <see cref="BindFeed"/> on the indicator's own thread; the toolbar menu
    /// runs on the UI thread and only READS, so it falls back to the same ambient instance
    /// rather than racing that hook.
    /// </summary>
    private ILevelsRegistry ActiveRegistry => _registry ?? LevelsAmbient.Registry;

    [IndicatorParameter(DisplayName = "Convert index levels",
        Description = "When the provider computes levels on an index/ETF proxy (QQQ for NQ, SPY for ES), scale them to chart prices using the snapshot's underlying price. Leave on unless the provider serves native futures levels.",
        Group = "Levels", Order = 0, Primary = true)]
    public bool AutoConvert { get; set; } = true;

    [IndicatorParameter(DisplayName = "Show extra levels",
        Description = "Also plot the provider's additional levels (ranked GEX strikes, 0DTE magnet, max OI, …) as thin gray lines next to the three primary levels.",
        Group = "Levels", Order = 1)]
    public bool ShowExtras { get; set; } = true;

    [IndicatorParameter(DisplayName = "Levels provider",
        Description = "Which connected Levels vendor feeds this chart, by provider key. Empty = whichever covering provider connected first. Pick it from the chart's Gamma toolbar menu, which lists the vendors connected right now.",
        Group = "Levels", Order = 2,
        // The vendor list is whatever is connected at this moment, so a static settings-dialog
        // row cannot render it; the toolbar menu is the single editing surface.
        Editor = ParameterEditorKind.Hidden,
        ContentKeyDefault = "")]
    public string LevelsProvider
    {
        get => _levelsProvider;
        // Restored values arrive from chart templates, workspaces and declarative overrides,
        // and the catalog's coercion passes a JSON null straight through. Empty is the value
        // that MEANS Auto, so normalise here once instead of null-checking every read.
        set => _levelsProvider = value ?? string.Empty;
    }

    [IndicatorParameter(DisplayName = "Expiration",
        Description = "Which option expirations the levels are computed from. Empty = the provider's own reading, which for a full-chain provider sums every expiration. Pick it from the chart's Gamma toolbar menu, which lists the boards the provider is serving right now. Only providers that can slice by expiration offer it.",
        Group = "Levels", Order = 3,
        // Same reason as LevelsProvider: the live board list cannot be rendered by a static
        // settings-dialog row, so the toolbar menu is the single editing surface.
        Editor = ParameterEditorKind.Hidden,
        ContentKeyDefault = "")]
    public string LevelExpiry
    {
        get => _levelExpiry;
        // Restored values arrive from chart templates and workspaces, where a JSON null
        // passes straight through the catalog's coercion. Empty is the value that MEANS
        // the provider default, so normalise once here rather than at every read.
        set => _levelExpiry = value ?? string.Empty;
    }

    [IndicatorParameter(DisplayName = "Show labels",
        Description = "Name each level at the right edge of the chart.",
        Group = "Display", Order = 10)]
    public bool ShowLabels { get; set; } = true;

    [IndicatorParameter(DisplayName = "Label size", MinValue = 8, MaxValue = 24, Step = 1,
        Description = "Label font size in pixels.",
        Group = "Display", Order = 11)]
    public double LabelSize { get; set; } = 11;

    [IndicatorParameter(DisplayName = "Regime background",
        Description = "Shade the gamma-flip pivot zone: green above the flip (dealers dampen moves), red below it (dealers amplify moves), fading with distance from the flip line.",
        Group = "Display", Order = 12)]
    public bool ShowRegimeBackground { get; set; } = true;

    [IndicatorParameter(DisplayName = "Regime opacity", MinValue = 2, MaxValue = 60, Step = 1,
        Description = "Peak alpha (0-255) of the regime shading right at the gamma flip; it fades to transparent away from the flip.",
        Group = "Display", Order = 13)]
    public int RegimeOpacity { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Expected move fill",
        Description = "Shade the band between the expected-move high and low (when the provider supplies them).",
        Group = "Display", Order = 14)]
    public bool ShowExpectedMoveFill { get; set; } = true;

    [IndicatorParameter(DisplayName = "Level glow",
        Description = "Soft glow behind the call wall, put wall and gamma flip across the current session, so the active levels stand out from historical steps.",
        Group = "Display", Order = 15)]
    public bool ShowGlow { get; set; } = true;

    [IndicatorParameter(DisplayName = "GEX profile histogram",
        Description = "Draw the provider's per-strike gamma exposure as a histogram along the right edge: bar length is the exposure at that strike. Shows WHERE gamma is concentrated, which the level lines alone cannot. Only providers that publish a strike profile supply it.",
        Group = "GEX profile", Order = 20, Primary = true)]
    public bool ShowStrikeProfile { get; set; } = true;

    [IndicatorParameter(DisplayName = "Split calls / puts",
        Description = "Split the histogram down the middle: call gamma grows left, put gamma grows right, both scaled against the same peak. Shows strikes where heavy calls AND heavy puts sit together — those net out to nothing in the combined view. Off = one bar per strike for net gamma.",
        Group = "GEX profile", Order = 21)]
    public bool SplitCallPut { get; set; } = true;

    [IndicatorParameter(DisplayName = "Profile width %", MinValue = 5, MaxValue = 40, Step = 1,
        Description = "Width of the histogram as a percentage of the chart's plot area.",
        Group = "GEX profile", Order = 22)]
    public double ProfileWidthPercent { get; set; } = 18;

    [IndicatorParameter(DisplayName = "Profile opacity", MinValue = 10, MaxValue = 255, Step = 5,
        Description = "Alpha (0-255) of the histogram bars.",
        Group = "GEX profile", Order = 23)]
    public int ProfileOpacity { get; set; } = 110;

    [IndicatorParameter(DisplayName = "Regime badge",
        Description = "Compact badge in the top-right corner naming the current dealer-gamma regime (positive = volatility-dampening, negative = volatility-amplifying).",
        Group = "Display", Order = 16)]
    public bool ShowRegimeBadge { get; set; } = true;

    public GammaLevels(IIndicator? parent = null, ISeries<double>? input = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        var levelSemantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("Call Wall", _callWall, PlotStyle.Line, PlotColors.Crimson, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("Put Wall", _putWall, PlotStyle.Line, PlotColors.LimeGreen, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("Gamma Flip", _hvl, PlotStyle.Line, PlotColors.Gold, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("EM High", _emHigh, PlotStyle.Dashed, PlotColors.SteelBlue, 1.0) { Semantics = levelSemantics });
        AddPlot(new Plot("EM Low", _emLow, PlotStyle.Dashed, PlotColors.SteelBlue, 1.0) { Semantics = levelSemantics });

        _extras = new ChunkedSeries<double>[GammaLevelsEngine.MaxExtraLevels];
        for (int i = 0; i < _extras.Length; i++)
        {
            _extras[i] = new ChunkedSeries<double>();
            AddPlot(new Plot($"Level {i + 1}", _extras[i], PlotStyle.Dotted, PlotColors.Gray, 1.0) { Semantics = levelSemantics });
        }
    }

    /// <summary>Test seam: the values applied to the most recent bar.</summary>
    internal GammaLevelsBarValues LatestValues => _latest;

    /// <inheritdoc />
    public event Action? RepaintRequested;

    /// <inheritdoc />
    public event Action? RecalculateRequested;

    /// <inheritdoc />
    public void RequestRepaint() => RepaintRequested?.Invoke();

    // ──────────────────────────────────── toolbar dropdown (IIndicatorToolbarMenu)

    /// <summary>Menu labels resolve at BuildMenu time — the host rebuilds the dropdown per
    /// open, so a language switch reaches it without a restart. Never cache these.</summary>
    private static string T(string slug, string english)
        => SabrTrader.Localization.Loc.TOr("Indicator.GammaLevels.Toolbar." + slug, english);

    /// <inheritdoc />
    public string MenuTitle => T("MenuTitle", "Gamma");

    /// <inheritdoc />
    public event Action? MenuChanged;

    private void RaiseMenuChanged() => MenuChanged?.Invoke();

    private ToolbarCheckItem Toggle(string slug, string english, Func<bool> get, Action<bool> set)
        => new(T(slug, english), get, v => { set(v); RaiseMenuChanged(); RequestRepaint(); });

    /// <summary>Show/hide one plot slot by index — the per-level toggles.</summary>
    private ToolbarCheckItem PlotToggle(int index, string label)
        => new(label,
            () => Plots[index].Visible,
            v => { Plots[index].Visible = v; RaiseMenuChanged(); RequestRepaint(); });

    /// <summary>
    /// One entry per level the provider actually publishes, so each line can be switched off
    /// individually. The vendor extras are listed under the names they currently carry
    /// ("Max Pain", "Charm Flip", …) rather than "Level 1…5" — see
    /// <see cref="SyncExtraPlotNames"/>; slots the current snapshot does not fill are omitted
    /// instead of shown as dead rows.
    /// </summary>
    private IReadOnlyList<ToolbarMenuItem> BuildLevelItems()
    {
        var items = new List<ToolbarMenuItem>(5 + _extras.Length)
        {
            PlotToggle(0, T("CallWall", "Call Wall")),
            PlotToggle(1, T("PutWall", "Put Wall")),
            PlotToggle(2, T("GammaFlip", "Gamma Flip")),
            PlotToggle(3, T("EmHigh", "EM High")),
            PlotToggle(4, T("EmLow", "EM Low")),
        };

        int extras = Math.Min(_latest.Extras.Count, _extras.Length);
        if (extras > 0) items.Add(new ToolbarSeparator());
        for (int i = 0; i < extras; i++)
            items.Add(PlotToggle(5 + i, Plots[5 + i].Name));   // vendor-supplied family name

        return items;
    }

    /// <summary>
    /// The chart's instrument, or null before the init pass has run. The host builds this
    /// menu as soon as the indicator is ATTACHED, which is earlier than "initialised":
    /// <c>ChartIndicatorRegistry.AddDeferred</c> (workspace restore, in-place reload) raises
    /// PlotsChanged and hands the init pass to a scheduler, and an inert premium indicator
    /// never gets an init pass at all. Everything the menu reads off the chart has to cope
    /// with that.
    /// </summary>
    private string? ChartInstrumentId => Bars is { } bars ? bars.Spec.InstrumentId : null;

    /// <summary>
    /// The vendor picker: "Auto" plus every connected Levels feed that covers THIS chart's
    /// instrument. Vendors overlap on purpose (FlashAlpha and QuantData both serve the whole
    /// US board), and the registry can only answer with the earliest-connected one, so which
    /// vendor a chart reads has to be the user's call. Rebuilt per open, so the rows are what
    /// is connected right now.
    ///
    /// <para>Before the init pass there is no instrument to resolve vendors against, so the
    /// group is the Auto row plus the user's pin. <see cref="BindFeed"/> raises
    /// <see cref="MenuChanged"/> once the chart's instrument is known, which rebuilds this
    /// with the real vendor rows.</para>
    /// </summary>
    private ToolbarRadioGroup BuildProviderGroup()
    {
        var options = new List<ToolbarRadioOption>(4)
        {
            new(T("ProviderAuto", "Auto (first connected)"),
                () => LevelsProvider.Length == 0,
                () => SelectProvider(string.Empty)),
        };

        string pinned = LevelsProvider;
        bool pinnedIsConnected = false;
        foreach (var feed in ChartInstrumentId is { } instrumentId
                     ? ActiveRegistry.ResolveFeeds(instrumentId)
                     : [])
        {
            string key = feed.ProviderKey;
            pinnedIsConnected |= string.Equals(key, pinned, StringComparison.OrdinalIgnoreCase);
            options.Add(new ToolbarRadioOption(feed.DisplayName,
                () => string.Equals(LevelsProvider, key, StringComparison.OrdinalIgnoreCase),
                () => SelectProvider(key)));
        }

        // A pin the user can still SEE is a pin they can undo. Dropping the row would leave a
        // chart with no levels and nothing on screen saying why.
        if (pinned.Length > 0 && !pinnedIsConnected)
            options.Add(new ToolbarRadioOption(
                $"{pinned} ({T("ProviderNotConnected", "not connected")})",
                () => true, () => { }, IsEnabled: false));

        return new ToolbarRadioGroup(T("Provider", "Levels provider :"), options);
    }

    /// <summary>
    /// Pin a Levels vendor for this chart (empty key = back to Auto) and rebind at once. The
    /// previous feed is dropped unconditionally: leaving its cached walls on screen after the
    /// user chose a different vendor is the same wrong-vendor lie from the other side.
    /// </summary>
    private void SelectProvider(string providerKey)
    {
        if (string.Equals(LevelsProvider, providerKey, StringComparison.OrdinalIgnoreCase)) return;
        LevelsProvider = providerKey;

        UnsubscribeFeed();
        _engine.Unbind();
        if (_engine.TryBind(ActiveRegistry, Bars.Spec.InstrumentId, IsContinuousFuturesChart, providerKey, SelectedExpiry))
            SubscribeFeed(_engine.Feed!);

        RaiseMenuChanged();
        RecalculateRequested?.Invoke();
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolbarMenuItem> BuildMenu()
    {
        var items = new List<ToolbarMenuItem> { BuildProviderGroup() };
        // Only vendors that can actually slice get the row; the rest keep today's menu.
        if (BuildExpiryGroup() is { } expiry) items.Add(expiry);
        items.AddRange(CommonMenuItems());
        return items;
    }

    /// <summary>
    /// The expiration picker for the bound vendor, or null when it cannot slice by
    /// expiration. Rebuilt per open so the dated rows are the boards live right now.
    /// </summary>
    private ToolbarRadioGroup? BuildExpiryGroup()
        => GammaLevelsExpiryMenu.Build(
            _engine.Feed, _engine.VendorSymbol, LevelExpiry, MenuSessionDate, SelectExpiry, T);

    /// <summary>
    /// The session the expiration menu counts days-to-expiry from. The chart's newest session,
    /// the same clock every other date decision in this indicator uses — NOT UTC today, which
    /// rolls over during the US evening and would label a board that expires today as
    /// tomorrow's, and hide it as already expired.
    /// </summary>
    private DateOnly MenuSessionDate
    {
        get
        {
            var bars = Bars;
            return bars is { Count: > 0 }
                ? SessionDateFor(bars[0].EndUtc)
                : SessionDateFor(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Switch this chart to another expiration selection and rebind at once. The previous
    /// levels are dropped unconditionally, for the same reason switching vendor drops them:
    /// leaving one board's walls on screen after the user picked another is the same lie.
    /// </summary>
    private void SelectExpiry(string token)
    {
        if (string.Equals(LevelExpiry, token, StringComparison.OrdinalIgnoreCase)) return;
        LevelExpiry = token;

        UnsubscribeFeed();
        _engine.Unbind();
        if (_engine.TryBind(ActiveRegistry, Bars.Spec.InstrumentId, IsContinuousFuturesChart,
                LevelsProvider, SelectedExpiry))
            SubscribeFeed(_engine.Feed!);

        RaiseMenuChanged();
        RecalculateRequested?.Invoke();
    }

    /// <summary>
    /// The parameter as a selection. An empty or unparseable token means the vendor's own
    /// unsliced reading, which is what every chart written before this feature carries.
    /// </summary>
    private ExpirySelection SelectedExpiry
        => ExpirySelection.TryParse(LevelExpiry, out var selection)
            ? selection
            : ExpirySelection.VendorDefault;

    private ToolbarMenuItem[] CommonMenuItems() => new ToolbarMenuItem[]
    {
        new ToolbarSeparator(),
        new ToolbarSubmenu(T("Levels", "Levels"), BuildLevelItems(),
            () => Plots.Any(p => p.Visible)),
        new ToolbarSeparator(),
        Toggle("ShowLabels", "Labels", () => ShowLabels, v => ShowLabels = v),
        Toggle("RegimeBackground", "Regime background",
            () => ShowRegimeBackground, v => ShowRegimeBackground = v),
        Toggle("RegimeBadge", "Regime badge", () => ShowRegimeBadge, v => ShowRegimeBadge = v),
        Toggle("Glow", "Level glow", () => ShowGlow, v => ShowGlow = v),
        Toggle("ExpectedMoveFill", "Expected move fill",
            () => ShowExpectedMoveFill, v => ShowExpectedMoveFill = v),
        new ToolbarSeparator(),
        Toggle("StrikeProfile", "GEX profile histogram",
            () => ShowStrikeProfile, v => ShowStrikeProfile = v),
        Toggle("SplitCallPut", "Split calls / puts",
            () => SplitCallPut, v => SplitCallPut = v),
        new ToolbarSlider(T("ProfileWidth", "Profile width %"),
            () => ProfileWidthPercent,
            v => { ProfileWidthPercent = v; RequestRepaint(); }, 5, 40),
        new ToolbarSlider(T("ProfileOpacity", "Profile opacity"),
            () => ProfileOpacity,
            v => { ProfileOpacity = (int)v; RequestRepaint(); }, 10, 255),
        new ToolbarSeparator(),
        // Conversion changes every projected price, so this one needs a recalculate,
        // not just a repaint.
        new ToolbarCheckItem(T("AutoConvert", "Convert index levels"),
            () => AutoConvert,
            v => { AutoConvert = v; _engine.Invalidate(); RaiseMenuChanged(); RecalculateRequested?.Invoke(); }),
        new ToolbarSeparator(),
        new ToolbarCommand(T("Recalculate", "RE-CALCULATE"), () => RecalculateRequested?.Invoke()),
    };

    CustomRenderLayer IChartCustomRender.Layer => CustomRenderLayer.AboveBars;
    bool IChartCustomRender.SuppressDefaultBarRendering => false;

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        BindFeed(ctx);

        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
            Consume(bars[bars.Count - 1 - i]);

        // One diagnostic line per replay: answers "why is nothing drawn" from the Log tab
        // (bar sessions vs the provider's snapshot session is the usual mismatch).
        var snapshot = _engine.CurrentSnapshot;
        ctx.Print($"Gamma Levels: replayed {bars.Count} bars" +
                  (bars.Count > 0
                      ? $" (newest end {bars[0].EndUtc:yyyy-MM-dd HH:mm}Z, session {SessionDateFor(bars[0].EndUtc):yyyy-MM-dd})"
                      : string.Empty) +
                  ", provider snapshot " +
                  (snapshot is null ? "none" : $"session {snapshot.SessionDate:yyyy-MM-dd} with {snapshot.Levels.Count} levels") +
                  $", applied to newest bar: {(_latest.Source != null ? "YES" : "no")}.");
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _engine.Invalidate();
        _latest = GammaLevelsBarValues.Empty;
    }

    public override void OnDispose()
    {
        UnsubscribeFeed();
        // Unbind, not just unsubscribe: the engine holds the feed's per-selection projection
        // open, and a feed keeps one warm for as long as anyone holds it. Skipping this leaks
        // the hold for the life of the process, so every closed chart would leave the vendor
        // still computing (and paying for) an expiration nothing is looking at.
        _engine.Unbind();
        if (_registry != null)
        {
            _registry.FeedsChanged -= OnFeedsChanged;
            _registry = null;
        }
        base.OnDispose();
    }

    // ─────────────────────────────────────────────── feed binding

    /// <summary>
    /// Bind to a connected levels feed for this chart's instrument; when none is connected
    /// yet, subscribe to the registry so the indicator binds reactively the moment a Levels
    /// connection comes up (same late-feed pattern the order-book consumers use).
    /// </summary>
    /// <summary>
    /// The chart's close at <paramref name="atUtc"/>, or null when that instant predates the
    /// loaded bars. Bars are newest-first, so EndUtc decreases with barsAgo and the "first bar
    /// that ended at or before the target" is a plain binary search. Used to sample the future
    /// at the exact instant the vendor sampled the cash index, so the basis is carry and not
    /// carry-plus-drift.
    /// </summary>
    private double? CloseAtUtc(DateTimeOffset atUtc)
    {
        var bars = Bars;
        int count = bars.Count;
        if (count == 0) return null;

        var target = atUtc.UtcDateTime;

        // Deliberately the last CLOSED bar, never the forming one. A bar that closed AT the stamp
        // is the price at that stamp and reads the same no matter when we ask; the forming bar's
        // close is the price NOW, so two charts (or the same chart after a reload) measured the
        // same stamp seconds apart and disagreed by 15pt — live-observed 2026-07-30, stamp
        // 13:55:00Z read as 7417.50 and 7432.25. A conversion must not depend on when it ran.
        if (bars[0].EndUtc <= target) return bars[0].Close;
        if (bars[count - 1].EndUtc > target) return null;            // stamp predates loaded history

        int lo = 0, hi = count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (bars[mid].EndUtc <= target) hi = mid;
            else lo = mid + 1;
        }
        return bars[lo].Close;
    }

    /// <summary>A continuous futures series carries its root on the spec; its instrument id may
    /// be a venue alias ("MES", "contfut:ES") with no month-code suffix, which the symbol-level
    /// check alone cannot recognise as a future.</summary>
    private bool IsContinuousFuturesChart => Bars.Spec.ContinuousRoot != null;

    private void BindFeed(IIndicatorContext ctx)
    {
        _engine.ReferenceCloseAt = CloseAtUtc;

        if (_registry is null)
        {
            _registry = LevelsAmbient.Registry;
            _registry.FeedsChanged += OnFeedsChanged;
        }

        string instrumentId = Bars.Spec.InstrumentId;
        if (_engine.TryBind(_registry, instrumentId, IsContinuousFuturesChart, LevelsProvider, SelectedExpiry))
        {
            SubscribeFeed(_engine.Feed!);
            ctx.Print($"Gamma Levels: using {_engine.Feed!.DisplayName} ({_engine.VendorSymbol}) " +
                      $"for {instrumentId}.{OtherProvidersHint(instrumentId)}");
        }
        else if (LevelsProvider.Length > 0)
        {
            ctx.Print($"Gamma Levels: provider \"{LevelsProvider}\" is pinned for {instrumentId} but is not " +
                      "connected — connect it under Connections → Levels, or pick another one from the " +
                      "chart's Gamma menu (Levels provider).");
        }
        else
        {
            ctx.Print("Gamma Levels: no Levels provider connected for this instrument — " +
                      "add one under Connections → Levels; the indicator binds automatically.");
        }

        // The toolbar seeded its dropdown when the indicator was ATTACHED, which is before
        // this ran — so it is showing the no-instrument menu (Auto + pin, no expiry group).
        // Now the chart's instrument and the bound feed are known, so the rows it could not
        // compute exist. The host rebuilds this button's items off the back of this.
        RaiseMenuChanged();
    }

    /// <summary>
    /// Names the OTHER connected vendors covering this instrument. Overlapping coverage used to
    /// be invisible: the earliest-connected feed won every chart and nothing said a second
    /// vendor was standing right behind it with different levels.
    /// </summary>
    private string OtherProvidersHint(string instrumentId)
    {
        var others = ActiveRegistry.ResolveFeeds(instrumentId)
            .Where(f => !ReferenceEquals(f, _engine.Feed))
            .Select(f => f.DisplayName)
            .ToArray();
        return others.Length == 0
            ? string.Empty
            : $" Also covering it: {string.Join(", ", others)} — pick one under Gamma → Levels provider.";
    }

    private void OnFeedsChanged()
    {
        // Runs on an arbitrary thread. (Re)bind and replay history; the host marshals the
        // recalculate through the indicator's normal single-threaded lifecycle.
        var registry = _registry;
        if (registry is null) return;

        var previous = _engine.Feed;
        if (_engine.TryBind(registry, Bars.Spec.InstrumentId, IsContinuousFuturesChart, LevelsProvider, SelectedExpiry))
        {
            if (!ReferenceEquals(previous, _engine.Feed))
            {
                SubscribeFeed(_engine.Feed!);
                RecalculateRequested?.Invoke();
            }
        }
        else if (previous != null)
        {
            // Nothing left that we MAY bind to: our feed disconnected, or the pinned vendor did.
            // Keep the last-drawn levels (stale but honest via the label badge) rather than
            // blanking the chart mid-session. Deliberately not "no feed covers it any more" —
            // with a pin in place another vendor still covering the symbol is not our feed.
            UnsubscribeFeed();
        }
    }

    private void SubscribeFeed(ILevelsFeed feed)
    {
        if (ReferenceEquals(_subscribedFeed, feed)) return;
        UnsubscribeFeed();
        _subscribedFeed = feed;
        feed.SnapshotChanged += OnSnapshotChanged;
    }

    private void UnsubscribeFeed()
    {
        if (_subscribedFeed is null) return;
        _subscribedFeed.SnapshotChanged -= OnSnapshotChanged;
        _subscribedFeed = null;
    }

    private void OnSnapshotChanged(string symbol)
    {
        if (!string.Equals(symbol, _engine.VendorSymbol, StringComparison.OrdinalIgnoreCase)) return;
        _engine.Invalidate();
        RecalculateRequested?.Invoke();
        RequestRepaint();
    }

    // ─────────────────────────────────────────────── per-bar values

    private void Consume(Bar bar)
    {
        var session = SessionDateFor(bar.EndUtc);
        // Bars storage is complete before OnDataLoaded (and Bars[0] IS the live bar during
        // OnBarUpdate), so Bars[0] always identifies the chart's newest session — the one a
        // vendor snapshot stamped with a future date (weekend/holiday publish) may apply to.
        bool isLatestBarSession = Bars.Count > 0 && session == SessionDateFor(Bars[0].EndUtc);
        var values = _engine.Compute(session, bar.Close, AutoConvert, isLatestBarSession);
        _latest = values;
        SyncExtraPlotNames(values);
        PrintAppliedOnce(values);

        _callWall.Append(values.CallResistance);
        _putWall.Append(values.PutSupport);
        _hvl.Append(values.Hvl);
        _emHigh.Append(values.ExpectedMoveHigh);
        _emLow.Append(values.ExpectedMoveLow);
        for (int i = 0; i < _extras.Length; i++)
            _extras[i].Append(ShowExtras && i < values.Extras.Count ? values.Extras[i].Price : double.NaN);
    }

    private GammaLevelSnapshot? _namedFrom;

    /// <summary>
    /// Name the extra plot slots after the vendor families they currently carry ("Max Pain",
    /// "Charm Flip", …) instead of the generic "Level 1…5". The chart's per-plot show/hide list,
    /// the legend and the Data Window all read <see cref="IPlot.Name"/>, so this is what makes
    /// each vendor level individually toggleable by MEANING rather than by slot number — the
    /// families differ per provider (and per session), which is exactly why the slots are
    /// generic to begin with. Slots the current snapshot does not fill keep their generic name.
    /// </summary>
    private void SyncExtraPlotNames(GammaLevelsBarValues values)
    {
        var source = values.Source;
        if (source is null || ReferenceEquals(source, _namedFrom)) return;
        // Only the feed's CURRENT snapshot names the slots — historical sessions replaying
        // through Consume must not rename them to families that no longer apply.
        if (!ReferenceEquals(source, _engine.CurrentSnapshot)) return;

        _namedFrom = source;

        // Name the expiration only when the SNAPSHOT says so, never from the selection that
        // was requested. A provider that cannot slice reports VendorDefault, and drawing its
        // full-chain wall under a "0 DTE" label would be a trading error, not a cosmetic one.
        var prefix = ExpiryPrefix(source.Expiry);
        Plots[0].Name = prefix + T("CallWall", "Call Wall");
        Plots[1].Name = prefix + T("PutWall", "Put Wall");
        Plots[2].Name = prefix + T("GammaFlip", "Gamma Flip");

        for (int i = 0; i < _extras.Length; i++)
            Plots[5 + i].Name = i < values.Extras.Count ? values.Extras[i].Label : $"Level {i + 1}";
    }

    /// <summary>
    /// How a snapshot's expiration reads in front of a level name ("0DTE ", "29Aug "), or
    /// empty for a reading that spans the whole chain and therefore names nothing.
    /// </summary>
    private static string ExpiryPrefix(ExpirySelection expiry) => expiry.Kind switch
    {
        ExpirySelectionKind.WithinDays when expiry.Days == 0 => "0DTE ",
        ExpirySelectionKind.WithinDays => $"{expiry.Days}DTE ",
        ExpirySelectionKind.Nearest => "Front ",
        ExpirySelectionKind.MonthlyOpex => "OPEX ",
        ExpirySelectionKind.OnDate => expiry.Date.ToString("ddMMM",
            System.Globalization.CultureInfo.CurrentCulture) + " ",
        _ => string.Empty,
    };

    private DateTimeOffset _lastPrintedAsOf;

    /// <summary>One Log-tab line per newly applied vendor snapshot, so "why is nothing
    /// visible" is answerable from the log: it shows exactly which prices are being drawn
    /// (levels often sit hundreds of points from spot, outside a zoomed viewport).</summary>
    private void PrintAppliedOnce(GammaLevelsBarValues values)
    {
        var source = values.Source;
        if (source is null || source.AsOfUtc == _lastPrintedAsOf) return;
        // Only announce the feed's CURRENT snapshot — historical sessions replaying through
        // Consume would otherwise print one line per past day on every reload.
        if (!ReferenceEquals(source, _engine.CurrentSnapshot)) return;

        _lastPrintedAsOf = source.AsOfUtc;
        var ctx = Context;
        // The conversion is the part that goes wrong silently (a wrong basis shifts every level
        // while still looking plausible), so state it outright next to the prices it produced.
        ctx?.Print($"Gamma Levels conversion: ratio {values.Ratio:0.######}, settled basis {values.Offset:0.####} " +
                   $"(vendor spot {source.UnderlyingPrice:0.####} @ {source.AsOfUtc:HH:mm:ss}Z, " +
                   $"chart ref {(CloseAtUtc(source.AsOfUtc) is double r ? r.ToString("0.####") : "none")}).");
        ctx?.Print($"Gamma Levels applied (session {source.SessionDate:yyyy-MM-dd}): " +
                   $"Call Wall {values.CallResistance:0.####}, Put Wall {values.PutSupport:0.####}, " +
                   $"Gamma Flip {values.Hvl:0.####}" +
                   (values.Extras.Count > 0
                       ? ", " + string.Join(", ", values.Extras.Select(e => $"{e.Label} {e.Price:0.####}"))
                       : string.Empty));
    }

    /// <summary>US options session (Eastern calendar date) a bar belongs to — matches how
    /// levels feeds stamp <see cref="GammaLevelSnapshot.SessionDate"/>.</summary>
    private static DateOnly SessionDateFor(DateTime barEndUtc)
    {
        if (Eastern is null) return DateOnly.FromDateTime(barEndUtc.Date);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(barEndUtc, DateTimeKind.Utc), Eastern).Date);
    }

    private static TimeZoneInfo? ResolveEastern()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch (TimeZoneNotFoundException) { return null; }
        }
    }

    // ──────────────────────────────────────────────────── rendering

    private const double PillPadX = 6, PillPadY = 3, PillGap = 3, PillAccentW = 3, PillRightMargin = 6;
    private static readonly ChartColor PillBackground = new(10, 12, 16, 210);

    /// <summary>One right-edge label pill; mutable class so the stacking pass can move it.</summary>
    private sealed class LevelLabel
    {
        public string Text = string.Empty;
        public ChartColor Color;
        public bool Primary, Stale;
        public double TrueY, Top, W, H;
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var values = _latest;
        if (values.Source is null) return;

        bool stale = IsStale(values);
        if (ShowRegimeBadge) DrawRegimeBadge(ctx, values.Regime, stale);
        if (!ShowLabels) return;

        // Top-pinned pills must not slide under the legend chips or the regime badge.
        double topLimit = Math.Max(ctx.PlotArea.Y + 2,
            ctx.ContentTopPx + (ShowRegimeBadge ? Math.Max(LabelSize, 11) + 16 : 2));

        var labels = new List<LevelLabel>(5 + values.Extras.Count);
        CollectLabel(labels, ctx, Plots[0], values.CallResistance, stale, topLimit, primary: true);
        CollectLabel(labels, ctx, Plots[1], values.PutSupport, stale, topLimit, primary: true);
        CollectLabel(labels, ctx, Plots[2], values.Hvl, stale, topLimit, primary: true);
        CollectLabel(labels, ctx, Plots[3], values.ExpectedMoveHigh, stale, topLimit, primary: false);
        CollectLabel(labels, ctx, Plots[4], values.ExpectedMoveLow, stale, topLimit, primary: false);

        if (ShowExtras)
            for (int i = 0; i < values.Extras.Count && i < _extras.Length; i++)
                CollectLabel(labels, ctx, Plots[5 + i], values.Extras[i].Price, stale, topLimit,
                    primary: false, name: values.Extras[i].Label);

        StackLabels(labels, ctx.PlotArea);
        foreach (var label in labels)
            DrawPill(ctx, label);
    }

    public void OnRenderBackground(IIndicatorRenderContext ctx)
    {
        var values = _latest;

        if (ShowRegimeBackground && values.Regime != GammaRegime.Unknown)
            DrawRegimeGradient(ctx, values);

        if (ShowStrikeProfile) DrawStrikeProfile(ctx, values);

        if (ShowExpectedMoveFill
            && !double.IsNaN(values.ExpectedMoveHigh) && !double.IsNaN(values.ExpectedMoveLow)
            && Plots[3].Visible && Plots[4].Visible)
        {
            BandFill.FillBetween(ctx.Renderer, ctx.Viewport, _emHigh, _emLow,
                Bars.Count, BandFill.WithAlpha(PlotColors.SteelBlue, 22));
        }

        if (ShowGlow && values.Source != null)
        {
            double x0 = CurrentSessionStartX(ctx);
            DrawGlow(ctx, Plots[0], values.CallResistance, x0);
            DrawGlow(ctx, Plots[1], values.PutSupport, x0);
            DrawGlow(ctx, Plots[2], values.Hvl, x0);
        }
    }

    /// <summary>
    /// Regime shading anchored to the gamma flip: green above / red below, strongest at the
    /// flip line and fading to transparent over ~160px — the flip is WHERE the regime pivots,
    /// so shading the pivot zone reads far better than flooding the whole pane in one color.
    /// Falls back to a single faint full-pane tint when the snapshot has no flip level.
    /// </summary>
    private void DrawRegimeGradient(IIndicatorRenderContext ctx, GammaLevelsBarValues values)
    {
        var area = ctx.PlotArea;
        var r = ctx.Renderer;
        byte peak = (byte)Math.Clamp(RegimeOpacity, 2, 60);

        if (double.IsNaN(values.Hvl))
        {
            var tint = values.Regime == GammaRegime.Positive ? PlotColors.LimeGreen : PlotColors.Crimson;
            r.FillRect(area.X, area.Y, area.Width, area.Height,
                BandFill.WithAlpha(tint, (byte)Math.Max(2, peak / 3)));
            return;
        }

        double flipY = ctx.Viewport.PriceToPixelY(values.Hvl);
        const int strips = 24;
        const double reach = 160;
        double stripH = reach / strips;
        r.PushClip(area.X, area.Y, area.Width, area.Height);
        for (int i = 0; i < strips; i++)
        {
            double t = 1.0 - (double)i / strips;
            byte a = (byte)Math.Round(peak * t * t);
            if (a == 0) continue;
            // +0.5px overlap between strips hides hairline seams on fractional pixel grids
            r.FillRect(area.X, flipY - (i + 1) * stripH, area.Width, stripH + 0.5,
                BandFill.WithAlpha(PlotColors.LimeGreen, a));
            r.FillRect(area.X, flipY + i * stripH, area.Width, stripH + 0.5,
                BandFill.WithAlpha(PlotColors.Crimson, a));
        }
        r.PopClip();
    }

    /// <summary>Reused across frames so the histogram allocates nothing on the render path.</summary>
    private readonly List<GammaProfileBar> _profileBars = new();

    /// <summary>
    /// Per-strike GEX histogram along the right edge: bar length = |gamma| at that strike against
    /// the profile's peak. In net mode one bar per strike, green above zero (dealers long gamma
    /// there) / red below; with <see cref="SplitCallPut"/> the band splits in two, call exposure
    /// leftwards and put exposure rightwards, each in its wall's color so the histogram and the
    /// Call/Put Wall lines read as the same thing. Drawn on the BACKGROUND layer so price action
    /// stays readable on top of it, and anchored to the right edge where the level labels live.
    ///
    /// <para>Strikes are scaled with the SAME ratio the level lines used
    /// (<see cref="GammaLevelsBarValues.Ratio"/>) so bars and lines can never disagree.</para>
    /// </summary>
    private void DrawStrikeProfile(IIndicatorRenderContext ctx, GammaLevelsBarValues values)
    {
        var profile = _engine.StrikeProfile;
        if (profile is null || profile.Strikes.Count == 0) return;

        var area = ctx.PlotArea;
        double maxWidth = area.Width * Math.Clamp(ProfileWidthPercent, 5, 40) / 100.0;
        if (maxWidth <= 0) return;

        var viewport = ctx.Viewport;
        GammaProfileLayout.Build(
            profile.Strikes,
            values.Ratio,
            values.Offset,
            viewport.PriceToPixelY,
            area.Right,
            maxWidth,
            area.Y,
            area.Bottom,
            _profileBars,
            SplitCallPut);
        if (_profileBars.Count == 0) return;

        byte alpha = (byte)Math.Clamp(ProfileOpacity, 10, 255);
        var positive = BandFill.WithAlpha(PlotColors.LimeGreen, alpha);
        var negative = BandFill.WithAlpha(PlotColors.Crimson, alpha);
        // Call/put sides take the wall plots' colors, so recoloring a wall recolors its side.
        var callSide = BandFill.WithAlpha(Plots[0].Color, alpha);
        var putSide = BandFill.WithAlpha(Plots[1].Color, alpha);

        var r = ctx.Renderer;
        r.PushClip(area.X, area.Y, area.Width, area.Height);
        foreach (var bar in _profileBars)
        {
            var color = bar.Side switch
            {
                GammaProfileSide.Call => callSide,
                GammaProfileSide.Put => putSide,
                _ => bar.Positive ? positive : negative,
            };
            r.FillRect(bar.Left, bar.Top, bar.Width, bar.Height, color);
        }
        r.PopClip();
    }

    /// <summary>Soft two-pass glow under a primary level line, from the current session's
    /// first visible bar to the right edge — marks which levels are LIVE this session.</summary>
    private void DrawGlow(IIndicatorRenderContext ctx, IPlot plot, double price, double x0)
    {
        if (double.IsNaN(price) || !plot.Visible) return;
        var area = ctx.PlotArea;
        double y = ctx.Viewport.PriceToPixelY(price);
        if (y < area.Y - 8 || y > area.Bottom + 8) return;
        ctx.Renderer.DrawLine(x0, y, area.Right, y, BandFill.WithAlpha(plot.Color, 18), 7);
        ctx.Renderer.DrawLine(x0, y, area.Right, y, BandFill.WithAlpha(plot.Color, 38), 3);
    }

    /// <summary>Pixel X where the newest bar's session starts (clamped to the plot area).</summary>
    private double CurrentSessionStartX(IIndicatorRenderContext ctx)
    {
        var bars = Bars;
        var area = ctx.PlotArea;
        if (bars.Count == 0) return area.X;
        var session = SessionDateFor(bars[0].EndUtc);
        int ago = 0;
        while (ago + 1 < bars.Count && ago < 3000 && SessionDateFor(bars[ago + 1].EndUtc) == session)
            ago++;
        return Math.Max(area.X, ctx.Viewport.BarIndexToPixelX(bars.Count - 1 - ago));
    }

    /// <summary>
    /// Measure one level label and queue it for the stacking pass. Labels stay VISIBLE when
    /// their level sits outside the viewport's price range: gamma walls routinely sit hundreds
    /// of points from spot, so an unclamped label would render off-screen and the indicator
    /// would look like it does nothing. Off-screen levels pin to the top/bottom edge with a
    /// direction arrow.
    /// </summary>
    private void CollectLabel(List<LevelLabel> labels, IIndicatorRenderContext ctx, IPlot plot,
        double price, bool stale, double topLimit, bool primary, string? name = null)
    {
        if (double.IsNaN(price) || !plot.Visible) return;
        var area = ctx.PlotArea;
        double trueY = ctx.Viewport.PriceToPixelY(price);
        string text = LabelText(name ?? plot.Name, price, stale);
        if (trueY < area.Y) text = "▲ " + text;
        else if (trueY > area.Bottom) text = "▼ " + text;

        var (w, h) = ctx.Renderer.MeasureText(text, LabelSize, bold: primary);
        double pillH = h + PillPadY * 2;
        labels.Add(new LevelLabel
        {
            Text = text,
            Color = plot.Color,
            Primary = primary,
            Stale = stale,
            TrueY = trueY,
            W = w,
            H = pillH,
            // manual min/max (not Math.Clamp): on a tiny panel the limits can cross
            Top = Math.Max(Math.Min(trueY - pillH / 2, area.Bottom - pillH - 2), topLimit),
        });
    }

    /// <summary>
    /// Resolve vertical collisions: levels at (nearly) the same price would otherwise draw
    /// their labels on top of each other. Sort by desired Y, push overlapping pills down,
    /// then push the whole run back up if it overflowed the bottom edge.
    /// </summary>
    private static void StackLabels(List<LevelLabel> labels, PlotAreaRect area)
    {
        labels.Sort((a, b) => a.Top.CompareTo(b.Top));
        for (int i = 1; i < labels.Count; i++)
            labels[i].Top = Math.Max(labels[i].Top, labels[i - 1].Top + labels[i - 1].H + PillGap);

        if (labels.Count == 0) return;
        double overflow = labels[^1].Top + labels[^1].H + 2 - area.Bottom;
        if (overflow <= 0) return;
        labels[^1].Top -= overflow;
        for (int i = labels.Count - 2; i >= 0; i--)
            labels[i].Top = Math.Min(labels[i].Top, labels[i + 1].Top - labels[i].H - PillGap);
    }

    /// <summary>Right-edge label pill: dark backing, colored accent bar + outline, and a
    /// dotted leader back to the actual level line when stacking displaced the pill.</summary>
    private void DrawPill(IIndicatorRenderContext ctx, LevelLabel label)
    {
        var area = ctx.PlotArea;
        var r = ctx.Renderer;
        double pillW = label.W + PillPadX * 2 + PillAccentW;
        double x = area.Right - pillW - PillRightMargin;
        double centerY = label.Top + label.H / 2;

        if (Math.Abs(centerY - label.TrueY) > label.H / 2
            && label.TrueY >= area.Y && label.TrueY <= area.Bottom)
        {
            r.DrawLine(x - 9, label.TrueY, x - 1, centerY,
                BandFill.WithAlpha(label.Color, 140), 1, LineStyle.Dotted);
        }

        var body = label.Stale ? BandFill.WithAlpha(label.Color, 150) : label.Color;
        r.FillRect(x, label.Top, pillW, label.H, PillBackground);
        r.FillRect(x, label.Top, PillAccentW, label.H, body);
        r.DrawRoundedRectOutline(x, label.Top, pillW, label.H, 3,
            BandFill.WithAlpha(label.Color, label.Stale ? (byte)90 : (byte)170), 1);
        r.DrawText(x + PillAccentW + PillPadX,
            label.Top + PillPadY + r.FontAscentPx(LabelSize, bold: label.Primary),
            label.Text, body, LabelSize, bold: label.Primary);
    }

    /// <summary>Top-right regime badge: the at-a-glance answer to "which gamma regime am I
    /// trading in", now that the background shading is localized around the flip.</summary>
    private void DrawRegimeBadge(IIndicatorRenderContext ctx, GammaRegime regime, bool stale)
    {
        if (regime == GammaRegime.Unknown) return;
        bool positive = regime == GammaRegime.Positive;
        string text = positive ? "γ+  POSITIVE · dampening" : "γ−  NEGATIVE · amplifying";
        if (stale) text += " (prev)";
        var color = positive ? PlotColors.LimeGreen : PlotColors.Crimson;

        var r = ctx.Renderer;
        var area = ctx.PlotArea;
        double size = Math.Max(LabelSize, 11);
        var (w, h) = r.MeasureText(text, size, bold: true);
        double pillW = w + PillAccentW + PillPadX * 2 + 4, pillH = h + 8;
        double x = area.Right - pillW - 8, y = ctx.ContentTopPx + 6;

        r.FillRect(x, y, pillW, pillH, PillBackground);
        r.FillRect(x, y, PillAccentW, pillH, color);
        r.DrawRoundedRectOutline(x, y, pillW, pillH, 4, BandFill.WithAlpha(color, 200), 1.2);
        r.DrawText(x + PillAccentW + PillPadX + 2, y + 4 + r.FontAscentPx(size, bold: true),
            text, color, size, bold: true);
    }

    private static string LabelText(string name, double price, bool stale)
        => stale ? $"{name} {price:0.####} (prev)" : $"{name} {price:0.####}";

    /// <summary>True when the applied snapshot belongs to an earlier session than the
    /// newest bar — the vendor hasn't published today's levels yet (data honesty).</summary>
    private bool IsStale(GammaLevelsBarValues values)
    {
        if (values.Source is null || Bars.Count == 0) return false;
        return values.Source.SessionDate < SessionDateFor(Bars[0].EndUtc);
    }
}
