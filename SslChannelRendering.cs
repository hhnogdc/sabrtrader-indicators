using System;
using System.Collections.Generic;
using System.Linq;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Plots;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rendering half of <see cref="SslChannel"/> — a 1-on-1 translation of NT
/// <c>OnRender</c> and its helpers (<c>RenderEntry</c>, <c>RenderStatistics</c>): the
/// per-bar trend-coloured channel lines + translucent fill, the reversal (R) /
/// pull-back (PB) markers, entry &amp; max-favourable target lines with tick labels, and
/// the win-ratio / profit-factor / expected-value statistics table.
/// </summary>
public sealed partial class SslChannel
{
    // Per-frame render state (render thread only).
    private IChartRenderer _r = null!;
    private ChartViewport _vp = null!;
    private int _firstVisAbs, _lastVisAbs;

    public CustomRenderLayer Layer => CustomRenderLayer.AboveBars;
    public bool SuppressDefaultBarRendering => false;

    private string? FontFam => string.IsNullOrWhiteSpace(FontFamilyName) ? null : FontFamilyName;
    private float TextW(string text, double size) => (float)_r.MeasureText(text, size, FontFam).WidthPx;
    private float TextH(string text, double size) => (float)_r.MeasureText(text, size, FontFam).HeightPx;
    private double XBar(int absIdx) => _vp.BarIndexToPixelX(absIdx);
    private double Y(double price) => _vp.PriceToPixelY(price);

    private static ChartColor WithAlpha(ChartColor c, byte a) => new(c.R, c.G, c.B, a);

    /// <summary>
    /// NT <c>DrawTextLabel</c>: a text layout of <paramref name="width"/> px anchored at
    /// (<paramref name="x"/>, <paramref name="topY"/> − 2). Centre alignment centres the
    /// glyphs within [x, x+width] (NT's default); leading left-aligns at x.
    /// </summary>
    private void DrawLabel(string text, double x, double topY, double width, double size, ChartColor color, bool center = true)
    {
        if (color.A == 0) return;
        double baseY = topY - 2 + _r.FontAscentPx(size, FontFam);
        if (center) _r.DrawTextCentered(x + width / 2.0, baseY, text, color, size, FontFam);
        else _r.DrawText(x, baseY, text, color, size, FontFam);
    }

    private void DrawLineStyled(double x0, double y0, double x1, double y1, ChartColor color, double width, LineStyle style)
    {
        if (color.A == 0) return;
        switch (style)
        {
            case LineStyle.Dashed: DashedLine(x0, y0, x1, y1, color, width, 6, 4); break;
            case LineStyle.Dotted: DashedLine(x0, y0, x1, y1, color, width, 2, 4); break;
            default: _r.DrawLine(x0, y0, x1, y1, color, width); break;
        }
    }

    private void DashedLine(double x0, double y0, double x1, double y1, ChartColor color, double width, double dashPx, double gapPx)
    {
        if (_r is IPolylineRenderer poly)
        {
            Span<(double X, double Y)> pts = stackalloc (double, double)[2];
            pts[0] = (x0, y0); pts[1] = (x1, y1);
            poly.DrawDashedPolyline(pts, color, width, dashPx, gapPx);
        }
        else _r.DrawLine(x0, y0, x1, y1, color, width);
    }

    private void FillEllipse(double cx, double cy, double rx, double ry, ChartColor color)
    {
        if (color.A == 0) return;
        if (_r is IEllipseRenderer e) { e.FillEllipse(cx, cy, rx, ry, color); return; }
        Span<(double X, double Y)> pts = stackalloc (double, double)[16];
        for (int i = 0; i < 16; i++)
        {
            double a = Math.PI * 2 * i / 16;
            pts[i] = (cx + rx * Math.Cos(a), cy + ry * Math.Sin(a));
        }
        _r.FillPolygon(pts, color);
    }

    // ─── Main pass (NT OnRender) ─────────────────────────────────────────────

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        try
        {
            _r = ctx.Renderer;
            _vp = ctx.Viewport;

            lock (_gate)
            {
                int count = _upV.Count;
                if (count == 0) return;
                _firstVisAbs = Math.Clamp(_vp.FirstVisibleBarIdx, 0, count - 1);
                _lastVisAbs = Math.Clamp(_vp.LastVisibleBarIdx, 0, count - 1);

                // Channel fill + per-bar trend-coloured lines.
                if (FillBands && FillOpacity > 0)
                {
                    byte alpha = (byte)Math.Clamp((int)Math.Round(FillOpacity / 100.0 * 255.0), 0, 255);
                    RenderFill(count, alpha);
                }
                if (Plots[0].Visible) RenderChannelLine(count, _upV, Plots[0].Style);
                if (Plots[1].Visible) RenderChannelLine(count, _downV, Plots[1].Style);

                // NT OnRender: per-entry markers, then the statistics panel.
                double radius = Math.Max(TextW("PB", FontSize), TextH("PB", FontSize));
                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (entry.EntryBar > _lastVisAbs) continue;
                    if (entry.MaxBar < _firstVisAbs && entry.EntryBar < _firstVisAbs) continue;
                    RenderEntry(entry, radius);
                }

                RenderStatistics();
            }
        }
        catch (Exception)
        {
            // NT OnRender swallows everything per frame.
        }
    }

    // ─── Channel fill (per trend segment) ────────────────────────────────────

    private void RenderFill(int count, byte alpha)
    {
        int first = Math.Max(0, _firstVisAbs);
        int last = Math.Min(count - 1, _lastVisAbs);
        var top = new List<(double X, double Y)>();
        var bot = new List<(double X, double Y)>();
        sbyte segDir = 0;
        for (int i = first; i <= last; i++)
        {
            if (!_computed[i]) { FlushFill(top, bot, segDir, alpha); segDir = 0; continue; }
            if (segDir != 0 && _trendDir[i] != segDir) FlushFill(top, bot, segDir, alpha);
            segDir = _trendDir[i];
            double x = XBar(i);
            top.Add((x, Y(_upV[i])));
            bot.Add((x, Y(_downV[i])));
        }
        FlushFill(top, bot, segDir, alpha);
    }

    private void FlushFill(List<(double X, double Y)> top, List<(double X, double Y)> bot, sbyte dir, byte alpha)
    {
        if (top.Count >= 2 && dir != 0)
        {
            ChartColor c = WithAlpha(dir > 0 ? FillUptrendColor : FillDowntrendColor, alpha);
            int n = top.Count;
            var poly = new (double X, double Y)[n * 2];
            for (int i = 0; i < n; i++) poly[i] = top[i];
            for (int i = 0; i < n; i++) poly[n + i] = bot[n - 1 - i];
            _r.FillPolygon(poly, c);
        }
        top.Clear();
        bot.Clear();
    }

    private void RenderChannelLine(int count, List<double> series, PlotStyle style)
    {
        int first = Math.Max(1, _firstVisAbs);
        int last = Math.Min(count - 1, _lastVisAbs);
        for (int i = first; i <= last; i++)
        {
            if (!_computed[i] || !_computed[i - 1]) continue;
            ChartColor c = _trendDir[i] > 0 ? UptrendColor : DowntrendColor;
            CustomLineRenderer.DrawSegment(_r, XBar(i - 1), Y(series[i - 1]), XBar(i), Y(series[i]), c, LineWidth, style);
        }
    }

    // ─── Per-entry markers + lines + tick labels (NT RenderEntry) ────────────

    private void RenderEntry(SslEntry entry, double radius)
    {
        string txt = "1";
        ChartColor markerColor = UpTrendReversalColor;
        switch (entry.PointType)
        {
            case SslPointType.Pullback:
                if (!ShowPullbacks) return;
                txt = "PB"; markerColor = PullbackColor; break;
            case SslPointType.ReversalUp:
                if (!ShowReversals) return;
                txt = "R"; markerColor = UpTrendReversalColor; break;
            case SslPointType.ReversalDown:
                if (!ShowReversals) return;
                txt = "R"; markerColor = DownTrendReversalColor; break;
        }

        double x = XBar(entry.EntryBar);
        double y = Y(entry.Price);
        FillEllipse(x, y, radius, radius, markerColor);
        DrawLabel(txt, x - radius / 2.0, y + 5 - radius / 2.0, radius, FontSize, TextColor);

        // Entry-price tick line.
        y = Y(entry.EntryPrice);
        ChartColor entryColor = entry.IsLong ? LongEntryColor : ShortEntryColor;
        double d = EntryLineWidth / 2.0;
        if (entryColor.A != 0) _r.DrawLine(x - d, y, x + d, y, entryColor, EntryColorWidth);

        if (entry.PointType == SslPointType.Pullback) return;

        // Reversal: max-favourable target line + tick label.
        double ticks = entry.IsLong
            ? (entry.MaxPrice - entry.EntryPrice) / _tickSize
            : (entry.EntryPrice - entry.MaxPrice) / _tickSize;
        int bar = entry.MaxBar;
        if (entry.IsActive)
        {
            bar = CurrentBar;
            ticks = entry.IsLong ? (_price - entry.EntryPrice) / _tickSize : (entry.EntryPrice - _price) / _tickSize;
        }

        if (entry.Winner || entry.IsActive)
        {
            double x2 = XBar(bar);
            double y2 = Y(entry.MaxPrice);
            DrawLineStyled(x, y, x2, y2, TargetLineColor, TargetLineWidth, TargetLineStyle);
            txt = ticks >= 0 ? $"+{(int)ticks} ticks" : $"{(int)ticks} ticks";
            double ty = entry.IsLong ? Y(entry.MaxPrice) - TextH(txt, TargetFontSize) : Y(entry.MaxPrice);
            DrawLabel(txt, x2, ty, radius, TargetFontSize, TargetWinnerColor);
        }
        else
        {
            ticks = Math.Abs(entry.ExitPrice - entry.EntryPrice) / _tickSize;
            double ty = entry.IsLong ? Y(entry.EntryPrice) - TextH(txt, TargetFontSize) : Y(entry.EntryPrice);
            double x2 = XBar(entry.EntryBar);
            txt = $"-{(int)ticks} ticks";
            DrawLabel(txt, x2, ty, radius, TargetFontSize, TargetLoserColor);
        }
    }

    // ─── Statistics panel (NT RenderStatistics) ──────────────────────────────

    private void RenderStatistics()
    {
        if (!ShowStats) return;
        double tickValue = _pointValue * _tickSize;
        var entries = _entries.Where(e => !e.IsActive).ToList();

        int totalCount = entries.Count;
        float totalWinners = entries.Count(e => e.Winner);
        float totalLosers = totalCount - totalWinners;
        float totalWinRatio = totalCount > 0 ? totalWinners / totalCount * 100f : 0f;
        double maxTotalProfit = entries.Where(e => e.Winner && e.MaxPrice > 0)
            .Sum(e => Math.Abs(e.MaxPrice - e.EntryPrice)) / _tickSize * tickValue;
        double maxTotalLoss = totalLosers * StoplossTicks * tickValue;
        double maxTotalProfitFactor = maxTotalProfit / maxTotalLoss;
        double maxTotalExpectedValue = ExpectedValue(totalWinners, totalLosers, totalCount, maxTotalProfit, maxTotalLoss);

        var reversals = entries.Where(e => e.PointType is SslPointType.ReversalDown or SslPointType.ReversalUp).ToList();
        int reversalsCount = reversals.Count;
        float reversalsWinners = reversals.Count(e => e.Winner);
        float reversalsLosers = reversalsCount - reversalsWinners;
        float reversalsWinRatio = reversalsCount > 0 ? reversalsWinners / reversalsCount * 100f : 0f;
        double maxReversalsProfit = reversals.Where(e => e.Winner && e.MaxPrice > 0)
            .Sum(e => Math.Abs(e.MaxPrice - e.EntryPrice)) / _tickSize * tickValue;
        double reversalsLoss = reversalsLosers * StoplossTicks * tickValue;
        double maxReversalsProfitFactor = maxReversalsProfit / reversalsLoss;
        double maxReversalsExpectedValue = ExpectedValue(reversalsWinners, reversalsLosers, reversalsCount, maxReversalsProfit, reversalsLoss);

        var pullbacks = entries.Where(e => e.PointType == SslPointType.Pullback).ToList();
        int pullbacksCount = pullbacks.Count;
        float pullbacksWinners = pullbacks.Count(e => e.Winner);
        float pullbackLosers = pullbacksCount - pullbacksWinners;
        float pullbacksWinRatio = pullbacksCount > 0 ? pullbacksWinners / pullbacksCount * 100f : 0f;
        double maxPullbacksProfit = pullbacks.Where(e => e.Winner && e.MaxPrice > 0)
            .Sum(e => Math.Abs(e.MaxPrice - e.EntryPrice)) / _tickSize * tickValue;
        double pullbacksLoss = pullbackLosers * StoplossTicks * tickValue;
        double maxPullbacksProfitFactor = maxPullbacksProfit / pullbacksLoss;
        double maxPullbacksExpectedValue = ExpectedValue(pullbacksWinners, pullbackLosers, pullbacksCount, maxPullbacksProfit, pullbacksLoss);

        float h = TextH("ABWJKLIPGj", StatFontSize) + 5;
        float w1 = TextW("Max. Expected Value:1", StatFontSize);
        float w2 = TextW("Reversals:1", StatFontSize) + w1;
        float w3 = TextW("Pullbacks:1", StatFontSize) + w2;
        float w4 = TextW("Pullbacks", StatFontSize) + w3;
        int line = 1;
        float x = StatsX + 10;
        float y = StatsY + 10;

        if (StatsBGColor.A != 0)
            _r.FillRect(StatsX, StatsY + h, 20 + w4, 20 + 7 * h, StatsBGColor);

        Lbl("Total", x + w1, y + line * h);
        Lbl("Reversals", x + w2, y + line * h);
        Lbl("Pullbacks", x + w3, y + line * h);
        line++;

        Lbl("Total", x, y + line * h);
        Lbl($"{totalCount}", x + w1, y + line * h);
        Lbl($"{reversalsCount}", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"{pullbacksCount}", x + w3, y + line * h);
        line++;

        Lbl("Winners", x, y + line * h);
        Lbl($"{totalWinners}", x + w1, y + line * h);
        Lbl($"{reversalsWinners}", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"{pullbacksWinners}", x + w3, y + line * h);
        line++;

        Lbl("Losers", x, y + line * h);
        Lbl($"{totalLosers}", x + w1, y + line * h);
        Lbl($"{reversalsLosers}", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"{pullbackLosers}", x + w3, y + line * h);
        line++;

        Lbl("Win Ratio", x, y + line * h);
        Lbl($"{totalWinRatio:0.00}%", x + w1, y + line * h);
        Lbl($"{reversalsWinRatio:0.00}%", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"{pullbacksWinRatio:0.00}%", x + w3, y + line * h);
        line++;

        Lbl("Max. Profit Factor", x, y + line * h);
        Lbl($"{maxTotalProfitFactor:0.00}", x + w1, y + line * h);
        Lbl($"{maxReversalsProfitFactor:0.00}", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"{maxPullbacksProfitFactor:0.00}", x + w3, y + line * h);
        line++;

        Lbl("Max. Expected Value", x, y + line * h);
        Lbl($"${maxTotalExpectedValue:0.00}", x + w1, y + line * h);
        Lbl($"${maxReversalsExpectedValue:0.00}", x + w2, y + line * h);
        if (ShowPullbacks) Lbl($"${maxPullbacksExpectedValue:0.00}", x + w3, y + line * h);

        void Lbl(string t, double lx, double ly) => DrawLabel(t, lx, ly, 200, StatFontSize, TextColor, center: false);
    }

    private static double ExpectedValue(float winners, float losers, double count, double profit, double loss)
    {
        if (count <= 0 || winners <= 0 || losers <= 0) return 0;
        return (winners / count) * (profit / winners) - (losers / count) * (loss / losers);
    }
}
