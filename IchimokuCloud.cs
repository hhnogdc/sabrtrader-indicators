using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ichimoku Cloud.
///   Conversion (Tenkan) = (MAX(H, ConversionPeriod) + MIN(L, ConversionPeriod)) / 2
///   Base (Kijun)        = (MAX(H, BasePeriod) + MIN(L, BasePeriod)) / 2
///   Leading Span A      = (Conversion + Base) / 2
///   Leading Span B      = (MAX(H, LeadingSpanBPeriod) + MIN(L, LeadingSpanBPeriod)) / 2
///   Lagging (Chikou)    = Close
/// the original visually displaces Conversion/Base back and Spans forward via
/// Values[i][±N] writes; our framework emits at the current bar — the
/// renderer is the right place for visual displacement. Math identical.
/// Defaults ConversionPeriod=9, BasePeriod=26, LeadingSpanBPeriod=52,
/// LaggingDisplacement=26, SpanDisplacement=-26. Overlay.
/// </summary>
[IndicatorDescription("Ichimoku Cloud: plots Tenkan/Kijun conversion lines, Chikou lagging line, and the Senkou A/B leading-span cloud projected forward for trend, support and resistance.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class IchimokuCloud : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _conv  = new();
    private readonly ChunkedSeries<double> _base_ = new();
    private readonly ChunkedSeries<double> _spanA = new();
    private readonly ChunkedSeries<double> _spanB = new();
    private readonly ChunkedSeries<double> _lag   = new();
    private IndicatorOutputSeries? _cW, _bW, _aW, _sBW, _lW;
    private RollingMinMax _hConv = null!, _lConv = null!;
    private RollingMinMax _hBase = null!, _lBase = null!;
    private RollingMinMax _hSpanB = null!, _lSpanB = null!;
    private readonly Plot _spanAPlot, _spanBPlot, _lagPlot;

    public IchimokuCloud(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Conversion (Tenkan)", _conv,  PlotStyle.Line, PlotColors.Blue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Base (Kijun)",        _base_, PlotStyle.Line, PlotColors.Red,  1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        // Span A/B/Lagging plots are visually DISPLACED, so they're painted by OnCustomRender,
        // not the standard PlotRenderPass. IsCustomRendered = true makes the standard pass skip
        // them (so they don't paint twice at the native, un-displaced bar position); Visible =
        // true so the settings show/hide toggle gates each displaced line in the custom render
        // (and the line-style picker works). The plots stay in the Plots list so the legend /
        // data box / settings dialog discover them, and the custom renderer reads their colour +
        // opacity back for the cloud fill.
        //
        // Default alpha 0x55 (~33%) on the Senkou plots so the cloud band is translucent
        // out of the box (original-style). Users tune via the settings dialog's Opacity column.
        var spanADefault = new ChartColor(PlotColors.LimeGreen.R, PlotColors.LimeGreen.G, PlotColors.LimeGreen.B, 0x55);
        var spanBDefault = new ChartColor(PlotColors.Red.R,       PlotColors.Red.G,       PlotColors.Red.B,       0x55);
        _spanAPlot = new Plot("Leading (Senkou) Span A", _spanA, PlotStyle.Line, spanADefault,    1.0) { Visible = true, IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        _spanBPlot = new Plot("Leading (Senkou) Span B", _spanB, PlotStyle.Line, spanBDefault,    1.0) { Visible = true, IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        _lagPlot   = new Plot("Lagging (Chikou)",        _lag,   PlotStyle.Line, PlotColors.Green,  1.0) { Visible = true, IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        AddPlot(_spanAPlot); AddPlot(_spanBPlot); AddPlot(_lagPlot);
    }

    [IndicatorParameter(DisplayName = "ConversionPeriod",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int ConversionPeriod { get; set; } = 9;

    [IndicatorParameter(DisplayName = "BasePeriod",           MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int BasePeriod { get; set; } = 26;

    [IndicatorParameter(DisplayName = "LeadingSpanBPeriod",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LeadingSpanBPeriod { get; set; } = 52;

    [IndicatorParameter(DisplayName = "LaggingDisplacement",  MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int LaggingDisplacement { get; set; } = 26;

    [IndicatorParameter(DisplayName = "SpanDisplacement",     MinValue = int.MinValue, MaxValue = 0, Step = 1)]
    public int SpanDisplacement { get; set; } = -26;

    public ISeries<double> Conversion => _cW  ??= new IndicatorOutputSeries(_conv,  Bars);
    public ISeries<double> Base       => _bW  ??= new IndicatorOutputSeries(_base_, Bars);
    public ISeries<double> SpanA      => _aW  ??= new IndicatorOutputSeries(_spanA, Bars);
    public ISeries<double> SpanB      => _sBW ??= new IndicatorOutputSeries(_spanB, Bars);
    public ISeries<double> Lagging    => _lW  ??= new IndicatorOutputSeries(_lag,   Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _hConv = new RollingMinMax(ConversionPeriod, trackMax: true);
        _lConv = new RollingMinMax(ConversionPeriod, trackMax: false);
        _hBase = new RollingMinMax(BasePeriod, trackMax: true);
        _lBase = new RollingMinMax(BasePeriod, trackMax: false);
        _hSpanB = new RollingMinMax(LeadingSpanBPeriod, trackMax: true);
        _lSpanB = new RollingMinMax(LeadingSpanBPeriod, trackMax: false);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    private void Consume(Bar bar)
    {
        _hConv.Push(bar.High);   _lConv.Push(bar.Low);
        _hBase.Push(bar.High);   _lBase.Push(bar.Low);
        _hSpanB.Push(bar.High);  _lSpanB.Push(bar.Low);

        double conv = (_hConv.Value + _lConv.Value) / 2;
        double base_ = (_hBase.Value + _lBase.Value) / 2;
        double spanA = (conv + base_) / 2;
        double spanB = (_hSpanB.Value + _lSpanB.Value) / 2;
        double lag = bar.Close;

        _conv.Append(conv); _base_.Append(base_);
        _spanA.Append(spanA); _spanB.Append(spanB);
        _lag.Append(lag);
    }

    /// <summary>
    /// Custom render — draws Lagging (Chikou) at <c>X(i - LaggingDisplacement)</c> and
    /// Senkou A / B at <c>X(i - SpanDisplacement)</c> (SpanDisplacement is typically -26
    /// which makes the cloud project FORWARD past the last bar). Optionally fills the cloud
    /// between Senkou A and B.
    /// </summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        int total = _spanA.Count;
        if (total == 0) return;

        // Fill the cloud band between Senkou A and B (its own visual, independent of the
        // boundary lines' show/hide toggles).
        FillCloudBetween(renderer, vp, total, ctx.PlotArea);

        // Each displaced line honours its plot's Visible toggle + line style. Use the opaque
        // variant of the user-chosen colour for the line itself — alpha governs the cloud only.
        if (_lagPlot.Visible)
            DrawDisplaced(renderer, vp, _lag,   total, -LaggingDisplacement, Opaque(_lagPlot.Color),   _lagPlot.LineWidthPx, _lagPlot.Style);
        // Senkou A: at X(i - SpanDisplacement). SpanDisplacement = -26 → shift right by +26.
        if (_spanAPlot.Visible)
            DrawDisplaced(renderer, vp, _spanA, total, -SpanDisplacement,    Opaque(_spanAPlot.Color), _spanAPlot.LineWidthPx, _spanAPlot.Style);
        // Senkou B.
        if (_spanBPlot.Visible)
            DrawDisplaced(renderer, vp, _spanB, total, -SpanDisplacement,    Opaque(_spanBPlot.Color), _spanBPlot.LineWidthPx, _spanBPlot.Style);
    }

    /// <summary>Strip the alpha channel — used so the boundary lines render solid even
    /// when the cloud fill below uses the same color with reduced alpha.</summary>
    private static ChartColor Opaque(ChartColor c) => new(c.R, c.G, c.B, 0xff);

    private static void DrawDisplaced(SabrTrader.Pipeline.Charts.Rendering.IChartRenderer renderer,
                                      SabrTrader.Pipeline.Charts.Viewport.ChartViewport vp,
                                      ChunkedSeries<double> series, int total, int barShift,
                                      ChartColor color, double widthPx, PlotStyle style)
    {
        bool havePrev = false;
        double prevX = 0, prevY = 0;
        // Iterate viewport-visible bars + the shift's worth of leading/trailing bars
        // so a line that starts off-screen still draws to the visible edge.
        int first = Math.Max(0, vp.FirstVisibleBarIdx - Math.Abs(barShift));
        int last  = Math.Min(total - 1, vp.LastVisibleBarIdx + Math.Abs(barShift));
        for (int i = first; i <= last; i++)
        {
            int barsAgo = total - 1 - i;
            double v = series[barsAgo];
            if (double.IsNaN(v)) { havePrev = false; continue; }
            double x = vp.BarIndexToPixelX(i + barShift);
            double y = vp.PriceToPixelY(v);
            if (havePrev)
                SabrTrader.Pipeline.Charts.Rendering.CustomLineRenderer.DrawSegment(renderer, prevX, prevY, x, y, color, widthPx, style);
            prevX = x; prevY = y; havePrev = true;
        }
    }

    private void FillCloudBetween(SabrTrader.Pipeline.Charts.Rendering.IChartRenderer renderer,
                                  SabrTrader.Pipeline.Charts.Viewport.ChartViewport vp,
                                  int total, PlotAreaRect area)
    {
        int barShift = -SpanDisplacement;
        int first = Math.Max(0, vp.FirstVisibleBarIdx - Math.Abs(barShift));
        int last  = Math.Min(total - 1, vp.LastVisibleBarIdx + Math.Abs(barShift));
        if (first >= last) return;

        var bullPoints = new System.Collections.Generic.List<(double X, double Y)>();
        var bearPoints = new System.Collections.Generic.List<(double X, double Y)>();
        // Two passes: build top edge (max of A, B) and bottom edge (min of A, B) for each side
        // of the A>=B / A<B boundary. Simplified: draw one filled polygon per contiguous
        // (A >= B) span (bullish cloud LimeGreen) and one per (A < B) span (bearish cloud Red).
        ChartColor bull = _spanAPlot.Color;       // LimeGreen
        ChartColor bear = _spanBPlot.Color;       // Red
        bool? lastBull = null;
        var poly = new System.Collections.Generic.List<(double X, double Y)>();
        var topPath = new System.Collections.Generic.List<(double X, double Y)>();
        var botPath = new System.Collections.Generic.List<(double X, double Y)>();
        for (int i = first; i <= last; i++)
        {
            int barsAgo = total - 1 - i;
            double a = _spanA[barsAgo];
            double b = _spanB[barsAgo];
            if (double.IsNaN(a) || double.IsNaN(b)) { Flush(renderer, topPath, botPath, lastBull, bull, bear); lastBull = null; continue; }
            bool isBull = a >= b;
            if (lastBull is null) lastBull = isBull;
            if (isBull != lastBull) { Flush(renderer, topPath, botPath, lastBull, bull, bear); lastBull = isBull; }
            double x = vp.BarIndexToPixelX(i + barShift);
            double yA = vp.PriceToPixelY(a);
            double yB = vp.PriceToPixelY(b);
            topPath.Add((x, Math.Min(yA, yB)));
            botPath.Add((x, Math.Max(yA, yB)));
        }
        Flush(renderer, topPath, botPath, lastBull, bull, bear);
    }

    private static void Flush(SabrTrader.Pipeline.Charts.Rendering.IChartRenderer renderer,
                              System.Collections.Generic.List<(double X, double Y)> topPath,
                              System.Collections.Generic.List<(double X, double Y)> botPath,
                              bool? isBull, ChartColor bull, ChartColor bear)
    {
        if (topPath.Count < 2 || isBull is null) { topPath.Clear(); botPath.Clear(); return; }
        // Use the plot's own color (with its user-configured alpha) directly. The renderer
        // does proper alpha-blending against the chart background, no fake gray mix needed.
        var color = isBull.Value ? bull : bear;
        int n = topPath.Count;
        var poly = new (double X, double Y)[n * 2];
        for (int i = 0; i < n; i++) poly[i]     = topPath[i];
        for (int i = 0; i < n; i++) poly[n + i] = botPath[n - 1 - i];
        renderer.FillPolygon(poly, color);
        topPath.Clear(); botPath.Clear();
    }
}
