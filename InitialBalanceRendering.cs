using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Drawing.Tools;
using SabrTrader.Pipeline.MarketProfiles.Rendering;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rendering half of <see cref="InitialBalance"/> — a translation of the original
/// <c>TDUInitialBalance.OnRender</c> and its helpers (<c>DrawTextLabel</c>,
/// <c>DrawPriceTextLabel(Above)</c>, <c>DrawExtensions</c>, <c>RenderStatistics</c>).
/// the original's time axis (<c>GetXByTime</c>) maps through <see cref="SessionBarTimeIndex"/>;
/// its price axis (<c>GetYByValue</c>) through <see cref="ChartViewport.PriceToPixelY"/>.
/// the original's per-bar <c>BackBrush</c> session / pre-market shading paints behind the candles
/// via <see cref="IChartBackgroundRender.OnRenderBackground"/>.
/// </summary>
public sealed partial class InitialBalance
{
    /// <summary>The original sets <c>ZOrder = -1</c>: every line / area / label paints BENEATH the candles.</summary>
    public CustomRenderLayer Layer => CustomRenderLayer.BelowBars;

    /// <summary>One the original <c>Stroke</c> as used for line drawing (colour + width + dash style).</summary>
    private readonly record struct StrokeDef(ChartColor Color, double Width, LineStyle Style);

    /// <summary>One level label to place: its line's pixel Y, the finished text, colour, and
    /// whether it stacks below its line (lows) rather than above it (everything else).</summary>
    private readonly record struct LevelLabel(double LineY, string Text, ChartColor Color, bool Below);

    // Per-frame render state (render thread only).
    private IChartRenderer _r = null!;
    private ChartViewport _vp = null!;
    private PlotAreaRect _plotArea;
    private SessionBarTimeIndex _time = null!;
    private DateTime _firstVisible, _lastVisible;

    private static ChartColor WithOpacity(ChartColor c, double opacityPct)
    {
        double a = Math.Min(100, Math.Max(0, opacityPct)) / 100.0;
        return new ChartColor(c.R, c.G, c.B, (byte)Math.Round(a * 255));
    }

    private double X(DateTime t) => _time.PixelXForTime(t);
    private double Y(double price) => _vp.PriceToPixelY(price);

    /// <summary>The original <c>RenderTarget.DrawLine</c> with the stroke's dash style.</summary>
    private void Line(double x0, double y0, double x1, double y1, in StrokeDef s)
    {
        if (s.Color.A == 0) return;
        switch (s.Style)
        {
            case LineStyle.Solid:
                _r.DrawLine(x0, y0, x1, y1, s.Color, s.Width);
                break;
            case LineStyle.Dashed:
                DashedLine(x0, y0, x1, y1, s.Color, s.Width, dashPx: 6, gapPx: 4);
                break;
            default: // Dotted
                DashedLine(x0, y0, x1, y1, s.Color, s.Width, dashPx: 2, gapPx: 4);
                break;
        }
    }

    private void DashedLine(double x0, double y0, double x1, double y1,
        ChartColor color, double width, double dashPx, double gapPx)
    {
        if (_r is IPolylineRenderer poly)
        {
            Span<(double X, double Y)> pts = stackalloc (double, double)[2];
            pts[0] = (x0, y0);
            pts[1] = (x1, y1);
            poly.DrawDashedPolyline(pts, color, width, dashPx, gapPx);
        }
        else
        {
            _r.DrawLine(x0, y0, x1, y1, color, width);   // renderer can't dash — solid fallback
        }
    }

    /// <summary>The original <c>FillRectangle</c> — normalised so an inverted rect still fills.</summary>
    private void FillRect(double x, double y, double w, double h, ChartColor color)
    {
        if (color.A == 0) return;
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        if (w <= 0 || h <= 0) return;
        _r.FillRect(x, y, w, h, color);
    }

    // ───── Text helpers (the original GetTextWidth / GetTextHeight / DrawTextLabel) ──

    private string? FontFamily => string.IsNullOrWhiteSpace(TextFontFamily) ? null : TextFontFamily;

    private double TextWidth(string text) => _r.MeasureText(text, TextFontSize, FontFamily).WidthPx;

    private double TextHeight(string text) => _r.MeasureText(text, TextFontSize, FontFamily).HeightPx;

    private double FontHeight => TextHeight("Ag");

    /// <summary>The original <c>DrawTextLabel</c>: vertically centres the text on <paramref name="y"/>, 5 px right of <paramref name="x"/>.</summary>
    private void DrawTextLabel(string text, double x, double y, ChartColor color)
    {
        if (color.A == 0) return;
        y -= FontHeight / 2;
        x += 5;
        _r.DrawText(x, y - 2 + _r.FontAscentPx(TextFontSize, FontFamily), text, color, TextFontSize, FontFamily);
    }

    /// <summary>The original <c>DrawPriceTextLabel</c>: appends "(price)" and "/ x.xx pts" per the Show toggles.</summary>
    private string PriceLabel(string text, double price, double range)
    {
        if (ShowPrice) text = text + " (" + MeasureFormatting.FormatPrice(price, _tickSize) + ")";
        if (ShowRange && range > 0) text = string.Format("{0} / {1:0.00} pts", text, range);
        return text;
    }

    /// <summary>The original <c>DrawPriceTextLabelAbove</c>: the label sits just above the line.</summary>
    private void DrawPriceTextLabelAbove(string text, double price, double range, double x, double y, ChartColor color)
    {
        text = PriceLabel(text, price, range);
        y -= FontHeight + 2;
        DrawTextLabel(text, x, y, color);
    }

    /// <summary>
    /// Places the level labels so none is bisected by its own line and none overlaps another:
    /// <c>Below</c> labels (lows) fan downward beneath their line, the rest fan upward above it,
    /// each separated by a text row. Replaces the original's pairwise nudges, which could leave the middle
    /// label of a coincident cluster (e.g. Session-low == OR-low) centred ON the line.
    /// </summary>
    private void DrawStackedLevelLabels(List<LevelLabel> labels, double x)
    {
        if (labels.Count == 0) return;

        var lineY = new double[labels.Count];
        var below = new bool[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            lineY[i] = labels[i].LineY;
            below[i] = labels[i].Below;
        }

        // Lines run UNDER the labels here, so labels must clear their line: fan highs up,
        // lows down (FontHeight + 2 clear of the line, FontHeight + 4 between stacked labels).
        double[] centerY = LevelLabelStacker.Fan(lineY, below, FontHeight + 2, FontHeight + 4);
        for (int i = 0; i < labels.Count; i++)
            DrawTextLabel(labels[i].Text, x, centerY[i], labels[i].Color);
    }

    // ───── Background pass (the original BackBrush session / pre-market shading) ─────

    public void OnRenderBackground(IIndicatorRenderContext ctx)
    {
        if (!ShowAll) return;
        IBarSeries? bars = ctx.Bars;
        if (bars is null || bars.Count == 0) return;

        bool session = ShowSessionBackground && SessionBackgroundOpacity > 0;
        bool premarket = ShowPreMarketBackground && PreMarketBackgroundOpacity > 0;
        if (!session && !premarket) return;

        var vp = ctx.Viewport;
        var area = ctx.PlotArea;
        ChartColor sessionFill = WithOpacity(SessionBackgroundColor, SessionBackgroundOpacity);
        ChartColor preFill = WithOpacity(PreMarketBackgroundColor, PreMarketBackgroundOpacity);

        int count = bars.Count;
        int first = Math.Max(0, vp.FirstVisibleBarIdx);
        int last = Math.Min(count - 1, vp.LastVisibleBarIdx);
        double half = vp.BarSpacingPx / 2.0;

        ctx.Renderer.PushClip(area.X, area.Y, area.Width, area.Height);
        try
        {
            for (int abs = first; abs <= last; abs++)
            {
                // The original checks the bar's (end-stamped) time against the same windows
                // it uses for the calculation.
                DateTime t = bars[count - 1 - abs].End;
                double x = vp.BarIndexToPixelX(abs);

                if (session && InSession(t, SessionStartTime, SessionEndTime))
                    ctx.Renderer.FillRect(x - half, area.Y, vp.BarSpacingPx, area.Height, sessionFill);

                if (premarket && InSession(t, PremarketStartTime, SessionStartTime))
                    ctx.Renderer.FillRect(x - half, area.Y, vp.BarSpacingPx, area.Height, preFill);
            }
        }
        finally
        {
            ctx.Renderer.PopClip();
        }
    }

    // ───── Main pass (the original OnRender) ──────────────────────────────────────────

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (!ShowAll) return;
        IBarSeries? bars = ctx.Bars;
        if (bars is null || bars.Count == 0) return;

        // The original skips Day / Week / Month / Year charts.
        if (bars.Spec is TimeBarSpec tb && tb.Period >= TimeSpan.FromDays(1)) return;

        _r = ctx.Renderer;
        _vp = ctx.Viewport;
        _plotArea = ctx.PlotArea;

        int count = bars.Count;
        _time = new SessionBarTimeIndex(count,
            a => bars[count - 1 - a].Start, a => bars[count - 1 - a].End, _vp);

        int firstAbs = Math.Clamp(_vp.FirstVisibleBarIdx, 0, count - 1);
        int lastAbs = Math.Clamp(_vp.LastVisibleBarIdx, 0, count - 1);
        _firstVisible = bars[count - 1 - firstAbs].Start;
        _lastVisible = bars[count - 1 - lastAbs].End;

        // The original Strokes, resolved once per frame from the flattened parameters.
        var preMarketStroke = new StrokeDef(PreMarketColor, PreMarketWidth, PreMarketStyle);
        var ibDeveloping = new StrokeDef(IbDevelopingColor, IbDevelopingWidth, IbDevelopingStyle);
        var ibVirgin = new StrokeDef(IbVirginColor, IbVirginWidth, IbVirginStyle);
        var ibBroken = new StrokeDef(IbBrokenColor, IbBrokenWidth, IbBrokenStyle);
        var ibMid = new StrokeDef(IbMidColor, IbMidWidth, IbMidStyle);
        var sessionOpen = new StrokeDef(SessionOpenColor, SessionOpenWidth, SessionOpenStyle);
        var orDeveloping = new StrokeDef(OrDevelopingColor, OrDevelopingWidth, OrDevelopingStyle);
        var orHiLo = new StrokeDef(OrHiLoColor, OrHiLoWidth, OrHiLoStyle);
        var orMid = new StrokeDef(OrMidColor, OrMidWidth, OrMidStyle);
        var sessionHigh = new StrokeDef(SessionHighColor, SessionHighWidth, SessionHighStyle);
        var sessionLow = new StrokeDef(SessionLowColor, SessionLowWidth, SessionLowStyle);

        IbSession[] sessions = RenderSessions();
        double chartWidth = _plotArea.X + _plotArea.Width;
        DateTime now = _now;

        double xNow = X(now);
        double ypreHigh = Y(_preMarketHigh);
        double ypreLow = Y(_preMarketLow);
        double ypreMid = (ypreLow + ypreHigh) / 2.0;
        double preMarketMid = (_preMarketHigh + _preMarketLow) / 2.0;

        // Right-edge band geometry (the original).
        bool rightEdge = LinesPlacement == InitialBalanceLinePlacement.RightEdge;
        double bandLeft = Math.Max(0, chartWidth - 100);
        double bandRight = Math.Max(0, chartWidth);
        double labelRightX = Math.Max(0, chartWidth - 95);

        _r.PushClip(_plotArea.X, _plotArea.Y, _plotArea.Width, _plotArea.Height);
        try
        {
            if (ShowPremarket && _preMarketStartTime != default)
            {
                double xdayStart = X(_preMarketStartTime);

                if (!rightEdge)
                {
                    double xpre2 = Math.Min(xNow + 140, chartWidth);

                    if (PremarketAreaOpacity > 0)
                        FillRect(xdayStart, ypreHigh, xNow - xdayStart, ypreLow - ypreHigh,
                            WithOpacity(PremarketAreaColor, PremarketAreaOpacity));

                    double tw = TextWidth("Pre-high");
                    double xt = xpre2;
                    if (xt + tw + 5 > chartWidth) xt = chartWidth - (tw + 25);
                    Line(xdayStart, ypreHigh, xt - 20, ypreHigh, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-high", _preMarketHigh, _preMarketHigh - _preMarketLow, labelRightX, ypreHigh, PreMarketColor);

                    tw = TextWidth("Pre-mid");
                    xt = xpre2;
                    if (xt + tw + 5 > chartWidth) xt = chartWidth - (tw + 25);
                    Line(xdayStart, ypreMid, xt - 20, ypreMid, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-mid", preMarketMid, 0, labelRightX, ypreMid, PreMarketColor);

                    tw = TextWidth("Pre-low");
                    xt = xpre2;
                    if (xt + tw + 5 > chartWidth) xt = chartWidth - (tw + 25);
                    Line(xdayStart, ypreLow, xt - 20, ypreLow, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-low", _preMarketLow, _preMarketHigh - _preMarketLow, labelRightX, ypreLow, PreMarketColor);
                }
                else
                {
                    // Right edge: short pre-market lines for today only.
                    Line(bandLeft, ypreHigh, bandRight, ypreHigh, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-high", _preMarketHigh, _preMarketHigh - _preMarketLow, labelRightX, ypreHigh, PreMarketColor);

                    Line(bandLeft, ypreMid, bandRight, ypreMid, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-mid", preMarketMid, 0, labelRightX, ypreMid, PreMarketColor);

                    Line(bandLeft, ypreLow, bandRight, ypreLow, preMarketStroke);
                    DrawPriceTextLabelAbove("Pre-low", _preMarketLow, _preMarketHigh - _preMarketLow, labelRightX, ypreLow, PreMarketColor);
                }
            }

            for (int i = 0; i < sessions.Length; i++)
            {
                IbSession ib = sessions[i];
                if (ib.EndTime < _firstVisible) continue;
                if (ib.StartTime > _lastVisible) continue;

                if (Display == InitialBalanceDisplay.CurrentDayOnly && ib.EndTime.Date < now.Date)
                    continue;

                // Right-edge mode draws only the latest session to avoid clutter.
                if (rightEdge && ib.EndTime.Date != now.Date)
                    continue;

                // The original end-stamps its bars and resolves X by that end-stamp, so it anchors
                // lines at SessionStartTime (= open + 1 min). SabrTrader maps a time to a bar by
                // its START (SessionBarTimeIndex), so that same +1 min lands one bar to the
                // RIGHT of the opening candle. Undo the end-stamp shift here so the lines
                // start on the opening bar (visual parity with the original); the levels are unaffected.
                DateTime renderStart = ib.StartTime.AddMinutes(-1);
                double x1 = X(renderStart);
                double xd = X(renderStart + (IbEndTime - IbOpenTime));
                double x2 = X(ib.EndTime);
                double xod = X(renderStart + (OrEndTime - IbOpenTime));

                double yor = Y(ib.OpenPrice);
                double yh = Y(ib.InitialBalanceHigh);
                double yl = Y(ib.InitialBalanceLow);
                double yoh = Y(ib.OpeningRangeLow);    // the original naming: yoh = Y of the OR LOW
                double yol = Y(ib.OpeningRangeHigh);   //            yol = Y of the OR HIGH
                double ymid = Y(ib.OpeningRangeLow + (ib.OpeningRangeHigh - ib.OpeningRangeLow) / 2);
                double ySessionHi = Y(ib.SessionHigh);
                double ySessionLow = Y(ib.SessionLow);
                double ym = (yh + yl) / 2.0;
                double xEnd = x2;

                if (rightEdge)
                {
                    // Compact 100-px band at the right edge for the current session. Lines run
                    // under the labels, so the labels go through the same de-overlap as Session mode.
                    var rightLabels = new List<LevelLabel>(9);

                    if (ShowSessionHighLow)
                    {
                        Line(bandLeft, ySessionHi, bandRight, ySessionHi, sessionHigh);
                        Line(bandLeft, ySessionLow, bandRight, ySessionLow, sessionLow);
                        rightLabels.Add(new LevelLabel(ySessionHi, PriceLabel("Session High", ib.SessionHigh, 0), SessionHighColor, Below: false));
                        rightLabels.Add(new LevelLabel(ySessionLow, PriceLabel("Session-low", ib.SessionLow, 0), SessionLowColor, Below: true));
                    }

                    if (ShowOR)
                    {
                        double orRange = ib.OpeningRangeHigh - ib.OpeningRangeLow;
                        Line(bandLeft, yoh, bandRight, yoh, orHiLo);
                        Line(bandLeft, yol, bandRight, yol, orHiLo);
                        Line(bandLeft, ymid, bandRight, ymid, orMid);
                        Line(bandLeft, yor, bandRight, yor, sessionOpen);
                        rightLabels.Add(new LevelLabel(yor, PriceLabel("OR-open", ib.OpenPrice, 0), SessionOpenColor, Below: false));
                        rightLabels.Add(new LevelLabel(Y(ib.OpeningRangeHigh), PriceLabel("OR-high", ib.OpeningRangeHigh, orRange), OrHiLoColor, Below: false));
                        rightLabels.Add(new LevelLabel(Y(ib.OpeningRangeLow), PriceLabel("OR-low", ib.OpeningRangeLow, orRange), OrHiLoColor, Below: true));
                        rightLabels.Add(new LevelLabel(ymid, PriceLabel("OR-mid", ib.OpeningRangeLow + orRange / 2, 0), OrMidColor, Below: false));
                    }

                    if (ShowIB && (ib.IBStarted || !ShowOR))
                    {
                        double ibRange = ib.InitialBalanceHigh - ib.InitialBalanceLow;
                        double priceIbMid = (ib.InitialBalanceHigh + ib.InitialBalanceLow) / 2.0;
                        Line(bandLeft, yh, bandRight, yh, ibVirgin);
                        Line(bandLeft, yl, bandRight, yl, ibVirgin);
                        Line(bandLeft, ym, bandRight, ym, ibMid);
                        rightLabels.Add(new LevelLabel(yh, PriceLabel("IB-high", ib.InitialBalanceHigh, ibRange), IbVirginColor, Below: false));
                        rightLabels.Add(new LevelLabel(ym, PriceLabel("IB-mid", priceIbMid, 0), IbMidColor, Below: false));
                        rightLabels.Add(new LevelLabel(yl, PriceLabel("IB-low", ib.InitialBalanceLow, ibRange), IbVirginColor, Below: true));
                    }

                    DrawStackedLevelLabels(rightLabels, labelRightX);

                    continue;   // skip the session-spanning rendering in right-edge mode
                }

                if (ib.EndTime.Date == now.Date)
                    xEnd += 140;
                xEnd = Math.Min(xEnd, chartWidth);

                // The level lines run to their natural right end; only the LABEL anchor (xEnd)
                // is pulled left to keep the widest label on-panel. The original charts carry right-margin
                // padding so the last bar is never at the panel edge; SabrTrader draws it AT the edge,
                // so for a freshly-opened session — whose open sits within a label-width of the
                // right edge — pulling xEnd left would drag it LEFT of x1, drawing the x1→xEnd-20
                // levels backwards, past the session open (looking as if the OR began minutes
                // early). Anchoring the lines on xLineEnd instead keeps them open→right-edge.
                double xLineEnd = xEnd;

                if (i == sessions.Length - 1)
                {
                    // Keep the widest label of the latest session inside the panel (the original).
                    string text = "OR-open";
                    if (ShowPrice) text = text + " (" + MeasureFormatting.FormatPrice(ib.OpenPrice, _tickSize) + ")";
                    if (ShowRange) text = string.Format("{0} / {1:0.00} pts", text, ib.SessionHigh - ib.SessionLow);

                    double w = TextWidth(text);
                    if (xEnd + w + 5 > chartWidth) xEnd = chartWidth - (w + 5);
                }

                if (ShowSessionHighLow)
                {
                    Line(x1, ySessionHi, xLineEnd - 20, ySessionHi, sessionHigh);
                    Line(x1, ySessionLow, xLineEnd - 20, ySessionLow, sessionLow);
                }

                if (ShowOR)
                {
                    double xor = X(renderStart.AddMinutes(OpeningRangeMinutes));
                    if (xor > xEnd)
                    {
                        // OR window still forming — developing lines only.
                        Line(x1, yoh, xor, yoh, orDeveloping);
                        Line(x1, yol, xor, yol, orDeveloping);
                        Line(x1, ymid, xor, ymid, orMid);
                    }
                    else
                    {
                        Line(x1, yoh, xor, yoh, orDeveloping);
                        Line(x1, yol, xor, yol, orDeveloping);
                        Line(xor, yoh, xLineEnd - 20, yoh, orHiLo);
                        Line(xor, yol, xLineEnd - 20, yol, orHiLo);
                        Line(x1, ymid, xLineEnd - 20, ymid, orMid);
                    }

                    Line(x1, yor, xLineEnd - 20, yor, sessionOpen);

                    if (OrBuildingAreaOpacity > 0)
                        FillRect(x1, yoh, xod - x1, yol - yoh, WithOpacity(OrBuildingAreaColor, OrBuildingAreaOpacity));

                    if (OrBackgroundOpacity > 0 && x2 > xod)
                        FillRect(xod, yoh, x2 - xod, yol - yoh, WithOpacity(OrBackgroundColor, OrBackgroundOpacity));
                }

                if (ShowIB && (ib.IBStarted || !ShowOR))
                {
                    double x3 = X(renderStart.AddMinutes(InitialBalanceMinutes));
                    StrokeDef stroke = ibDeveloping;
                    double xStart = x1;
                    if (xEnd >= x3)
                    {
                        // IB window complete — developing segment up to the IB end,
                        // then virgin/broken lines onward.
                        Line(x1, yl, x3, yl, ibDeveloping);
                        Line(x1, yh, x3, yh, ibDeveloping);
                        Line(x1, ym, x3, ym, ibDeveloping);
                        stroke = ibVirgin;
                        xStart = x3;
                    }

                    if (ib.IBHighBroken)
                    {
                        double xBroken = X(ib.IBHighBreakDateTime);
                        Line(xStart, yh, xBroken, yh, stroke);
                        Line(xBroken, yh, xLineEnd - 20, yh, ibBroken);
                    }
                    else
                    {
                        Line(xStart, yh, xLineEnd - 20, yh, stroke);
                    }

                    if (ib.IBLowBroken)
                    {
                        double xBroken = X(ib.IBLowBreakDateTime);
                        Line(xStart, yl, xBroken, yl, stroke);
                        Line(xBroken, yl, xLineEnd - 20, yl, ibBroken);
                    }
                    else
                    {
                        Line(xStart, yl, xLineEnd - 20, yl, stroke);
                    }

                    Line(xStart, ym, xLineEnd - 20, ym, ibMid);

                    if (IbBuildingAreaOpacity > 0)
                        FillRect(x1, yh, xd - x1, yl - yh, WithOpacity(IbBuildingAreaColor, IbBuildingAreaOpacity));

                    if (x2 > xd && IbAreaOpacity > 0)
                        FillRect(xd, yh, x2 - xd, yl - yh, WithOpacity(IbAreaColor, IbAreaOpacity));
                }

                // Level labels, de-overlapped so none is bisected by its own line and none
                // overlaps another: lows fan downward beneath their line, the rest upward above
                // it, each separated by a text row. The original's pairwise nudges left the middle label of
                // a coincident cluster (e.g. Session-low == OR-low) centred ON the line; this
                // resolves any number of coincidences cleanly.
                var labels = new List<LevelLabel>(9);

                if (ShowSessionHighLow)
                {
                    labels.Add(new LevelLabel(Y(ib.SessionHigh), PriceLabel("Session High", ib.SessionHigh, 0), SessionHighColor, Below: false));
                    labels.Add(new LevelLabel(Y(ib.SessionLow), PriceLabel("Session-low", ib.SessionLow, 0), SessionLowColor, Below: true));
                }

                if (ShowOR)
                {
                    double orRange = ib.OpeningRangeHigh - ib.OpeningRangeLow;
                    labels.Add(new LevelLabel(yor, PriceLabel("OR-open", ib.OpenPrice, 0), SessionOpenColor, Below: false));
                    labels.Add(new LevelLabel(Y(ib.OpeningRangeHigh), PriceLabel("OR-high", ib.OpeningRangeHigh, orRange), OrHiLoColor, Below: false));
                    labels.Add(new LevelLabel(Y(ib.OpeningRangeLow), PriceLabel("OR-low", ib.OpeningRangeLow, orRange), OrHiLoColor, Below: true));
                    labels.Add(new LevelLabel(ymid, PriceLabel("OR-mid", ib.OpeningRangeLow + orRange / 2, 0), OrMidColor, Below: false));
                }

                if (ShowIB && (ib.IBStarted || !ShowOR))
                {
                    double ibRange = ib.InitialBalanceHigh - ib.InitialBalanceLow;
                    double priceIbMid = (ib.InitialBalanceHigh + ib.InitialBalanceLow) / 2.0;
                    labels.Add(new LevelLabel(yh, PriceLabel("IB-high", ib.InitialBalanceHigh, ibRange), IbVirginColor, Below: false));
                    labels.Add(new LevelLabel(ym, PriceLabel("IB-mid", priceIbMid, 0), IbMidColor, Below: false));
                    labels.Add(new LevelLabel(yl, PriceLabel("IB-low", ib.InitialBalanceLow, ibRange), IbVirginColor, Below: true));
                }

                DrawStackedLevelLabels(labels, xEnd);

                if (ShowPreviousIBExtensions)
                    DrawExtensions(sessions, ib);
            }

            if (!ShowPreviousIBExtensions && ShowTodaysIBExtensions && sessions.Length > 0)
                DrawExtensions(sessions, sessions[^1]);

            if (ShowStatistics)
                RenderStatistics(sessions);
        }
        finally
        {
            _r.PopClip();
        }
    }

    // ───── IB extensions (the original DrawExtensions) ────────────────────────────────

    private void DrawExtensions(IbSession[] sessions, IbSession ib)
    {
        var ext100 = new StrokeDef(Ib100ExtensionColor, Ib100ExtensionWidth, Ib100ExtensionStyle);
        var ext200 = new StrokeDef(Ib200ExtensionColor, Ib200ExtensionWidth, Ib200ExtensionStyle);
        var ext300 = new StrokeDef(Ib300ExtensionColor, Ib300ExtensionWidth, Ib300ExtensionStyle);
        var ext400 = new StrokeDef(Ib400ExtensionColor, Ib400ExtensionWidth, Ib400ExtensionStyle);

        DateTime renderStart = ib.StartTime.AddMinutes(-1);   // see OnCustomRender: undo the original end-stamp +1 for X anchoring
        double chartWidth = _plotArea.X + _plotArea.Width;
        double x1 = X(renderStart);
        double x2 = X(ib.EndTime);
        if (sessions.Length > 0 && ReferenceEquals(ib, sessions[^1]))
        {
            double w = TextWidth("IB 200%") + 5;
            if (x2 + w > chartWidth) x2 = chartWidth - w;
        }

        double xd = X(renderStart + (IbEndTime - IbOpenTime));
        if (x2 <= xd) return;   // IB window not complete yet

        double diffPrice = ib.InitialBalanceHigh - ib.InitialBalanceLow;

        // Downside extensions — drawn once the session traded at least halfway to each level.
        for (int n = 1; n <= 6; n++)
        {
            if (ib.SessionLow > ib.InitialBalanceLow - (n - 0.5) * diffPrice) continue;
            double y = Y(ib.InitialBalanceLow - n * diffPrice);
            // The original parity: levels 5 and 6 reuse the 400% stroke (so does 100-400 their own).
            StrokeDef s = n switch { 1 => ext100, 2 => ext200, 3 => ext300, _ => ext400 };
            Line(x1, y, x2 - 20, y, s);
            DrawTextLabel($"IB {n}00%", x2, y, IbVirginColor);
        }

        // Upside extensions.
        for (int n = 1; n <= 6; n++)
        {
            if (ib.SessionHigh < ib.InitialBalanceHigh + (n - 0.5) * diffPrice) continue;
            double y = Y(ib.InitialBalanceHigh + n * diffPrice);
            StrokeDef s = n switch { 1 => ext100, 2 => ext200, 3 => ext300, _ => ext400 };
            Line(x1, y, x2, y, s);
            DrawTextLabel($"IB {n}00%", x2, y, IbVirginColor);
        }
    }

    // ───── Statistics card (the original RenderStatistics) ────────────────────────────

    private void RenderStatistics(IbSession[] sessions)
    {
        int totalDays = sessions.Length;
        int ibBrokenCount = 0;
        int ibBrokenLow = 0;
        int ibBrokenHigh = 0;
        double totalPercentageLow = 0;
        double totalPercentageHigh = 0;

        for (int i = 0; i < sessions.Length; i++)
        {
            IbSession ib = sessions[i];
            double range = ib.InitialBalanceHigh - ib.InitialBalanceLow;
            if (ib.SessionLow < ib.InitialBalanceLow || ib.SessionHigh > ib.InitialBalanceHigh)
                ibBrokenCount++;
            if (ib.SessionLow < ib.InitialBalanceLow)
            {
                ibBrokenLow++;
                totalPercentageLow += 100 * ((ib.InitialBalanceLow - ib.SessionLow) / range);
            }
            if (ib.SessionHigh > ib.InitialBalanceHigh)
            {
                ibBrokenHigh++;
                totalPercentageHigh += 100 * ((ib.SessionHigh - ib.InitialBalanceHigh) / range);
            }
        }

        totalPercentageLow = ibBrokenLow > 0 ? totalPercentageLow / ibBrokenLow : 0;
        totalPercentageHigh = ibBrokenHigh > 0 ? totalPercentageHigh / ibBrokenHigh : 0;

        double h = _plotArea.Y + _plotArea.Height;
        double lineHeight = FontHeight + 6;
        double tableWidth = TextWidth("Avg break IB highhigh 200.93%200.93%");
        double tableHeight = 9 * lineHeight;
        double xpos = 20;
        double ypos = h - 50 - tableHeight;

        if (StatisticsBackgroundOpacity > 0)
            FillRect(xpos, ypos, tableWidth, tableHeight, WithOpacity(StatisticsBackgroundColor, StatisticsBackgroundOpacity));

        xpos += 10;
        ypos += 20;
        double column1 = TextWidth("Avg break IB highhigh");

        DrawTextLabel(Bars.Spec.InstrumentId, xpos, ypos, TextColor);
        ypos += lineHeight;
        ypos += lineHeight;

        DrawTextLabel("Days", xpos, ypos, TextColor);
        DrawTextLabel(totalDays.ToString(), xpos + column1, ypos, TextColor);

        ypos += lineHeight;
        DrawTextLabel("IB Broken", xpos, ypos, TextColor);
        DrawTextLabel(GetPercentage(ibBrokenCount, totalDays), xpos + column1, ypos, TextColor);

        ypos += lineHeight;
        DrawTextLabel("IB low Broken", xpos, ypos, TextColor);
        DrawTextLabel(GetPercentage(ibBrokenLow, totalDays), xpos + column1, ypos, TextColor);

        ypos += lineHeight;
        DrawTextLabel("IB high Broken", xpos, ypos, TextColor);
        DrawTextLabel(GetPercentage(ibBrokenHigh, totalDays), xpos + column1, ypos, TextColor);

        ypos += lineHeight;
        DrawTextLabel("Avg break IB low", xpos, ypos, TextColor);
        DrawTextLabel(string.Format("{0:0.00}%", totalPercentageLow), xpos + column1, ypos, TextColor);

        ypos += lineHeight;
        DrawTextLabel("Avg break IB high", xpos, ypos, TextColor);
        DrawTextLabel(string.Format("{0:0.00}%", totalPercentageHigh), xpos + column1, ypos, TextColor);
    }

    private static string GetPercentage(double partial, double total)
    {
        double percentage = total > 0 ? partial / total * 100.0 : 0;
        return string.Format("{0:0.00}%", percentage);
    }
}
