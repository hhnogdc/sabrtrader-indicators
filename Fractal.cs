using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum FractalEndType { HighLow, Close }

/// <summary>
/// Williams Fractal — 1-on-1 port of Skender Fractal.
/// A bearish fractal is signalled at bar i when bar (i − RightSpan)'s High is strictly greater
/// than the Highs of all surrounding 2·Span bars (LeftSpan on each side). Bullish = Low pivot.
/// Bar (i − RightSpan) carries the signal; bar i does the detection (because we need RightSpan
/// future bars). In our streaming model we therefore emit the signal RightSpan bars LATE —
/// the result is written to <c>bar i − RightSpan</c> when bar i confirms it.
/// EndType.HighLow uses High/Low; EndType.Close uses Close.
/// Defaults LeftSpan=RightSpan=2, EndType=HighLow.
/// Plots: FractalBear (Red Dots), FractalBull (LimeGreen Dots).
/// </summary>
[IndicatorDescription("Williams Fractal pivots: marks a bearish fractal where a bar's High is the strict max over LeftSpan + RightSpan surrounding bars, and a bullish fractal on the symmetric Low pattern.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Fractal : IndicatorBase
{
    private readonly ChunkedSeries<double> _bear = new();
    private readonly ChunkedSeries<double> _bull = new();
    private IndicatorOutputSeries? _bearW, _bullW;
    private int _barIdx;

    public Fractal(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("FractalBear", _bear, PlotStyle.Dots, NTColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Down, null) });
        AddPlot(new Plot("FractalBull", _bull, PlotStyle.Dots, NTColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, null) });
    }

    [IndicatorParameter(DisplayName = "LeftSpan",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LeftSpan  { get; set; } = 2;

    [IndicatorParameter(DisplayName = "RightSpan", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RightSpan { get; set; } = 2;

    [IndicatorParameter(DisplayName = "EndType")]
    public FractalEndType EndType { get; set; } = FractalEndType.HighLow;

    public ISeries<double> FractalBear => _bearW ??= new IndicatorOutputSeries(_bear, Bars);
    public ISeries<double> FractalBull => _bullW ??= new IndicatorOutputSeries(_bull, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LeftSpan < 1 || RightSpan < 1) throw new ArgumentOutOfRangeException();
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
        // Default: emit NaN at current bar.
        _bear.Append(double.NaN);
        _bull.Append(double.NaN);

        // Skender check: i+1 > leftSpan && i+1 <= length - rightSpan. In streaming we don't know
        // length ahead — we evaluate the candidate at "rightSpan bars ago" (which now has rightSpan
        // confirming future bars = current and (rightSpan-1) before).
        if (_barIdx < LeftSpan + RightSpan) { _barIdx++; return; }

        int candidateBarsAgo = RightSpan; // candidate sits RightSpan bars ago
        var cand = bars[barsAgoOfCurrent + candidateBarsAgo];
        double evalHigh = EndType == FractalEndType.Close ? cand.Close : cand.High;
        double evalLow  = EndType == FractalEndType.Close ? cand.Close : cand.Low;

        bool isHigh = true, isLow = true;
        for (int k = -LeftSpan; k <= RightSpan; k++)
        {
            if (k == 0) continue;
            int barsAgo = barsAgoOfCurrent + candidateBarsAgo - k;     // walk back +k = walk forward in time (closer to current)
            var w = bars[barsAgo];
            double wh = EndType == FractalEndType.Close ? w.Close : w.High;
            double wl = EndType == FractalEndType.Close ? w.Close : w.Low;
            if (evalHigh <= wh) isHigh = false;
            if (evalLow  >= wl) isLow  = false;
        }

        if (isHigh) _bear.Set(candidateBarsAgo, evalHigh);
        if (isLow)  _bull.Set(candidateBarsAgo, evalLow);
        _barIdx++;
    }
}
