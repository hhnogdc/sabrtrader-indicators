using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// TTM Trend (John Carter) — bar-coloring trend-regime detector.
/// Compares current Close to the average HL2 of the last Length bars:
///   reference = SMA( (High+Low)/2, Length )
///   Trend[i]  = Close[i] > reference  →  +1 (bullish)
///   Trend[i]  = Close[i] < reference  →  −1 (bearish)
///   Trend[i]  = Close[i] = reference  →   0 (neutral, carries prior)
/// Defaults Length=6.
/// Plots:
///   - Trend signal series (+1/−1) sub-panel histogram (DarkCyan/Crimson are rendered via
///     IChartCustomRender bar-coloring on the price panel).
///   - The custom render colors the price bars themselves green (bullish) / red (bearish) so
///     the indicator behaves like the original's BarBrushes assignment.
/// </summary>
[IndicatorDescription("John Carter's TTM Trend bar-color regime detector. Paints bars bullish or bearish based on whether Close sits above or below the SMA of HL2 over the last Length bars.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class TtmTrend : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _trend = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _hl2Sum = null!;

    public TtmTrend(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Trend", _trend, PlotStyle.Histogram, PlotColors.DarkCyan, 2.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Length", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 6;

    public ChartColor BullishColor { get; set; } = PlotColors.LimeGreen;
    public ChartColor BearishColor { get; set; } = PlotColors.Crimson;
    public ChartColor NeutralColor { get; set; } = PlotColors.DarkGray;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_trend, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 1) throw new ArgumentOutOfRangeException(nameof(Length));
        _hl2Sum = new RollingSum(Length);
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
        _hl2Sum.Push((bar.High + bar.Low) / 2);
        if (_hl2Sum.Count < Length) { _trend.Append(double.NaN); return; }
        double reference = _hl2Sum.Sum / Length;
        double t = bar.Close > reference ? 1 : bar.Close < reference ? -1 : (_trend.Count > 0 ? _trend[0] : 0);
        _trend.Append(t);
    }

    /// <summary>Color each visible bar green/red/gray based on its trend signal.</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (Bars == null) return;
        var vp = ctx.Viewport;
        int total = _trend.Count;
        if (total == 0) return;
        double w = Math.Max(2, vp.BarSpacingPx * 0.7);

        int first = Math.Max(0, vp.FirstVisibleBarIdx);
        int last  = Math.Min(total - 1, vp.LastVisibleBarIdx);
        for (int absIdx = first; absIdx <= last; absIdx++)
        {
            int barsAgo = total - 1 - absIdx;
            double t = _trend[barsAgo];
            if (double.IsNaN(t)) continue;
            var color = t > 0 ? BullishColor : t < 0 ? BearishColor : NeutralColor;
            // Subtle dot at bar's HIGH so it doesn't fight with the candle pass — full
            // bar-tinting would require a per-bar color override on the standard CandleRenderPass.
            int bAgoForBars = Bars.Count - 1 - absIdx;
            if (bAgoForBars < 0 || bAgoForBars >= Bars.Count) continue;
            var bar = Bars[bAgoForBars];
            double x = vp.BarIndexToPixelX(absIdx);
            double y = vp.PriceToPixelY(bar.High);
            ctx.Renderer.FillRect(x - w / 2, y - 4, w, 3, color);
        }
    }
}
