using System;
using System.Globalization;
using System.Threading;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Renders a small
/// "HH:MM:SS" chip in a configurable corner of the chart, counting
/// down until the in-progress bar closes. Meaningful on sub-daily
/// time bars (HH:MM:SS), tick bars ("N tk") and volume bars ("N vol").
///
/// <para><b>Price-triggered bars (Range / Renko / Median Renko)</b> have
/// no countdown — a new bar opens the instant price crosses a fixed
/// threshold. For those the BarTimer instead draws TWO horizontal guide
/// lines to the right of the live bar, marking the exact prices where the
/// next bar begins (see <see cref="BarTimerLevels"/>). Range gives a
/// symmetric pair around the forming bar's open; Renko gives the
/// continuation level (1 brick, with the trend) and the reversal level
/// (2 bricks, against it); Median Renko gives its trend / reversal
/// threshold band.</para>
///
/// <para><b>Update cadence — indicator-owned timer, no chart polling:</b></para>
/// The indicator owns a single <see cref="System.Threading.Timer"/>
/// at 1 Hz. Every tick fires <see cref="RepaintRequested"/>; the
/// chart's coalescer caps the actual paints at vsync (so the badge
/// updates per second, the chart wastes no paints on idle frames).
///
/// Why <see cref="System.Threading.Timer"/> instead of WPF's
/// <c>DispatcherTimer</c>: the indicator must work in test +
/// headless contexts that don't have a WPF dispatcher; the threading
/// timer fires on a thread-pool thread and the event handler hops
/// to the chart's UI dispatcher via the standard
/// <see cref="IChartCustomRender.RepaintRequested"/> contract.
/// </summary>
[IndicatorDescription("Bar Timer. Time bars show a live HH:MM:SS countdown chip in a chosen corner; tick / volume bars show the count remaining. Range / Renko / Median Renko bars instead draw two guide lines to the right of the live bar at the exact prices where the next bar opens. Inactive on daily+ bars.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BarTimerIndicator : IndicatorBase, IChartCustomRender, IRepaintNotifier
{
    /// <summary>Where in the chart pane the badge sits.</summary>
    public enum BadgeCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    private Timer? _timer;
    private int _disposedFlag;

    // Instrument tick size, captured at init (the render context doesn't
    // carry it). Needed to turn RangeTicks / BrickTicks into prices for
    // the Range / Renko next-bar guide lines. 0 = host couldn't resolve
    // one → the level lines are suppressed and we fall back to the badge.
    private double _tickSize;

    public BarTimerIndicator()
    {
        Corner = BadgeCorner.BottomRight;          // matches the original default
        TextColor       = new ChartColor(0xE0, 0xE0, 0xE0);
        BackgroundColor = new ChartColor(0x18, 0x1C, 0x25, 0xCC);   // semi-transparent dark
        BorderColor     = new ChartColor(0x3F, 0x3F, 0x3F);
        FontSizePx      = 12;
        PaddingPx       = 6;

        ShowBarStartLevels = true;
        ShowLevelPrice     = true;
        UpperLevelColor    = new ChartColor(0x26, 0xA6, 0x9A);      // teal-green (up trigger)
        LowerLevelColor    = new ChartColor(0xEF, 0x53, 0x50);      // red (down trigger)
        LevelLineWidthPx   = 1.5;
    }

    public event Action? RepaintRequested;

    // ─── Parameters ──────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "Corner",
        Description = "Which corner of the chart pane the BarTimer badge anchors to.")]
    public BadgeCorner Corner { get; set; }

    /// <summary>Text colour of the countdown.</summary>
    public ChartColor TextColor { get; set; }
    /// <summary>Background fill of the badge chip.</summary>
    public ChartColor BackgroundColor { get; set; }
    /// <summary>Border stroke colour around the badge chip.</summary>
    public ChartColor BorderColor { get; set; }
    /// <summary>Font size (px) of the countdown text.</summary>
    public double FontSizePx { get; set; }
    /// <summary>Inner padding (px) between text and badge edge.</summary>
    public double PaddingPx { get; set; }

    [IndicatorParameter(DisplayName = "Show bar-start levels",
        Description = "On Range / Renko bars, draw two horizontal guide lines to the right of the live bar at the exact prices where the next bar opens (instead of the countdown badge).")]
    public bool ShowBarStartLevels { get; set; }

    [IndicatorParameter(DisplayName = "Show level price",
        Description = "Label each bar-start guide line with its price.")]
    public bool ShowLevelPrice { get; set; }

    /// <summary>Colour of the upper (higher-price) next-bar guide line.</summary>
    public ChartColor UpperLevelColor { get; set; }
    /// <summary>Colour of the lower (lower-price) next-bar guide line.</summary>
    public ChartColor LowerLevelColor { get; set; }
    /// <summary>Stroke width (px) of the next-bar guide lines.</summary>
    public double LevelLineWidthPx { get; set; }

    // ─── Lifecycle ───────────────────────────────────────────────

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tickSize = ctx.TickSize ?? 0;
        // 1 Hz timer fires from a thread-pool thread; the handler
        // raises RepaintRequested which the chart marshals + coalesces.
        // dueTime = 1s so the first tick lands exactly one second in.
        _timer = new Timer(_ =>
        {
            if (Volatile.Read(ref _disposedFlag) != 0) return;
            RepaintRequested?.Invoke();
        }, state: null, dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));
    }

    public override void OnDispose()
    {
        // IndicatorBase.Dispose guarantees a single OnDispose call so
        // this body runs exactly once; the inner CAS is belt-and-suspenders
        // for the rare race where the timer callback might overlap.
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;
        var t = _timer;
        _timer = null;
        t?.Dispose();
    }

    // ─── Custom render ───────────────────────────────────────────

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var bars = ctx.Bars;
        if (bars == null || bars.Count == 0) return;
        var lastBar = bars[0];
        var spec = bars.Spec;

        // Price-triggered bar types (Range / Renko / Median Renko) have no
        // countdown — draw the two prices where the next bar opens instead
        // of a badge. Median Renko also needs the previous closed brick.
        if (ShowBarStartLevels)
        {
            Bar? prevBar = bars.Count > 1 ? bars[1] : null;
            var levels = BarTimerLevels.ComputeStartLevels(spec, lastBar, _tickSize, prevBar);
            if (levels is { } lv)
            {
                DrawStartLevels(ctx, bars, lv);
                return;
            }
        }

        var text = BarTimerFormatter.FormatTimeRemaining(spec, lastBar, ctx.Now);
        if (string.IsNullOrEmpty(text)) return;

        // Estimate the badge size from the text length. The chart
        // renderer doesn't expose MeasureText — use a font-metric
        // heuristic (~0.6 * font-size per char for monospace digits).
        double charW = FontSizePx * 0.6;
        double textW = text.Length * charW;
        double textH = FontSizePx;
        double w = textW + PaddingPx * 2;
        double h = textH + PaddingPx * 2;

        // Position the badge in the requested corner of the PLOT area
        // (excludes axis strips — badge never sits over a price label).
        var pa = ctx.PlotArea;
        const double Margin = 6;
        double x = Corner switch
        {
            BadgeCorner.TopLeft or BadgeCorner.BottomLeft  => pa.X + Margin,
            _                                              => pa.Right - w - Margin,
        };
        double y = Corner switch
        {
            BadgeCorner.TopLeft or BadgeCorner.TopRight    => pa.Y + Margin,
            _                                              => pa.Bottom - h - Margin,
        };

        // Background fill, border stroke (4 thin lines), text.
        ctx.Renderer.FillRect(x, y, w, h, BackgroundColor);
        ctx.Renderer.DrawLine(x,     y,     x + w, y,     BorderColor, 1);
        ctx.Renderer.DrawLine(x + w, y,     x + w, y + h, BorderColor, 1);
        ctx.Renderer.DrawLine(x + w, y + h, x,     y + h, BorderColor, 1);
        ctx.Renderer.DrawLine(x,     y + h, x,     y,     BorderColor, 1);
        ctx.Renderer.DrawText(x + PaddingPx, y + PaddingPx + FontSizePx * 0.8,
            text, TextColor, FontSizePx);
    }

    // ─── Bar-start level guide lines (Range / Renko) ─────────────

    /// <summary>
    /// Draw the two next-bar trigger prices as horizontal lines running
    /// from the live bar's right edge across the chart's right margin to
    /// the price axis — exactly where the user looks for "what price opens
    /// the next bar". Custom renderers may paint into the right-side
    /// margin (the original parity), so the lines sit in the otherwise-empty strip
    /// to the right of the live bar.
    /// </summary>
    private void DrawStartLevels(IIndicatorRenderContext ctx, IBarSeries bars,
        BarTimerLevels.BarStartLevels lv)
    {
        var vp = ctx.Viewport;

        // Live bar = newest bar = highest absolute viewport index.
        int liveIdx = bars.Count - 1;
        double liveRightX = vp.BarIndexToPixelX(liveIdx) + vp.BarSpacingPx * 0.5;
        // Start no later than the live edge; the lines extend rightward
        // into the empty margin and never sit on top of a candle.
        double x0 = Math.Min(liveRightX, vp.BarAreaWidthPx);
        double x1 = vp.PlotWidthPx;                       // price-axis clip boundary
        if (x1 <= x0) x1 = x0 + 24;                       // no margin configured → short stub

        DrawLevel(ctx, x0, x1, lv.Upper, UpperLevelColor);
        DrawLevel(ctx, x0, x1, lv.Lower, LowerLevelColor);
    }

    private void DrawLevel(IIndicatorRenderContext ctx, double x0, double x1,
        double price, ChartColor color)
    {
        var pa = ctx.PlotArea;
        double y = ctx.Viewport.PriceToPixelY(price);
        // Skip levels scrolled off the visible price range.
        if (y < pa.Y || y > pa.Bottom) return;

        var r = ctx.Renderer;
        r.DrawLine(x0, y, x1, y, color, LevelLineWidthPx);

        if (!ShowLevelPrice) return;
        string txt = FormatPrice(price);
        var (w, h) = r.MeasureText(txt, FontSizePx);
        double tx = x1 - w - 2;                           // right-aligned at the axis end
        double ty = y - h - 1;                            // just above the line
        if (ty < pa.Y) ty = y + 1;                        // flip below if no room above
        r.FillRect(tx - 2, ty, w + 4, h, BackgroundColor);
        r.DrawText(tx, ty + FontSizePx * 0.8, txt, color, FontSizePx);
    }

    /// <summary>Format a price to the instrument's tick precision.</summary>
    private string FormatPrice(double price)
        => price.ToString("F" + DecimalsFromTickSize(_tickSize), CultureInfo.InvariantCulture);

    /// <summary>Number of fractional digits implied by the tick size.</summary>
    private static int DecimalsFromTickSize(double tickSize)
    {
        if (tickSize <= 0) return 2;
        if (tickSize >= 1) return 0;
        var s = tickSize.ToString("0.##########", CultureInfo.InvariantCulture);
        int dot = s.IndexOf('.');
        return dot < 0 ? 0 : s.Length - dot - 1;
    }
}
