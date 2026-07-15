using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Gaps — 1-on-1 port of ATAS <c>Gaps</c>. Detects BAR-TO-BAR price gaps (distinct from the
/// 3-bar imbalance <see cref="Fvg"/>):
/// <list type="bullet">
///   <item><b>Bullish</b> when <c>bar.Low &gt; prevBar.High</c> — gap band [prevBar.High, bar.Low].</item>
///   <item><b>Bearish</b> when <c>bar.High &lt; prevBar.Low</c> — gap band [bar.High, prevBar.Low].</item>
/// </list>
/// A gap is kept only when its size is at least <see cref="MinDeviation"/> percent of a 14-period
/// SMA of the bar range (High−Low). Each open gap is drawn as a filled rectangle extending to the
/// right until it is filled:
/// <list type="bullet">
///   <item><see cref="CloseGapsPartially"/>=false (default): the gap is removed on the FIRST bar
///   that trades back into it.</item>
///   <item><see cref="CloseGapsPartially"/>=true: the rectangle shrinks toward the gap origin as
///   price eats into it, and is removed once fully filled.</item>
/// </list>
/// <see cref="LimitMaxGapBodyLength"/> force-closes a gap after
/// <see cref="MaxGapBodyLength"/> bars.
/// </summary>
[IndicatorDescription("Gaps: detects bar-to-bar price gaps (bar.Low > prevBar.High bullish, bar.High < prevBar.Low bearish) that exceed MinDeviation% of the 14-bar average range, and draws each as a band that fills (fully or partially) as price trades back into it.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Gaps : IndicatorBase, IChartCustomRender
{
    /// <summary>One open gap. <see cref="Top"/> &gt; <see cref="Bottom"/>; for partial fills the
    /// edges move toward each other as price penetrates.</summary>
    public sealed class Gap
    {
        public int BarIndex { get; }      // bar that created the gap (rectangle left edge)
        public bool IsBull { get; }
        public double Top { get; internal set; }
        public double Bottom { get; internal set; }
        public double Midpoint => (Top + Bottom) / 2.0;

        internal Gap(int barIndex, bool isBull, double top, double bottom)
        {
            BarIndex = barIndex; IsBull = isBull; Top = top; Bottom = bottom;
        }

        /// <summary>Apply one later candle. Returns true when the gap is now fully closed (drop it).</summary>
        internal bool Update(in Bar c, int bar, bool partial, bool limitOn, int maxLen)
        {
            if (limitOn && bar - BarIndex >= maxLen) return true;
            if (IsBull)
            {
                if (c.Low < Bottom) return true;              // fully filled
                if (c.Low < Top) { if (partial) Top = c.Low; else return true; }
            }
            else
            {
                if (c.High > Top) return true;                // fully filled
                if (c.High > Bottom) { if (partial) Bottom = c.High; else return true; }
            }
            return false;
        }
    }

    private const int RangeSmaPeriod = 14;

    private readonly ChunkedSeries<double> _upMid = new();
    private readonly ChunkedSeries<double> _downMid = new();
    private IndicatorOutputSeries? _upW, _downW;

    private readonly List<Gap> _gaps = new();
    private RollingSum _rangeSum = null!;
    private int _barIdx;

    public Gaps(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var up = new ChartColor(NTColors.Green.R, NTColors.Green.G, NTColors.Green.B, 0x66);
        var dn = new ChartColor(NTColors.Red.R, NTColors.Red.G, NTColors.Red.B, 0x66);
        AddPlot(new Plot("Up Gap", _upMid, PlotStyle.Dots, up, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Down Gap", _downMid, PlotStyle.Dots, dn, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "Close Gaps Partially", Description = "When on, a gap rectangle shrinks as price partially fills it; when off (default) the gap is removed on first penetration.",
        Group = "01. Detection", Order = 1)]
    public bool CloseGapsPartially { get; set; } = false;

    [IndicatorParameter(DisplayName = "Min Deviation %", Description = "Minimum gap size as a percentage of the 14-bar average range (High-Low).",
        Group = "01. Detection", Order = 2, MinValue = 1, MaxValue = 100, Step = 1)]
    public int MinDeviation { get; set; } = 30;

    [IndicatorParameter(DisplayName = "Limit Max Gap Length", Description = "When on, force-close a gap after Max Gap Length bars even if unfilled.",
        Group = "01. Detection", Order = 3)]
    public bool LimitMaxGapBodyLength { get; set; } = false;

    [IndicatorParameter(DisplayName = "Max Gap Length", Description = "Bars after which an unfilled gap is force-closed (only when Limit Max Gap Length is on).",
        Group = "01. Detection", Order = 4, MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int MaxGapBodyLength { get; set; } = 300;

    [IndicatorParameter(DisplayName = "Max Gaps", Description = "Cap on simultaneously tracked open gaps (oldest evicted first) — bounds memory / render cost.",
        Group = "01. Detection", Order = 5, MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int MaxGaps { get; set; } = 50;

    [IndicatorParameter(DisplayName = "Transparency", Description = "Fill transparency 0 (opaque) .. 10 (invisible).",
        Group = "02. Display", Order = 1, MinValue = 0, MaxValue = 10, Step = 1)]
    public int Transparency { get; set; } = 6;

    [IndicatorParameter(DisplayName = "Hide Border", Group = "02. Display", Order = 2)]
    public bool HideBorder { get; set; } = false;

    [IndicatorParameter(DisplayName = "Border Width", Group = "02. Display", Order = 3, MinValue = 1, MaxValue = 10, Step = 1)]
    public int BorderWidth { get; set; } = 2;

    [IndicatorParameter(DisplayName = "Show Label", Group = "02. Display", Order = 4)]
    public bool ShowLabel { get; set; } = true;

    [IndicatorParameter(DisplayName = "Label Size", Group = "02. Display", Order = 5, MinValue = 5, MaxValue = 40, Step = 1)]
    public int LabelSize { get; set; } = 10;

    public ChartColor BullishColor { get; set; } = NTColors.Green;
    public ChartColor BearishColor { get; set; } = NTColors.Red;
    public ChartColor LabelColor { get; set; } = NTColors.Gray;

    /// <summary>Currently-open gaps (oldest first).</summary>
    public IReadOnlyList<Gap> OpenGaps => _gaps;

    public ISeries<double> UpGapMidpoints => _upW ??= new IndicatorOutputSeries(_upMid, Bars);
    public ISeries<double> DownGapMidpoints => _downW ??= new IndicatorOutputSeries(_downMid, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (MinDeviation < 1) throw new ArgumentOutOfRangeException(nameof(MinDeviation));
        if (MaxGapBodyLength < 1) throw new ArgumentOutOfRangeException(nameof(MaxGapBodyLength));
        if (MaxGaps < 1) throw new ArgumentOutOfRangeException(nameof(MaxGaps));
        if (BorderWidth < 1) throw new ArgumentOutOfRangeException(nameof(BorderWidth));
        _rangeSum = new RollingSum(RangeSmaPeriod);
        _gaps.Clear();
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars, 0);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _gaps.Clear();
        _barIdx = 0;
    }

    private void Consume(IBarSeries bars, int barsAgo)
    {
        _upMid.Append(double.NaN);
        _downMid.Append(double.NaN);

        var c = bars[barsAgo];
        _rangeSum.Push(c.High - c.Low);
        double sma = _rangeSum.Count > 0 ? _rangeSum.Sum / _rangeSum.Count : 0;

        // Fill/shrink existing gaps against the current candle (a gap never filled by its own
        // creating bar — new gap is added AFTER this pass).
        for (int i = _gaps.Count - 1; i >= 0; i--)
        {
            if (_gaps[i].Update(in c, _barIdx, CloseGapsPartially, LimitMaxGapBodyLength, MaxGapBodyLength))
                _gaps.RemoveAt(i);
        }

        if (_barIdx >= 1)
        {
            var p = bars[barsAgo + 1];
            double threshold = sma / 100.0 * MinDeviation;

            if (c.Low > p.High && (c.Low - p.High) >= threshold)
            {
                _gaps.Add(new Gap(_barIdx, isBull: true, top: c.Low, bottom: p.High));
                if (_upMid.Count > 0) _upMid.Set(0, (c.Low + p.High) / 2.0);
            }
            else if (c.High < p.Low && (p.Low - c.High) >= threshold)
            {
                _gaps.Add(new Gap(_barIdx, isBull: false, top: p.Low, bottom: c.High));
                if (_downMid.Count > 0) _downMid.Set(0, (p.Low + c.High) / 2.0);
            }
        }

        // Bound memory: drop the oldest open gaps beyond the cap.
        while (_gaps.Count > MaxGaps) _gaps.RemoveAt(0);

        _barIdx++;
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_gaps.Count == 0) return;
        IChartRenderer r = ctx.Renderer;
        ChartViewport vp = ctx.Viewport;
        byte alpha = (byte)Math.Clamp(255 * (10 - Transparency) / 10, 0, 255);

        foreach (var g in _gaps)
        {
            int rightBar = vp.LastVisibleBarIdx;
            if (rightBar < g.BarIndex) continue;

            double x = vp.BarIndexToPixelX(g.BarIndex);
            double w = vp.BarIndexToPixelX(rightBar) - x;
            if (w < 1) w = 1;
            double yTop = vp.PriceToPixelY(g.Top);
            double yBot = vp.PriceToPixelY(g.Bottom);
            double y = Math.Min(yTop, yBot);
            double h = Math.Abs(yBot - yTop);
            if (h < 1) h = 1;

            ChartColor baseColor = g.IsBull ? BullishColor : BearishColor;
            var fill = new ChartColor(baseColor.R, baseColor.G, baseColor.B, alpha);
            r.FillRect(x, y, w, h, fill);

            if (!HideBorder)
            {
                var border = new ChartColor(baseColor.R, baseColor.G, baseColor.B, 0xFF);
                r.DrawLine(x, y, x + w, y, border, BorderWidth);
                r.DrawLine(x, y + h, x + w, y + h, border, BorderWidth);
                r.DrawLine(x, y, x, y + h, border, BorderWidth);
                r.DrawLine(x + w, y, x + w, y + h, border, BorderWidth);
            }

            if (ShowLabel)
                r.DrawText(x + 2, y + 1, g.IsBull ? "Gap ▲" : "Gap ▼", LabelColor, LabelSize);
        }
    }
}
