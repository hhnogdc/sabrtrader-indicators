using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rollover Dates — marks futures contract-rollover dates with a vertical line (the original
/// <c>Rollover</c>). The roll instants come from the host through the agnostic
/// <see cref="IIndicatorContext.GetContractRolloversUtc"/> seam (the desktop platform wires it to
/// the existing contract calendar / roll-schedule services); the indicator itself is venue-agnostic
/// and renders nothing for non-futures or when no resolver is installed.
///
/// <para>The roll instants are fetched compute-side over the loaded bar range (and refreshed as live
/// bars extend it), then drawn at render time: each instant is mapped to the bar that covers it and
/// a vertical line is painted at that bar's x. A single transparent plot gives the indicator a
/// legend presence (mirrors the News overlay); it carries no data.</para>
/// </summary>
[IndicatorDescription("Rollover Dates: draws a vertical line at each futures contract-rollover date (the day a continuous chart rolls to the next contract), sourced from the platform's contract calendar. Futures-specific; renders nothing on non-futures instruments.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class RolloverDates : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _signal = new();
    private DateTime[] _rollovers = Array.Empty<DateTime>();
    private DateTime _fetchedToUtc = DateTime.MinValue;

    public RolloverDates(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // Transparent legend-only plot (no data) — same pattern as the News overlay.
        AddPlot(new Plot("Rollover", _signal, PlotStyle.Line, ChartColor.Transparent, 1.0)
        {
            Visible = false,
            ShowPriceMarker = false,
            Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None,
                "marker overlay: vertical line at each contract-rollover date"),
        });
    }

    [IndicatorParameter(DisplayName = "Line Width", Group = "01. Display", Order = 1, MinValue = 1, MaxValue = 10, Step = 1)]
    public int LineWidth { get; set; } = 1;

    [IndicatorParameter(DisplayName = "Show Label", Group = "01. Display", Order = 2)]
    public bool ShowLabel { get; set; } = true;

    [IndicatorParameter(DisplayName = "Label Size", Group = "01. Display", Order = 3, MinValue = 5, MaxValue = 40, Step = 1)]
    public int LabelSize { get; set; } = 10;

    public ChartColor LineColor { get; set; } = PlotColors.Goldenrod;
    public ChartColor LabelColor { get; set; } = PlotColors.Goldenrod;

    /// <summary>The rollover instants currently held (true venue UTC). Exposed for tests/diagnostics.</summary>
    public IReadOnlyList<DateTime> Rollovers => _rollovers;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LineWidth < 1) throw new ArgumentOutOfRangeException(nameof(LineWidth));
        _rollovers = Array.Empty<DateTime>();
        _fetchedToUtc = DateTime.MinValue;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) _signal.Append(double.NaN);
        Fetch(ctx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        _signal.Append(double.NaN);
        // Refresh only when live bars have extended the range past what we already queried.
        if (Bars[0].EndUtc > _fetchedToUtc) Fetch(ctx);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _rollovers = Array.Empty<DateTime>();
        _fetchedToUtc = DateTime.MinValue;
    }

    private void Fetch(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        var fromUtc = bars[bars.Count - 1].StartUtc;   // oldest bar open
        var toUtc = bars[0].EndUtc;                    // newest bar close
        var list = ctx.GetContractRolloversUtc(fromUtc, toUtc);
        var arr = new DateTime[list.Count];
        for (int i = 0; i < list.Count; i++) arr[i] = list[i];
        Array.Sort(arr);
        _rollovers = arr;
        _fetchedToUtc = toUtc;
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_rollovers.Length == 0) return;
        var bars = ctx.Bars;
        if (bars is null || bars.Count == 0) return;
        ChartViewport vp = ctx.Viewport;
        IChartRenderer r = ctx.Renderer;

        foreach (var t in _rollovers)
        {
            int abs = FindBarIndex(bars, t);
            if (abs < vp.FirstVisibleBarIdx || abs > vp.LastVisibleBarIdx) continue;
            double x = vp.BarIndexToPixelX(abs);
            r.DrawLine(x, ctx.PlotArea.Y, x, ctx.PlotArea.Bottom, LineColor, LineWidth);
            if (ShowLabel)
                r.DrawText(x + 2, ctx.ContentTopPx + 2, "Roll", LabelColor, LabelSize);
        }
    }

    /// <summary>Absolute index (0 = oldest) of the bar whose open is the latest at or before
    /// <paramref name="t"/>; -1 when <paramref name="t"/> precedes the first bar.</summary>
    private static int FindBarIndex(IBarSeries bars, DateTime t)
    {
        int n = bars.Count;
        // bars[barsAgo] with barsAgo 0 = newest; absolute index = (n-1) - barsAgo.
        int lo = 0, hi = n - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;                  // absolute index
            DateTime start = bars[n - 1 - mid].StartUtc;
            if (start <= t) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }
}
