using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Simple Moving Average. Defaults to <see cref="Period"/>=14 with
/// the Goldenrod plot colour, the conventional default for this study.
///
/// Math: rolling mean over the last <see cref="Period"/> input
/// values. During warmup (fewer than <see cref="Period"/> samples
/// available) emits the running mean over what IS available —
/// the original emits a value from bar 0 onwards, not NaN.
///
/// Use standalone or chain:
/// <code>
///     new Sma { Period = 14 }                          // chart, defaults
///     new Sma(this)                                    // child, defaults
///     new Sma(this, input: ema.Output) { Period = 7 }  // chained input
/// </code>
/// </summary>
[IndicatorDescription("Simple Moving Average: arithmetic mean of the last Period input values. The baseline trend-following average; emits a running mean from bar 0 during warmup.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Sma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputAsBarSourced;
    // Running sum over the most-recent up-to-Period samples. We store
    // an explicit ring buffer of input values so window subtraction is
    // O(1) once the window is full. RollingSum from Incremental works
    // identically but the inlined version keeps the original's "running mean
    // during warmup" branch trivial to read.
    private double[] _window = [];
    private double _sum;
    private int _count;     // total inputs seen (≤ Period)
    private int _writeIdx;

    public Sma(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot(
            name: "SMA",
            values: _output,
            style: PlotStyle.Line,
            color: PlotColors.Goldenrod,
            lineWidthPx: 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(
        DisplayName = "Period",
        Description = "Number of inputs averaged. The original range: 1..int.MaxValue.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    /// <summary>Per-bar SMA value as a chainable bar-sourced series.</summary>
    public ISeries<double> Output =>
        _outputAsBarSourced ??= new IndicatorOutputSeries(_output, Bars);

    /// <summary>True once the rolling window holds <see cref="Period"/> samples.</summary>
    public bool IsFull => _count >= Period;

    /// <summary>Latest SMA value. original-style: defined from the first bar (running mean during warmup).</summary>
    public double Value => _output.Count == 0 ? 0 : _output[0];

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period),
                "SMA period must be at least 1.");
        _window = new double[Period];
        _sum = 0;
        _count = 0;
        _writeIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            ConsumeAndAppend(src[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        ConsumeAndAppend(src[0]);
    }

    /// <summary>
    /// the original semantics: when <see cref="Period"/>=1 the output equals
    /// the current input. When the window isn't full yet
    /// (<c>CurrentBar &lt; Period - 1</c>), output is the simple mean
    /// over the samples seen so far. Once the window fills, each new
    /// input replaces the oldest (sum += in - out).
    /// </summary>
    private void ConsumeAndAppend(double input)
    {
        if (_count < Period)
        {
            // Warmup: accumulate without subtracting.
            _window[_writeIdx] = input;
            _sum += input;
            _count++;
            _writeIdx = (_writeIdx + 1) % Period;
            _output.Append(_sum / _count);
            return;
        }
        // Full window: subtract the value being overwritten, add the new one.
        double evicted = _window[_writeIdx];
        _window[_writeIdx] = input;
        _sum += input - evicted;
        _writeIdx = (_writeIdx + 1) % Period;
        _output.Append(_sum / Period);
    }
}
