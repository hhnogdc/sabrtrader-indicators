using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Williams Alligator — derived from Skender.Stock.Indicators (Apache-2.0).
/// Three SMMA (Wilder) lines on HL2 (Median price), each displaced FORWARD by their own offset:
///   Jaw    = SMMA(HL2, 13)  displaced +8 bars
///   Teeth  = SMMA(HL2, 8)   displaced +5 bars
///   Lips   = SMMA(HL2, 5)   displaced +3 bars
/// SMMA seeded with SMA over the seed-window (Skender: SMA over the period preceding the offset slot).
/// Defaults JawPeriods=13/JawOffset=8/TeethPeriods=8/TeethOffset=5/LipsPeriods=5/LipsOffset=3.
/// Plots: Jaw (Blue), Teeth (Red), Lips (LimeGreen).
/// Forward-displacement is rendered via IChartCustomRender; the per-bar plot values still expose
/// the SMMA at the current bar (without the visual shift) for indicator chaining.
/// </summary>
[IndicatorDescription("Bill Williams Alligator. Three forward-displaced smoothed moving averages on median price (Jaw 13/8, Teeth 8/5, Lips 5/3). Lines spreading apart signal an active trend, lines tightly intertwined signal a sleeping market.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Alligator : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _jaw   = new();
    private readonly ChunkedSeries<double> _teeth = new();
    private readonly ChunkedSeries<double> _lips  = new();
    private IndicatorOutputSeries? _jW, _tW, _lW;
    private readonly Plot _jawPlot, _teethPlot, _lipsPlot;

    private double _jawState, _teethState, _lipsState;
    private bool _jawSeeded, _teethSeeded, _lipsSeeded;
    private int _barIdx;

    // Per-line rolling history of HL2 values: we need values[i - offset] when seeding/extending.
    // To support arbitrary offsets without owning a full history array, we keep a fixed-size ring
    // sized to max(period + offset).
    private double[] _hl2Ring = null!;
    private int _hl2Head;
    private int _hl2Count;

    public Alligator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // The three lines are forward-DISPLACED, so they're painted by OnCustomRender, not the
        // standard PlotRenderPass. IsCustomRendered = true makes the standard pass skip them so
        // they don't ALSO paint un-displaced at the native bar position (which doubled every
        // line). Visible = true so the settings show/hide toggle gates each displaced line (and
        // the line-style picker works); the per-bar plot values still expose the un-shifted
        // SMMA for chaining regardless of Visible.
        _jawPlot   = new Plot("Jaw",   _jaw,   PlotStyle.Line, PlotColors.Blue,      1.0) { IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        _teethPlot = new Plot("Teeth", _teeth, PlotStyle.Line, PlotColors.Red,       1.0) { IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        _lipsPlot  = new Plot("Lips",  _lips,  PlotStyle.Line, PlotColors.LimeGreen, 1.0) { IsCustomRendered = true, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) };
        AddPlot(_jawPlot); AddPlot(_teethPlot); AddPlot(_lipsPlot);
    }

    [IndicatorParameter(DisplayName = "JawPeriods",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int JawPeriods   { get; set; } = 13;

    [IndicatorParameter(DisplayName = "JawOffset",    MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int JawOffset    { get; set; } = 8;

    [IndicatorParameter(DisplayName = "TeethPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int TeethPeriods { get; set; } = 8;

    [IndicatorParameter(DisplayName = "TeethOffset",  MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int TeethOffset  { get; set; } = 5;

    [IndicatorParameter(DisplayName = "LipsPeriods",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LipsPeriods  { get; set; } = 5;

    [IndicatorParameter(DisplayName = "LipsOffset",   MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int LipsOffset   { get; set; } = 3;

    public ISeries<double> Jaw   => _jW ??= new IndicatorOutputSeries(_jaw,   Bars);
    public ISeries<double> Teeth => _tW ??= new IndicatorOutputSeries(_teeth, Bars);
    public ISeries<double> Lips  => _lW ??= new IndicatorOutputSeries(_lips,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (JawPeriods   < 1 || JawOffset   < 0) throw new ArgumentOutOfRangeException();
        if (TeethPeriods < 1 || TeethOffset < 0) throw new ArgumentOutOfRangeException();
        if (LipsPeriods  < 1 || LipsOffset  < 0) throw new ArgumentOutOfRangeException();
        int maxRing = Math.Max(JawPeriods + JawOffset, Math.Max(TeethPeriods + TeethOffset, LipsPeriods + LipsOffset)) + 1;
        _hl2Ring = new double[maxRing];
        _hl2Head = 0; _hl2Count = 0;
        _jawSeeded = _teethSeeded = _lipsSeeded = false;
        _barIdx = 0;
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
        double hl2 = (bar.High + bar.Low) / 2;
        // Push into ring.
        _hl2Ring[_hl2Head] = hl2;
        _hl2Head = (_hl2Head + 1) % _hl2Ring.Length;
        if (_hl2Count < _hl2Ring.Length) _hl2Count++;

        _jaw.Append(  StepLine(JawPeriods,   JawOffset,   ref _jawState,   ref _jawSeeded)   );
        _teeth.Append(StepLine(TeethPeriods, TeethOffset, ref _teethState, ref _teethSeeded) );
        _lips.Append( StepLine(LipsPeriods,  LipsOffset,  ref _lipsState,  ref _lipsSeeded)  );
        _barIdx++;
    }

    private double StepLine(int period, int offset, ref double state, ref bool seeded)
    {
        if (_barIdx < period + offset - 1) return double.NaN;
        if (!seeded)
        {
            // Seed = SMA over the (period) bars ending offset bars ago.
            double sum = 0;
            for (int k = offset; k < offset + period; k++) sum += ReadHl2BarsAgo(k);
            state = sum / period;
            seeded = true;
            return state;
        }
        // SMMA recursion using value at (offset) bars ago.
        double v = ReadHl2BarsAgo(offset);
        state = (state * (period - 1) + v) / period;
        return state;
    }

    private double ReadHl2BarsAgo(int barsAgo)
    {
        // _hl2Head points to the SLOT AFTER the newest. Newest = head-1.
        int idx = (_hl2Head - 1 - barsAgo + _hl2Ring.Length) % _hl2Ring.Length;
        return _hl2Ring[idx];
    }

    /// <summary>Custom render — applies the per-line forward displacement (drawing each line shifted right).</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        // Each line is painted ONLY here (the standard pass skips IsCustomRendered plots), at its
        // forward offset. Honour each plot's Visible toggle + line style.
        if (_jawPlot.Visible)   DrawDisplaced(renderer, vp, _jaw,   JawOffset,   _jawPlot.Color,   _jawPlot.LineWidthPx,   _jawPlot.Style);
        if (_teethPlot.Visible) DrawDisplaced(renderer, vp, _teeth, TeethOffset, _teethPlot.Color, _teethPlot.LineWidthPx, _teethPlot.Style);
        if (_lipsPlot.Visible)  DrawDisplaced(renderer, vp, _lips,  LipsOffset,  _lipsPlot.Color,  _lipsPlot.LineWidthPx,  _lipsPlot.Style);
    }

    private static void DrawDisplaced(SabrTrader.Pipeline.Charts.Rendering.IChartRenderer renderer,
                                      SabrTrader.Pipeline.Charts.Viewport.ChartViewport vp,
                                      ChunkedSeries<double> series, int offset, ChartColor color, double widthPx,
                                      PlotStyle style)
    {
        if (offset < 0) return;
        int total = series.Count;
        if (total == 0) return;
        bool havePrev = false;
        double prevX = 0, prevY = 0;
        int first = Math.Max(0, vp.FirstVisibleBarIdx - offset);
        int last  = Math.Min(total - 1, vp.LastVisibleBarIdx);
        for (int i = first; i <= last; i++)
        {
            int barsAgo = total - 1 - i;
            double v = series[barsAgo];
            if (double.IsNaN(v)) { havePrev = false; continue; }
            double x = vp.BarIndexToPixelX(i + offset);
            double y = vp.PriceToPixelY(v);
            if (havePrev)
                SabrTrader.Pipeline.Charts.Rendering.CustomLineRenderer.DrawSegment(renderer, prevX, prevY, x, y, color, widthPx, style);
            prevX = x; prevY = y; havePrev = true;
        }
    }
}
