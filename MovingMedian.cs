using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Moving Median (ATAS "MMed") — 1-on-1 port of ATAS's
/// <c>MMed</c> (Technical/MMed.cs). The rolling median of the input over
/// the window <c>[max(0, bar − Period) … bar]</c>.
///
/// <para>Faithful to ATAS's exact index/parity rules — which differ
/// between warm-up and steady state:</para>
/// <list type="bullet">
///   <item><b>Warm-up</b> (<c>bar &lt; Period</c>): the true median of the
///     <c>bar + 1</c> samples available — parity decided by the window
///     size, so even windows average the two central order statistics.</item>
///   <item><b>Steady state</b> (<c>bar ≥ Period</c>): the window holds
///     <c>Period + 1</c> samples but ATAS selects the order statistic by
///     the parity of <c>Period</c> (not <c>Period + 1</c>): odd Period
///     returns <c>sorted[Period/2]</c>; even Period averages
///     <c>sorted[Period/2]</c> and <c>sorted[Period/2 − 1]</c>. This
///     asymmetry is intentional ATAS behaviour and is reproduced here so
///     values match tick-for-tick.</item>
/// </list>
/// A median centre-line is robust to outliers/spikes that distort a
/// mean-based MA. Default Period=10, overlay.
/// </summary>
[IndicatorDescription("Moving Median: the rolling median of price over the window [bar-Period .. bar]. A robust centre-line that ignores spikes/outliers a mean-based moving average would chase.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MovingMedian : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    // Ring buffer of the last (Period + 1) raw inputs and a reusable
    // sort scratch buffer so steady-state bars allocate nothing.
    private double[] _window = [];
    private double[] _sortBuf = [];
    private int _capacity;
    private int _count;
    private int _writeIdx;
    private int _barIdx;

    public MovingMedian(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("MMed", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Median window (spans Period+1 samples, ATAS-faithful).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        _capacity = Period + 1;
        _window = new double[_capacity];
        _sortBuf = new double[_capacity];
        _count = 0;
        _writeIdx = 0;
        _barIdx = 0;
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
        if (_count < _capacity) _count++;
        _window[_writeIdx] = v;
        _writeIdx = (_writeIdx + 1) % _capacity;

        // Sort the live window contents into the scratch buffer.
        int n = _count;
        Array.Copy(_window, _sortBuf, n);
        Array.Sort(_sortBuf, 0, n);

        // ATAS selects the order statistic by Period parity in steady
        // state and by window-size parity during warm-up.
        double median;
        if (_barIdx < Period)
        {
            int idx = (_barIdx + 1) / 2;          // = bar - targetBar in ATAS
            median = ((_barIdx + 1) % 2 == 1)
                ? _sortBuf[idx]
                : (_sortBuf[idx] + _sortBuf[idx - 1]) / 2.0;
        }
        else
        {
            int idx = Period / 2;                   // = bar - targetBar in ATAS
            median = (Period % 2 == 1)
                ? _sortBuf[idx]
                : (_sortBuf[idx] + _sortBuf[idx - 1]) / 2.0;
        }

        _output.Append(median);
        _barIdx++;
    }
}
