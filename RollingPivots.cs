using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum PivotPointType { Standard, Camarilla, Demark, Fibonacci, Woodie }

/// <summary>
/// Rolling Pivot Points — 1-on-1 port of Skender RollingPivots.
/// Same formulas as classical / Camarilla / Demark / Fibonacci / Woodie pivots, but the H/L/C
/// "previous period" is a rolling window of WindowPeriods bars (offset OffsetPeriods bars back)
/// instead of the calendar day. Useful for intraday charts where session pivots don't fit.
/// Defaults WindowPeriods=20, OffsetPeriods=0, PointType=Standard.
/// 7 plots (PP + R1..3 + S1..3); R4/S4 emitted only for Camarilla. Overlay.
/// </summary>
[IndicatorDescription("Rolling Pivot Points: classical pivot levels (Standard, Camarilla, Demark, Fibonacci or Woodie) computed from a sliding WindowPeriods bar window instead of the calendar session. Suits intraday charts where session pivots don't apply.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class RollingPivots : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _pp = new();
    private readonly ChunkedSeries<double> _r1 = new(), _r2 = new(), _r3 = new(), _r4 = new();
    private readonly ChunkedSeries<double> _s1 = new(), _s2 = new(), _s3 = new(), _s4 = new();
    private IndicatorOutputSeries? _ppW, _r1W, _r2W, _r3W, _r4W, _s1W, _s2W, _s3W, _s4W;

    private RollingMinMax _hMax = null!, _lMin = null!;
    private double[] _closeRing = null!;
    private int _closeHead, _closeCount;
    private int _barIdx;

    public RollingPivots(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("PP", _pp, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R1", _r1, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R2", _r2, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R3", _r3, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R4", _r4, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S1", _s1, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S2", _s2, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S3", _s3, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S4", _s4, PlotStyle.Line, NTColors.Crimson,    1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "WindowPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int WindowPeriods { get; set; } = 20;

    [IndicatorParameter(DisplayName = "OffsetPeriods", MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int OffsetPeriods { get; set; } = 0;

    [IndicatorParameter(DisplayName = "PointType")]
    public PivotPointType PointType { get; set; } = PivotPointType.Standard;

    public ISeries<double> PP => _ppW ??= new IndicatorOutputSeries(_pp, Bars);
    public ISeries<double> R1 => _r1W ??= new IndicatorOutputSeries(_r1, Bars);
    public ISeries<double> R2 => _r2W ??= new IndicatorOutputSeries(_r2, Bars);
    public ISeries<double> R3 => _r3W ??= new IndicatorOutputSeries(_r3, Bars);
    public ISeries<double> R4 => _r4W ??= new IndicatorOutputSeries(_r4, Bars);
    public ISeries<double> S1 => _s1W ??= new IndicatorOutputSeries(_s1, Bars);
    public ISeries<double> S2 => _s2W ??= new IndicatorOutputSeries(_s2, Bars);
    public ISeries<double> S3 => _s3W ??= new IndicatorOutputSeries(_s3, Bars);
    public ISeries<double> S4 => _s4W ??= new IndicatorOutputSeries(_s4, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (WindowPeriods < 1 || OffsetPeriods < 0) throw new ArgumentOutOfRangeException();
        _hMax = new RollingMinMax(WindowPeriods, trackMax: true);
        _lMin = new RollingMinMax(WindowPeriods, trackMax: false);
        // For Close we need value at (OffsetPeriods + 1) bars-ago of the window's end — store enough.
        _closeRing = new double[Math.Max(2, OffsetPeriods + 2)];
        _closeHead = _closeCount = 0;
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
        // Push H/L for window-min/max (ONLY bars that are "fully past" by OffsetPeriods).
        // Skender: window is quotes[i - windowPeriods - offsetPeriods + 1 .. i - offsetPeriods - 1].
        // Streaming: we feed _hMax/_lMin with bars seen OffsetPeriods bars ago, so we delay-push.
        // Simplest: keep a small ring of the last (OffsetPeriods + 1) (high, low) and push the
        // oldest-eligible one into RollingMinMax.
        // For OffsetPeriods=0 the "yesterday" bar (i-1) is delayed by 1.
        _pp.Append(double.NaN); _r1.Append(double.NaN); _r2.Append(double.NaN); _r3.Append(double.NaN); _r4.Append(double.NaN);
        _s1.Append(double.NaN); _s2.Append(double.NaN); _s3.Append(double.NaN); _s4.Append(double.NaN);

        // Push close.
        _closeRing[_closeHead] = bar.Close;
        _closeHead = (_closeHead + 1) % _closeRing.Length;
        if (_closeCount < _closeRing.Length) _closeCount++;

        // We feed RollingMinMax with the bar that's "OffsetPeriods + 1 bars ago" so the window
        // looks at bars in [i-windowPeriods-offset+1 .. i-offset-1] (inclusive both ends).
        // The Skender formula's max range index is i-offset-1 → that bar becomes eligible when
        // current bar is _barIdx and the eligible past bar's barsAgo = OffsetPeriods + 1.
        if (_barIdx >= OffsetPeriods + 1)
        {
            // The "newest-eligible" bar's high/low — that's the bar (OffsetPeriods+1) bars ago.
            // For ring access we'd need a parallel high/low ring; instead, build it: push H/L of
            // the SAME bar via a delayed buffer.
            // To avoid extra rings, we exploit that bar.High/Low are needed at fixed lag. Use a
            // local ring of length OffsetPeriods+2 below.
            PushDelayedHl(bar);
        }
        else
        {
            PushDelayedHl(bar); // still feed the delay buffer
        }

        if (_barIdx < WindowPeriods + OffsetPeriods) { _barIdx++; return; }

        double high = _hMax.Value;
        double low  = _lMin.Value;
        double close = ReadCloseBarsAgo(OffsetPeriods + 1);  // bar i-offset-1
        // Open for Demark / Woodie = the current bar's open (Skender's q.Open at i, NOT the
        // window-start bar). Source: GetPivotPoint(... (double)q.Open ...).
        double open = bar.Open;

        ComputePoints(open, high, low, close, PointType,
            out double pp, out double r1, out double r2, out double r3, out double r4,
            out double s1, out double s2, out double s3, out double s4);
        int ago = 0;
        if (!double.IsNaN(pp)) _pp.Set(ago, pp);
        if (!double.IsNaN(r1)) _r1.Set(ago, r1);
        if (!double.IsNaN(r2)) _r2.Set(ago, r2);
        if (!double.IsNaN(r3)) _r3.Set(ago, r3);
        if (!double.IsNaN(r4)) _r4.Set(ago, r4);
        if (!double.IsNaN(s1)) _s1.Set(ago, s1);
        if (!double.IsNaN(s2)) _s2.Set(ago, s2);
        if (!double.IsNaN(s3)) _s3.Set(ago, s3);
        if (!double.IsNaN(s4)) _s4.Set(ago, s4);
        _barIdx++;
    }

    private readonly Bar[] _delayBuf = new Bar[64];   // small enough; OffsetPeriods rarely > 60
    private int _delayHead, _delayCount;
    private void PushDelayedHl(Bar bar)
    {
        if (_delayBuf.Length < OffsetPeriods + 2)
            throw new InvalidOperationException("OffsetPeriods exceeds internal delay buffer capacity.");
        _delayBuf[_delayHead] = bar;
        _delayHead = (_delayHead + 1) % _delayBuf.Length;
        if (_delayCount < _delayBuf.Length) _delayCount++;

        // Feed RollingMinMax with the bar that became "newest eligible" this step.
        // That's the bar (OffsetPeriods + 1) bars ago — i.e. delayBuf at offset (OffsetPeriods+1).
        if (_delayCount > OffsetPeriods + 1)
        {
            int idx = (_delayHead - 1 - (OffsetPeriods + 1) + _delayBuf.Length) % _delayBuf.Length;
            var b = _delayBuf[idx];
            _hMax.Push(b.High);
            _lMin.Push(b.Low);
        }
    }

    private double ReadCloseBarsAgo(int barsAgo)
    {
        int idx = (_closeHead - 1 - barsAgo + _closeRing.Length) % _closeRing.Length;
        return _closeRing[idx];
    }

    internal static void ComputePoints(double open, double high, double low, double close, PivotPointType type,
        out double pp, out double r1, out double r2, out double r3, out double r4,
        out double s1, out double s2, out double s3, out double s4)
    {
        pp = r1 = r2 = r3 = r4 = s1 = s2 = s3 = s4 = double.NaN;
        switch (type)
        {
            case PivotPointType.Standard:
                pp = (high + low + close) / 3;
                r1 = 2 * pp - low; s1 = 2 * pp - high;
                r2 = pp + (high - low); s2 = pp - (high - low);
                r3 = high + 2 * (pp - low); s3 = low - 2 * (high - pp);
                break;
            case PivotPointType.Camarilla:
                pp = (high + low + close) / 3;
                r1 = close + (high - low) * 1.1 / 12;
                r2 = close + (high - low) * 1.1 / 6;
                r3 = close + (high - low) * 1.1 / 4;
                r4 = close + (high - low) * 1.1 / 2;
                s1 = close - (high - low) * 1.1 / 12;
                s2 = close - (high - low) * 1.1 / 6;
                s3 = close - (high - low) * 1.1 / 4;
                s4 = close - (high - low) * 1.1 / 2;
                break;
            case PivotPointType.Demark:
                double x = close < open ? high + 2 * low + close
                         : close > open ? 2 * high + low + close
                         : high + low + 2 * close;
                pp = x / 4d;
                s1 = x / 2d - high;
                r1 = x / 2d - low;
                break;
            case PivotPointType.Fibonacci:
                pp = (high + low + close) / 3;
                r1 = pp + 0.382 * (high - low); s1 = pp - 0.382 * (high - low);
                r2 = pp + 0.618 * (high - low); s2 = pp - 0.618 * (high - low);
                r3 = pp + 1.000 * (high - low); s3 = pp - 1.000 * (high - low);
                break;
            case PivotPointType.Woodie:
                pp = (high + low + 2 * open) / 4;
                r1 = 2 * pp - low;
                s1 = 2 * pp - high;
                r2 = pp + high - low;
                s2 = pp - high + low;
                r3 = high + 2 * (pp - low);
                s3 = low  - 2 * (high - pp);
                break;
        }
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_pp.Count == 0) return;
        double sz = 11;
        // The level LINES are drawn by the standard plot pass (so they honour Visible / Style /
        // Color already); the custom render only adds the right-edge name label. Drive each
        // label off its plot so it tracks the same Visible toggle and Color as the line —
        // otherwise a hidden R4/S4 still printed its label, and a recoloured line kept a
        // hard-coded label colour.
        for (int i = 0; i < Plots.Count; i++)
            DrawLevel(ctx, Plots[i], sz);
    }

    private static void DrawLevel(IIndicatorRenderContext ctx, IPlot plot, double sizePx)
    {
        if (!plot.Visible) return;
        if (plot.Values.Count == 0) return;
        double value = plot.Values[0];
        if (double.IsNaN(value)) return;
        double y = ctx.Viewport.PriceToPixelY(value);
        var (w, h) = ctx.Renderer.MeasureText(plot.Name, sizePx);
        ctx.Renderer.DrawText(ctx.PlotArea.Right - w - 4, y - h / 2, plot.Name, plot.Color, sizePx);
    }
}
