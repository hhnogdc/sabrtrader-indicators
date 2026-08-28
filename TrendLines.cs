using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// TrendLines.
/// Uses a child <see cref="Swing"/> to detect swing highs/lows. When a NEW swing high
/// is detected that is LOWER than the previous swing high, a down-trend line is drawn
/// from the previous swing-high pivot to the new pivot (and vice-versa for up-trends).
/// Keeps the last <see cref="NumberOfTrendLines"/> trend lines; older ones rendered with
/// <see cref="OldTrendsOpacity"/>.
/// One Plot ("Current Trend Line", White) holds the projected price of the active trend
/// at each bar (intercept + slope · barIdx) for the auto-scale and data-box.
/// Custom render draws the trend rays via IChartCustomRender.
/// Defaults Strength=5, NumberOfTrendLines=1, OldTrendsOpacity=25, AlertOnBreak=false.
/// </summary>
[IndicatorDescription("Auto-draws trend rays between consecutive swing pivots (lower highs for down-trends, higher lows for up-trends). Retains the last N lines and can alert on a break.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class TrendLines : IndicatorBase, IChartCustomRender
{
    private sealed class TrendRay
    {
        public int StartBar;
        public double StartPrice;
        public int EndBar;
        public double EndPrice;
        public bool IsHigh;
        // Absolute bar index where price first closed through the line (broke it), or -1
        // while still intact. A broken line stops extending here (the original @TrendLines freezes a
        // trend line at its break instead of projecting it on into the void).
        public int BreakBar = -1;
    }

    private readonly ChunkedSeries<double> _projected = new();
    private IndicatorOutputSeries? _projW;

    private readonly Queue<TrendRay> _trendLines = new();
    private TrendRay _highTrend = null!;
    private TrendRay _lowTrend = null!;
    private bool? _highTrendIsActive;
    private int _lastHighBar = -1;
    private int _lastLowBar = -1;
    private double _lastHighPrice = double.MinValue;
    private double _lastLowPrice = double.MaxValue;

    private Swing _swing = null!;
    private int _barIdx;

    public TrendLines(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // The original @TrendLines: the "Current Trend Line" plot is TRANSPARENT — it exists only
        // for the data box + auto-scale (it carries the active trend's projected price,
        // which jumps as the active trend flips high↔low so it would render as an ugly
        // zigzag). The visible trend lines are the rays drawn in OnCustomRender.
        AddPlot(new Plot("Current Trend Line", _projected, PlotStyle.Line, ChartColor.Transparent, 1.0)
        {
            Visible = true,
            ShowPriceMarker = false,
            Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null),
        });
    }

    [IndicatorParameter(DisplayName = "Strength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Strength { get; set; } = 5;

    [IndicatorParameter(DisplayName = "NumberOfTrendLines", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int NumberOfTrendLines { get; set; } = 1;

    [IndicatorParameter(DisplayName = "OldTrendsOpacity", MinValue = 0, MaxValue = int.MaxValue, Step = 5)]
    public int OldTrendsOpacity { get; set; } = 25;

    [IndicatorParameter(DisplayName = "AlertOnBreak")]
    public bool AlertOnBreak { get; set; } = false;

    public ChartColor HighStroke { get; set; } = PlotColors.DarkCyan;
    public ChartColor LowStroke  { get; set; } = PlotColors.Goldenrod;

    public ISeries<double> Projected => _projW ??= new IndicatorOutputSeries(_projected, Bars);

    /// <summary>A retained trend line: pivot→pivot plus the bar where price broke it
    /// (<see cref="BreakBar"/> = -1 while still intact).</summary>
    public readonly record struct TrendLineSnapshot(
        int StartBar, double StartPrice, int EndBar, double EndPrice, bool IsHigh, int BreakBar);

    /// <summary>Read-only snapshot of the retained trend lines (oldest→newest). Exposed
    /// for the data box / diagnostics / tests; the rays themselves are drawn in OnCustomRender.</summary>
    public IReadOnlyList<TrendLineSnapshot> TrendLineSnapshots()
    {
        var list = new List<TrendLineSnapshot>(_trendLines.Count);
        foreach (var t in _trendLines)
            list.Add(new TrendLineSnapshot(t.StartBar, t.StartPrice, t.EndBar, t.EndPrice, t.IsHigh, t.BreakBar));
        return list;
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Strength < 1 || NumberOfTrendLines < 1) throw new ArgumentOutOfRangeException();
        // Swing as child (reads source bars directly — safe).
        _swing = new Swing(this, bars: Bars) { Strength = Strength };
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Step(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child (Swing/Sma) on the live bar
        if (Bars.Count == 0) return;
        Step(Bars, 0);
    }

    private void Step(IBarSeries bars, int barsAgoOfCurrent)
    {
        _projected.Append(double.NaN);

        // Look back up to Strength+1 bars for a high pivot. The child Swing's barsAgo
        // reference is the series' CURRENT (last) bar, so query relative to the bar
        // being processed (barsAgoOfCurrent) — NOT a fixed 0. The returned index is
        // barsAgo from the last bar; convert to an ABSOLUTE bar index for the pivot.
        // (Using 0 + over-indexing bars[barsAgoOfCurrent + hb] read the wrong pivot and
        // ran off the end of the series during backfill, so no trend was ever found.)
        int lastBar = bars.Count - 1;
        int hb = _swing.SwingHighBar(barsAgoOfCurrent, 1, Strength + 1);
        if (hb != -1)
        {
            double price = bars[hb].High;
            int pivotBar = lastBar - hb;
            if (price < _lastHighPrice && _lastHighBar > -1 && pivotBar > _lastHighBar)
            {
                _highTrend = new TrendRay { StartBar = _lastHighBar, StartPrice = _lastHighPrice, EndBar = pivotBar, EndPrice = price, IsHigh = true };
                EnqueueTrend(_highTrend);
                _highTrendIsActive = true;
            }
            _lastHighBar = pivotBar;
            _lastHighPrice = price;
        }

        int lb = _swing.SwingLowBar(barsAgoOfCurrent, 1, Strength + 1);
        if (lb != -1)
        {
            double price = bars[lb].Low;
            int pivotBar = lastBar - lb;
            if (price > _lastLowPrice && _lastLowBar > -1 && pivotBar > _lastLowBar)
            {
                _lowTrend = new TrendRay { StartBar = _lastLowBar, StartPrice = _lastLowPrice, EndBar = pivotBar, EndPrice = price, IsHigh = false };
                EnqueueTrend(_lowTrend);
                _highTrendIsActive = false;
            }
            _lastLowBar = pivotBar;
            _lastLowPrice = price;
        }

        // Break detection: once price CLOSES through a line it's broken — freeze it at
        // this bar so it no longer extends (a down-trend/resistance breaks when a bar
        // closes ABOVE it; an up-trend/support breaks when a bar closes BELOW it).
        int curAbs = lastBar - barsAgoOfCurrent;
        double curClose = bars[barsAgoOfCurrent].Close;
        foreach (var t in _trendLines)
        {
            if (t.BreakBar >= 0 || curAbs <= t.EndBar || t.EndBar == t.StartBar) continue;
            double slope = (t.EndPrice - t.StartPrice) / (t.EndBar - t.StartBar);
            double lineVal = t.StartPrice + slope * (curAbs - t.StartBar);
            if (t.IsHigh ? curClose > lineVal : curClose < lineVal)
                t.BreakBar = curAbs;
            // (AlertOnBreak has no wired alert seam in the pipeline yet — kept as a no-op
            // parameter, matching the original port; the break itself is recorded above.)
        }

        if (_highTrendIsActive.HasValue)
        {
            var t = _highTrendIsActive.Value ? _highTrend : _lowTrend;
            if (t != null && t.EndBar != t.StartBar)
            {
                double slope = (t.EndPrice - t.StartPrice) / (t.EndBar - t.StartBar);
                double v = slope * _barIdx - (slope * t.StartBar - t.StartPrice);
                _projected.Set(0, v);
            }
        }

        _barIdx++;
    }

    private void EnqueueTrend(TrendRay t)
    {
        _trendLines.Enqueue(t);
        while (_trendLines.Count > NumberOfTrendLines) _trendLines.Dequeue();
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_trendLines.Count == 0) return;
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        int n = _trendLines.Count;
        int i = 0;
        // Older rays drawn at OldTrendsOpacity; newest at full.
        foreach (var t in _trendLines)
        {
            bool newest = (++i == n);
            var color = t.IsHigh ? HighStroke : LowStroke;
            if (!newest) color = WithOpacity(color, OldTrendsOpacity);
            double slope = (t.EndPrice - t.StartPrice) / (t.EndBar - t.StartBar);
            double x1 = vp.BarIndexToPixelX(t.StartBar);
            double y1 = vp.PriceToPixelY(t.StartPrice);
            // Right end of the ray: where price BROKE the line (frozen there), else the
            // current bar. A broken line must NOT keep projecting on past the break — that
            // produced the "weird" ray plunging far below price after it was long broken.
            // No margin math — the chart clips the custom-render pass to the bar area.
            int lastBar = Bars.Count - 1;
            int rightBar = t.BreakBar >= 0 ? t.BreakBar : (lastBar > t.EndBar ? lastBar : t.EndBar);
            double x2 = vp.BarIndexToPixelX(rightBar);
            double y2 = vp.PriceToPixelY(t.StartPrice + slope * (rightBar - t.StartBar));
            renderer.DrawLine(x1, y1, x2, y2, color, 1.0);
        }
    }

    private static ChartColor WithOpacity(ChartColor c, double opacityPct)
    {
        double a = Math.Min(100, Math.Max(0, opacityPct)) / 100.0;
        return new ChartColor(c.R, c.G, c.B, (byte)Math.Round(a * 255));
    }
}
