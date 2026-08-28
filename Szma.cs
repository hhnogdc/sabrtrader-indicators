using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// SZMA — "Simple Moving Average, Skip Zeros".
///
/// For each bar the average is taken over the window
/// <c>[max(0, bar − Period) … bar]</c> (inclusive on both ends, so the
/// window spans up to <c>Period + 1</c> samples) but <b>zero values are
/// excluded</b> from both the numerator and the denominator:
/// <c>SZMA = Σ(non-zero inputs) / count(non-zero inputs)</c>. When the
/// window holds only zeros the output is 0.
///
/// Skipping zeros matters for series that legitimately go flat-at-zero
/// (delta, volume-imbalance, signed indicators) where a plain SMA would
/// be dragged toward zero by the empty bars. Default Period=10, overlay.
/// </summary>
[IndicatorDescription("Simple Moving Average that skips zero values: the mean of the non-zero inputs over the window [bar-Period .. bar], dividing by the count of non-zero samples. Avoids zero bars dragging the average down on signed/sparse series.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Szma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    // Ring buffer of the last (Period + 1) raw inputs plus a running
    // sum/count of the NON-ZERO members, so each bar is O(1).
    private double[] _window = [];
    private int _capacity;
    private int _count;          // values currently buffered (≤ capacity)
    private int _writeIdx;
    private double _sumNonZero;
    private int _countNonZero;

    public Szma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("SZMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Look-back window (spans Period+1 samples, original-faithful).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        // The original sums i = max(0, bar-Period) .. bar inclusive ⇒ Period+1 slots.
        _capacity = Period + 1;
        _window = new double[_capacity];
        _count = 0;
        _writeIdx = 0;
        _sumNonZero = 0;
        _countNonZero = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double v)
    {
        if (_count == _capacity)
        {
            double evicted = _window[_writeIdx];
            if (evicted != 0) { _sumNonZero -= evicted; _countNonZero--; }
        }
        else
        {
            _count++;
        }
        _window[_writeIdx] = v;
        if (v != 0) { _sumNonZero += v; _countNonZero++; }
        _writeIdx = (_writeIdx + 1) % _capacity;

        _output.Append(_countNonZero != 0 ? _sumNonZero / _countNonZero : 0.0);
    }
}
