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
public sealed class GammaLevels : IndicatorBase, IChartCustomRender, IChartBackgroundRender, IRepaintNotifier, IRecalculateNotifier
{
    private static readonly TimeZoneInfo? Eastern = ResolveEastern();

    private readonly GammaLevelsEngine _engine = new();
    private readonly ChunkedSeries<double> _callWall = new(), _putWall = new(), _hvl = new();
    private readonly ChunkedSeries<double> _emHigh = new(), _emLow = new();
    private readonly ChunkedSeries<double>[] _extras;
    private ILevelsRegistry? _registry;
    private ILevelsFeed? _subscribedFeed;
    private GammaLevelsBarValues _latest = GammaLevelsBarValues.Empty;

    [IndicatorParameter(DisplayName = "Convert index levels",
        Description = "When the provider computes levels on an index/ETF proxy (QQQ for NQ, SPY for ES), scale them to chart prices using the snapshot's underlying price. Leave on unless the provider serves native futures levels.",
        Group = "Levels", Order = 0, Primary = true)]
    public bool AutoConvert { get; set; } = true;

    [IndicatorParameter(DisplayName = "Show extra levels",
        Description = "Also plot the provider's additional levels (ranked GEX strikes, 0DTE magnet, max OI, …) as thin gray lines next to the three primary levels.",
        Group = "Levels", Order = 1)]
    public bool ShowExtras { get; set; } = true;

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

    [IndicatorParameter(DisplayName = "Regime badge",
        Description = "Compact badge in the top-right corner naming the current dealer-gamma regime (positive = volatility-dampening, negative = volatility-amplifying).",
        Group = "Display", Order = 16)]
    public bool ShowRegimeBadge { get; set; } = true;

    public GammaLevels(IIndicator? parent = null, ISeries<double>? input = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        var levelSemantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("Call Wall", _callWall, PlotStyle.Line, NTColors.Crimson, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("Put Wall", _putWall, PlotStyle.Line, NTColors.LimeGreen, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("Gamma Flip", _hvl, PlotStyle.Line, NTColors.Gold, 1.5) { Semantics = levelSemantics });
        AddPlot(new Plot("EM High", _emHigh, PlotStyle.Dashed, NTColors.SteelBlue, 1.0) { Semantics = levelSemantics });
        AddPlot(new Plot("EM Low", _emLow, PlotStyle.Dashed, NTColors.SteelBlue, 1.0) { Semantics = levelSemantics });

        _extras = new ChunkedSeries<double>[GammaLevelsEngine.MaxExtraLevels];
        for (int i = 0; i < _extras.Length; i++)
        {
            _extras[i] = new ChunkedSeries<double>();
            AddPlot(new Plot($"Level {i + 1}", _extras[i], PlotStyle.Dotted, NTColors.Gray, 1.0) { Semantics = levelSemantics });
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
        var snapshot = _engine.Feed?.GetSnapshot(_engine.VendorSymbol ?? string.Empty);
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
    private void BindFeed(IIndicatorContext ctx)
    {
        if (_registry is null)
        {
            _registry = LevelsAmbient.Registry;
            _registry.FeedsChanged += OnFeedsChanged;
        }

        string instrumentId = Bars.Spec.InstrumentId;
        if (_engine.TryBind(_registry, instrumentId))
        {
            SubscribeFeed(_engine.Feed!);
            ctx.Print($"Gamma Levels: using {_engine.Feed!.DisplayName} ({_engine.VendorSymbol}) for {instrumentId}.");
        }
        else
        {
            ctx.Print("Gamma Levels: no Levels provider connected for this instrument — " +
                      "add one under Connections → Levels; the indicator binds automatically.");
        }
    }

    private void OnFeedsChanged()
    {
        // Runs on an arbitrary thread. (Re)bind and replay history; the host marshals the
        // recalculate through the indicator's normal single-threaded lifecycle.
        var registry = _registry;
        if (registry is null) return;

        var previous = _engine.Feed;
        if (_engine.TryBind(registry, Bars.Spec.InstrumentId))
        {
            if (!ReferenceEquals(previous, _engine.Feed))
            {
                SubscribeFeed(_engine.Feed!);
                RecalculateRequested?.Invoke();
            }
        }
        else if (previous != null && registry.ResolveFeed(Bars.Spec.InstrumentId) is null)
        {
            // Our feed disconnected: keep the last-drawn levels (stale but honest via the
            // label badge) rather than blanking the chart mid-session.
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
        PrintAppliedOnce(values);

        _callWall.Append(values.CallResistance);
        _putWall.Append(values.PutSupport);
        _hvl.Append(values.Hvl);
        _emHigh.Append(values.ExpectedMoveHigh);
        _emLow.Append(values.ExpectedMoveLow);
        for (int i = 0; i < _extras.Length; i++)
            _extras[i].Append(ShowExtras && i < values.Extras.Count ? values.Extras[i].Price : double.NaN);
    }

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
        if (!ReferenceEquals(source, _engine.Feed?.GetSnapshot(_engine.VendorSymbol ?? string.Empty))) return;

        _lastPrintedAsOf = source.AsOfUtc;
        var ctx = Context;
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

        if (ShowExpectedMoveFill
            && !double.IsNaN(values.ExpectedMoveHigh) && !double.IsNaN(values.ExpectedMoveLow)
            && Plots[3].Visible && Plots[4].Visible)
        {
            BandFill.FillBetween(ctx.Renderer, ctx.Viewport, _emHigh, _emLow,
                Bars.Count, BandFill.WithAlpha(NTColors.SteelBlue, 22));
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
            var tint = values.Regime == GammaRegime.Positive ? NTColors.LimeGreen : NTColors.Crimson;
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
                BandFill.WithAlpha(NTColors.LimeGreen, a));
            r.FillRect(area.X, flipY + i * stripH, area.Width, stripH + 0.5,
                BandFill.WithAlpha(NTColors.Crimson, a));
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
        var color = positive ? NTColors.LimeGreen : NTColors.Crimson;

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
