using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;
using SabrTrader.Pipeline.Indicators;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum FvgExtendUntil { Filled = 1, PartiallyFilled = 2, BarsSpecified = 3 }

/// <summary>
/// Fair Value Gap — 1-on-1 port of NT @FVG math layer.
/// A 3-bar gap exists when bar[0].Low &gt; bar[2].High (up-gap) or bar[0].High &lt; bar[2].Low (down-gap)
/// and the gap size in ticks ≥ MinimumTicks. Gaps are anchored at bar[1] (the gap-creating bar).
/// EnforceSameDirection requires all three bars to be in the same direction (close vs open).
///
/// Each gap is filled (terminated) when later bars trade into its tick range:
///   • Filled       → first revisit closes the whole gap.
///   • PartiallyFilled → each tick is terminated individually as it's revisited.
///   • BarsSpecified → all ticks terminate at gap.BarIndex + BarsToExtend (no fill detection).
///
/// Defaults: ExtendUntil=Filled, MaxFvg=10, MinimumTicks=1, BarsToExtend=10,
/// EnforceSameDirection=false, ShowMidpointLine=false, FvgColorUp=LimeGreen, FvgColorDown=Red.
/// Overlay. Rendering (filled rectangles per tick) is a host responsibility — math layer here
/// exposes the gap list, midpoints, and per-tick termination indices.
///
/// Plots: Up gap midpoint (LimeGreen Dot), Down gap midpoint (Red Dot). NaN on non-gap bars.
/// The full gap list (with per-tick state) is available via <see cref="Gaps"/>.
/// </summary>
[IndicatorDescription("Fair Value Gap: detects 3-bar imbalance gaps (bar[0].Low > bar[2].High up-gap or bar[0].High < bar[2].Low down-gap) and draws them as price-band rectangles until they're filled, partially filled, or expire.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Fvg : IndicatorBase, IChartCustomRender
{

    public sealed class Gap
    {
        public int BarIndex { get; }
        public bool IsUp { get; }
        public double Midpoint { get; }
        public double Low { get; }
        public double High { get; }
        public Dictionary<double, int> Ticks { get; } = new();

        internal Gap(double tickSize, int barIndex, double barsAgoPrice, double currentBarPrice, int barsToKeep)
        {
            BarIndex = barIndex;
            IsUp = barsAgoPrice < currentBarPrice;
            Midpoint = (barsAgoPrice + currentBarPrice) / 2.0;
            Low  = Math.Min(barsAgoPrice, currentBarPrice);
            High = Math.Max(barsAgoPrice, currentBarPrice);
            if (IsUp)
                for (double p = barsAgoPrice; p <= currentBarPrice + 1e-12; p += tickSize)
                {
                    double rp = RoundToTick(p, tickSize);
                    if (!Ticks.ContainsKey(rp))
                        Ticks.Add(rp, barsToKeep == 0 ? -1 : barIndex + barsToKeep);
                }
            else
                for (double p = barsAgoPrice; p >= currentBarPrice - 1e-12; p -= tickSize)
                {
                    double rp = RoundToTick(p, tickSize);
                    if (!Ticks.ContainsKey(rp))
                        Ticks.Add(rp, barsToKeep == 0 ? -1 : barIndex + barsToKeep);
                }
        }

        internal void Check(double tickSize, double minPrice, double maxPrice, int barIndex, bool terminateAll)
        {
            if (barIndex < BarIndex + 2) return;
            for (double p = minPrice; p <= maxPrice + 1e-12; p += tickSize)
            {
                double rp = RoundToTick(p, tickSize);
                if (Ticks.TryGetValue(rp, out int v) && v == -1)
                {
                    if (terminateAll)
                    {
                        foreach (var k in new List<double>(Ticks.Keys)) Ticks[k] = barIndex;
                        break;
                    }
                    Ticks[rp] = Math.Max(barIndex, v);
                }
            }
        }

        private static double RoundToTick(double price, double tickSize)
            => Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
    }

    private readonly ChunkedSeries<double> _upMid   = new();
    private readonly ChunkedSeries<double> _downMid = new();
    private IndicatorOutputSeries? _upW, _downW;

    private readonly SortedDictionary<int, Gap> _gaps = new();
    private int _barIdx;

    public Fvg(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // Default alpha 0x66 (~40%) — matches NT FVG's default Opacity=40 so
        // the gap rectangles ship translucent out of the box. Users tune via the
        // settings dialog's Opacity column on each plot.
        var upDefault = new ChartColor(NTColors.LimeGreen.R, NTColors.LimeGreen.G, NTColors.LimeGreen.B, 0x66);
        var dnDefault = new ChartColor(NTColors.Red.R,       NTColors.Red.G,       NTColors.Red.B,       0x66);
        AddPlot(new Plot("Up Gap",   _upMid,   PlotStyle.Dots, upDefault, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Down Gap", _downMid, PlotStyle.Dots, dnDefault, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "ExtendUntil",
        Description = "Filled, PartiallyFilled, or BarsSpecified.")]
    public FvgExtendUntil ExtendUntil { get; set; } = FvgExtendUntil.Filled;

    [IndicatorParameter(DisplayName = "MaxFvg", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int MaxFvg { get; set; } = 10;

    [IndicatorParameter(DisplayName = "MinimumTicks", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int MinimumTicks { get; set; } = 1;

    [IndicatorParameter(DisplayName = "BarsToExtend", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int BarsToExtend { get; set; } = 10;

    [IndicatorParameter(DisplayName = "EnforceSameDirection")]
    public bool EnforceSameDirection { get; set; } = false;

    [IndicatorParameter(DisplayName = "ShowMidpointLine")]
    public bool ShowMidpointLine { get; set; } = false;

    /// <summary>Tick size used for the gap-fill ledger. Pipeline does not carry instrument metadata
    /// yet, so the host supplies the tick size at construction (or via this property pre-OnInit).
    /// Defaults to 0.25 (CME index futures).</summary>
    public double TickSize { get; set; } = 0.25;

    /// <summary>Snapshot of active gaps keyed by anchor bar index (BarIndex of the middle bar).</summary>
    public IReadOnlyDictionary<int, Gap> Gaps => _gaps;

    public ISeries<double> UpGapMidpoints   => _upW   ??= new IndicatorOutputSeries(_upMid,   Bars);
    public ISeries<double> DownGapMidpoints => _downW ??= new IndicatorOutputSeries(_downMid, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (MaxFvg < 1 || MinimumTicks < 1 || BarsToExtend < 1) throw new ArgumentOutOfRangeException();
        if (TickSize <= 0) throw new ArgumentOutOfRangeException(nameof(TickSize));
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

    private void Consume(IBarSeries bars, int barsAgoOfCurrent)
    {
        _upMid.Append(double.NaN);
        _downMid.Append(double.NaN);

        var b0 = bars[barsAgoOfCurrent];
        // Update existing gaps with current bar's price range.
        bool terminateAll = ExtendUntil == FvgExtendUntil.PartiallyFilled;
        foreach (var existing in _gaps.Values) existing.Check(TickSize, b0.Low, b0.High, _barIdx, terminateAll);

        if (_barIdx < 3) { _barIdx++; return; }

        var b2 = bars[barsAgoOfCurrent + 2];
        int upGapTicks = (int)Math.Round((b0.Low - b2.High) / TickSize);
        int dnGapTicks = (int)Math.Round((b2.Low - b0.High) / TickSize);

        bool passesDirectionFilter = true;
        if (EnforceSameDirection)
        {
            var b1 = bars[barsAgoOfCurrent + 1];
            if (upGapTicks >= MinimumTicks)
                passesDirectionFilter = b0.Close > b0.Open && b1.Close > b1.Open && b2.Close > b2.Open;
            else if (dnGapTicks >= MinimumTicks)
                passesDirectionFilter = b0.Close < b0.Open && b1.Close < b1.Open && b2.Close < b2.Open;
        }

        Gap? g = null;
        int barsToKeep = ExtendUntil == FvgExtendUntil.BarsSpecified ? BarsToExtend : 0;
        int anchorBar = _barIdx - 1;
        if (upGapTicks >= MinimumTicks && passesDirectionFilter)
            g = new Gap(TickSize, anchorBar, b2.High, b0.Low, barsToKeep);
        else if (dnGapTicks >= MinimumTicks && passesDirectionFilter)
            g = new Gap(TickSize, anchorBar, b2.Low, b0.High, barsToKeep);

        if (g != null)
        {
            _gaps[anchorBar] = g;
            // Plot the midpoint at bar barsAgo=1 (the gap anchor bar).
            if (_upMid.Count > 1)
            {
                if (g.IsUp)   _upMid.Set(1, g.Midpoint);
                else          _downMid.Set(1, g.Midpoint);
            }
        }
        else
        {
            _gaps.Remove(anchorBar);
        }

        while (_gaps.Count > MaxFvg)
        {
            int first = int.MaxValue;
            foreach (var k in _gaps.Keys) { first = k; break; } // SortedDictionary → ascending
            _gaps.Remove(first);
        }
        _barIdx++;
    }

    /// <summary>
    /// Custom rendering — draws each active gap as a filled rectangle covering the price band
    /// from the gap anchor bar to the per-tick termination bar (or end-of-visible-range if -1).
    /// Optional midpoint line per gap.
    /// </summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        var upColor = Plots[0].Color;
        var dnColor = Plots[1].Color;

        foreach (var g in _gaps.Values)
        {
            // Compute the gap extent. Use the min/max tick keys to capture the full price band
            // but draw it as ONE rect (per-tick draw at high tick counts would burn render time).
            int endBarIdx = int.MinValue;
            foreach (var v in g.Ticks.Values)
                if (v > endBarIdx) endBarIdx = v;
            // -1 in any tick means "still open" → extend to the right edge.
            bool stillOpen = false;
            foreach (var v in g.Ticks.Values) if (v == -1) { stillOpen = true; break; }
            int rightBar = stillOpen ? vp.LastVisibleBarIdx : endBarIdx;
            if (rightBar < g.BarIndex) continue;

            double x = vp.BarIndexToPixelX(g.BarIndex);
            double w = vp.BarIndexToPixelX(rightBar) - x;
            if (w < 1) w = 1;
            double yTop = vp.PriceToPixelY(g.High);
            double yBot = vp.PriceToPixelY(g.Low);
            double h = Math.Abs(yBot - yTop);
            if (h < 1) h = 1;
            renderer.FillRect(x, Math.Min(yTop, yBot), w, h, g.IsUp ? upColor : dnColor);

            if (ShowMidpointLine)
            {
                double midY = vp.PriceToPixelY(g.Midpoint);
                renderer.DrawLine(x, midY, x + w, midY, g.IsUp ? upColor : dnColor, 1.0);
            }
        }
    }
}
