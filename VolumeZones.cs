using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Zones.
/// Splits the visible price range into <see cref="BarCount"/> bins (default 10) and
/// accumulates Volume from each visible bar into its bin, split up (Close &gt; Open)
/// vs down (Close ≤ Open). Renders horizontal histograms anchored at the chart's
/// left edge via IChartCustomRender.
/// Defaults BarCount=10, BarSpacing=1, Opacity=50, BarUpBrush=DarkCyan, BarDownBrush=Crimson,
/// LineBrush=DarkGray, DrawLines=false, IsOverlay=true (no plot data series).
/// </summary>
[IndicatorDescription("Horizontal volume histogram. Buckets the visible price range into N bins and stacks bar volume into each bin, split by up/down close to show high-activity price zones.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class VolumeZones : IndicatorBase, IChartCustomRender
{
    public VolumeZones(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // No standard plot series — custom render only. Add a placeholder
        // plot for legend / settings dialog discoverability.
        AddPlot(new Plot("Volume Zones", new ChunkedSeries<double>(),
            PlotStyle.Histogram, PlotColors.DarkCyan, 1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "BarCount", MinValue = 2, MaxValue = 20, Step = 1)]
    public int BarCount { get; set; } = 10;

    [IndicatorParameter(DisplayName = "BarSpacing", MinValue = 0, MaxValue = 5, Step = 1)]
    public int BarSpacing { get; set; } = 1;

    [IndicatorParameter(DisplayName = "Opacity", MinValue = 10, MaxValue = 100, Step = 5)]
    public double Opacity { get; set; } = 50;

    [IndicatorParameter(DisplayName = "DrawLines")]
    public bool DrawLines { get; set; } = false;

    public ChartColor BarUpBrush   { get; set; } = PlotColors.DarkCyan;
    public ChartColor BarDownBrush { get; set; } = PlotColors.Crimson;
    public ChartColor LineBrush    { get; set; } = PlotColors.DarkGray;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (BarCount < 2 || BarCount > 20) throw new ArgumentOutOfRangeException(nameof(BarCount));
    }

    public override void OnBarUpdate(IIndicatorContext ctx) { /* bar-driven repaint comes from the chart's normal cycle */ }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (Bars == null || Bars.Count == 0) return;
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;

        int first = Math.Max(0, vp.FirstVisibleBarIdx);
        int last = Math.Min(Bars.Count - 1, vp.LastVisibleBarIdx);
        if (first > last) return;

        // Walk the visible range to compute price extremes.
        double highPrice = double.MinValue, lowPrice = double.MaxValue;
        for (int absIdx = first; absIdx <= last; absIdx++)
        {
            int barsAgo = Bars.Count - 1 - absIdx;
            var bar = Bars[barsAgo];
            if (bar.High > highPrice) highPrice = bar.High;
            if (bar.Low  < lowPrice)  lowPrice  = bar.Low;
        }
        if (highPrice <= lowPrice) return;

        int bins = BarCount;
        double range = highPrice - lowPrice;
        double binSize = range / bins;

        // Accumulate volume per bin (up / down split by Close vs Open).
        Span<double> upVol = stackalloc double[bins];
        Span<double> dnVol = stackalloc double[bins];
        double volumeMax = 0;
        for (int absIdx = first; absIdx <= last; absIdx++)
        {
            int barsAgo = Bars.Count - 1 - absIdx;
            var bar = Bars[barsAgo];
            double price = bar.Close;
            int bin = (int)Math.Min(bins - 1, Math.Max(0, (price - lowPrice) / binSize));
            if (bar.Close > bar.Open) upVol[bin] += bar.Volume;
            else                       dnVol[bin] += bar.Volume;
        }
        for (int i = 0; i < bins; i++)
        {
            double t = upVol[i] + dnVol[i];
            if (t > volumeMax) volumeMax = t;
        }
        if (volumeMax <= 0) return;

        double plotX = ctx.PlotArea.X;
        double plotH = ctx.PlotArea.Height;
        double halfW = plotH / 2;       // matches the original's chartScale.Height / 2 horizontal scaling.

        // Apply opacity to colors.
        var upColor = WithOpacity(BarUpBrush, Opacity);
        var dnColor = WithOpacity(BarDownBrush, Opacity);

        for (int i = 0; i < Math.Min(bins, last - first + 1); i++)
        {
            double priceUpper = lowPrice + binSize * (i + 1);
            double priceLower = lowPrice + binSize * i;
            double yUpper = vp.PriceToPixelY(priceUpper) + BarSpacing;
            double yLower = vp.PriceToPixelY(priceLower);
            double barWidthUp = halfW * (upVol[i] / volumeMax);
            double barWidthDn = halfW * (dnVol[i] / volumeMax);
            double rectH = Math.Abs(yUpper - yLower);
            renderer.FillRect(plotX,                          yUpper, barWidthUp, rectH, upColor);
            renderer.FillRect(plotX + barWidthUp,             yUpper, barWidthDn, rectH, dnColor);
            if (DrawLines)
            {
                double right = ctx.PlotArea.Right;
                renderer.DrawLine(plotX, yLower, right, yLower, LineBrush, 1.0);
                if (i == bins - 1) renderer.DrawLine(plotX, yUpper, right, yUpper, LineBrush, 1.0);
            }
        }
    }

    private static ChartColor WithOpacity(ChartColor c, double opacityPct)
    {
        // True alpha — Skia / D2D blends against the live chart background, which gives
        // a real translucent overlay (no "washed out against mid-gray" artefacts on dark/
        // light themes).
        double a = Math.Min(100, Math.Max(0, opacityPct)) / 100.0;
        return new ChartColor(c.R, c.G, c.B, (byte)Math.Round(a * 255));
    }
}
