using System;
using System.Threading;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;
using SabrTrader.Pipeline.Ticks;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Port of NinjaTrader's built-in <c>PriceLine</c>. Draws horizontal
/// reference lines at the current Bid / Ask / Last prices —
/// independently togglable, each with its own length (% of plot
/// width anchored to the right edge).
///
/// Surface matches NT:
/// <list type="bullet">
///   <item>Three plots (Ask, Bid, Last) exposed via the standard
///     <see cref="Plots"/> list so the indicator picker / settings
///     dialog / legend / price-marker pill all see them.</item>
///   <item>Each plot is backed by a 1-value
///     <see cref="LiveScalarSeries"/> — the indicator does not
///     store one value per bar; it stores ONE current scalar per
///     side. <see cref="ISeries{T}.this[int]"/> returns that
///     scalar for any <c>barsAgo</c> so the standard
///     <c>PlotRenderPass</c> draws a horizontal line through every
///     visible bar (using the plot's color) AND the
///     <c>PriceMarkerRenderPass</c> shows a coloured pill on the
///     right axis at the live price.</item>
///   <item><see cref="IChartCustomRender.OnCustomRender"/> extends
///     the line past the last visible bar to the right edge of the
///     plot area (the NT-style "ahead of price" look), respecting
///     each side's length parameter.</item>
///   <item>Tick-driven state — <see cref="ProcessesMarketData"/>=true.
///     Each tick routes by <see cref="TickFlags"/> to the matching
///     side's scalar; the indicator fires
///     <see cref="IChartCustomRender.RepaintRequested"/> so the
///     chart's vsync-coalesced scheduler queues a paint.</item>
/// </list>
/// </summary>
[IndicatorDescription("Draws live horizontal reference lines at the current Bid, Ask and Last trade prices. Each side is independently togglable with its own length as a percentage of the plot area.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class PriceLineIndicator : IndicatorBase, IChartCustomRender, IRepaintNotifier
{
    private readonly LiveScalarSeries _askSeries  = new();
    private readonly LiveScalarSeries _bidSeries  = new();
    private readonly LiveScalarSeries _lastSeries = new();
    private readonly Plot _askPlot;
    private readonly Plot _bidPlot;
    private readonly Plot _lastPlot;

    public PriceLineIndicator()
    {
        // Tick-driven price lines opt INTO raw market data — without
        // this flag the runtime skips OnMarketData entirely.
        ProcessesMarketData = true;

        ShowAskLine    = false;          // NT defaults: Ask off, Bid off, Last on
        ShowBidLine    = false;
        ShowLastLine   = true;
        AskLineLength  = 100;
        BidLineLength  = 100;
        LastLineLength = 100;

        _askPlot = new Plot(
            name: "Ask",
            values: _askSeries,
            style: PlotStyle.Line,
            color: new ChartColor(0x00, 0x64, 0x00),   // NT DarkGreen
            lineWidthPx: 1.0)
        {
            Visible = ShowAskLine,
            Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null),
        };
        _bidPlot = new Plot(
            name: "Bid",
            values: _bidSeries,
            style: PlotStyle.Line,
            color: new ChartColor(0x21, 0x96, 0xF3),   // blue
            lineWidthPx: 1.0)
        {
            Visible = ShowBidLine,
            Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null),
        };
        _lastPlot = new Plot(
            name: "Last",
            values: _lastSeries,
            style: PlotStyle.Line,
            color: new ChartColor(0xFF, 0xEB, 0x3B),   // yellow
            lineWidthPx: 1.0)
        {
            Visible = ShowLastLine,
            Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null),
        };

        // Add in Ask / Bid / Last order — matches NT's plot order
        // so a workspace from NT round-trips with the right indexes.
        AddPlot(_askPlot);
        AddPlot(_bidPlot);
        AddPlot(_lastPlot);
    }

    public event Action? RepaintRequested;

    // ─── Parameters (NT parity) ──────────────────────────────────

    [IndicatorParameter(DisplayName = "Show Ask Line",
        Description = "Render a horizontal line at the current Ask price.")]
    public bool ShowAskLine { get; set; }

    [IndicatorParameter(DisplayName = "Show Bid Line",
        Description = "Render a horizontal line at the current Bid price.")]
    public bool ShowBidLine { get; set; }

    [IndicatorParameter(DisplayName = "Show Last Line",
        Description = "Render a horizontal line at the most recent trade price.")]
    public bool ShowLastLine { get; set; }

    [IndicatorParameter(DisplayName = "Ask Line Length (%)",
        Description = "Width of the Ask line as a percentage of the chart's plot area, anchored to the right.",
        MinValue = 1, MaxValue = 100, Step = 1)]
    public int AskLineLength { get; set; }

    [IndicatorParameter(DisplayName = "Bid Line Length (%)",
        Description = "Width of the Bid line as a percentage of the chart's plot area, anchored to the right.",
        MinValue = 1, MaxValue = 100, Step = 1)]
    public int BidLineLength { get; set; }

    [IndicatorParameter(DisplayName = "Last Line Length (%)",
        Description = "Width of the Last line as a percentage of the chart's plot area, anchored to the right.",
        MinValue = 1, MaxValue = 100, Step = 1)]
    public int LastLineLength { get; set; }

    // ─── Read-only diagnostic accessors (mirror NT's surface) ──

    public double AskLine  => _askSeries.Value;
    public double BidLine  => _bidSeries.Value;
    public double LastLine => _lastSeries.Value;

    // ─── Lifecycle ───────────────────────────────────────────────

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        // ShowXxxLine flags may have been mutated post-construction
        // (settings dialog / workspace decode); sync plot visibility
        // now so the first paint reflects the current state.
        _askPlot.Visible  = ShowAskLine;
        _bidPlot.Visible  = ShowBidLine;
        _lastPlot.Visible = ShowLastLine;
    }

    public override void OnMarketData(in Tick tick, IIndicatorContext ctx)
    {
        // Route the tick by side. A compound tick (Trade + AtAsk + Ask)
        // updates every flagged side so all three stay in sync.
        bool anyChanged = false;
        if ((tick.Flags & TickFlags.Ask)   != 0) { _askSeries.Set(tick.Price);  anyChanged = true; }
        if ((tick.Flags & TickFlags.Bid)   != 0) { _bidSeries.Set(tick.Price);  anyChanged = true; }
        if ((tick.Flags & TickFlags.Trade) != 0) { _lastSeries.Set(tick.Price); anyChanged = true; }
        if (anyChanged) RepaintRequested?.Invoke();
    }

    // ─── Custom render — extension past the last visible bar ────

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        // The standard PlotRenderPass already drew a horizontal line
        // from the leftmost visible bar to the latest bar (the
        // LiveScalarSeries returns the same scalar at every barsAgo,
        // so consecutive segments fall on a single horizontal line).
        // OnCustomRender ADDS the NT-style extension from the latest
        // bar to the right edge of the plot area (the "ahead of price"
        // look), respecting the per-side length parameter.
        //
        // Sync plot visibility from the param FIRST so a settings-dialog
        // toggle takes effect on the next paint without an OnInit.
        _askPlot.Visible  = ShowAskLine;
        _bidPlot.Visible  = ShowBidLine;
        _lastPlot.Visible = ShowLastLine;

        if (ShowAskLine)  DrawExtension(ctx, _askSeries.Value,  AskLineLength,  _askPlot.Color);
        if (ShowBidLine)  DrawExtension(ctx, _bidSeries.Value,  BidLineLength,  _bidPlot.Color);
        if (ShowLastLine) DrawExtension(ctx, _lastSeries.Value, LastLineLength, _lastPlot.Color);
    }

    private static void DrawExtension(IIndicatorRenderContext ctx, double price,
                                      int lengthPct, ChartColor color)
    {
        if (double.IsNaN(price)) return;
        if (lengthPct <= 0) return;
        if (lengthPct > 100) lengthPct = 100;
        var vp = ctx.Viewport;
        double plotRight = vp.PlotWidthPx;
        double startX = plotRight * (1.0 - lengthPct / 100.0);
        double y = vp.PriceToPixelY(price);
        ctx.Renderer.DrawLine(startX, y, plotRight, y, color, 1.0);
    }

    /// <summary>
    /// Single-scalar <see cref="ISeries{T}"/> — exposes ONE live
    /// value (the most recent tick's price) regardless of
    /// <c>barsAgo</c>. <c>PlotRenderPass</c> walks the visible bars
    /// and treats the same scalar as a "value at every bar", producing
    /// a horizontal line for free. <c>PriceMarkerRenderPass</c> reads
    /// <c>Values[0]</c> and shows a coloured pill on the right axis.
    ///
    /// Thread-safety: ticks arrive on the pipeline worker thread;
    /// <c>OnCustomRender</c> / paint passes run on the WPF UI thread.
    /// <see cref="Interlocked.Exchange(ref double, double)"/> +
    /// <see cref="Volatile"/> reads/writes prevent torn 8-byte values
    /// on 32-bit.
    /// </summary>
    private sealed class LiveScalarSeries : ISeries<double>
    {
        private double _value = double.NaN;
        private int _count;   // 0 before first Set, 1 after — signals "has data" to renderers

        public double Value => Volatile.Read(ref _value);

        public void Set(double value)
        {
            Interlocked.Exchange(ref _value, value);
            // Volatile.Write ensures readers that observe _count = 1
            // also observe the published _value (acquire/release).
            Volatile.Write(ref _count, 1);
        }

        public int Count    => Volatile.Read(ref _count);
        public int Capacity => int.MaxValue;
        public double this[int barsAgo] => Volatile.Read(ref _value);

        public bool TryGet(int barsAgo, out double value)
        {
            value = Volatile.Read(ref _value);
            if (Volatile.Read(ref _count) == 0 || double.IsNaN(value))
            {
                value = double.NaN;
                return false;
            }
            return true;
        }
    }
}
