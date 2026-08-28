using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Weighted Moving Average.
/// Linear weights 1..N (most recent gets weight N). The original: bar 0 = Input.
/// Default Period=14, Goldenrod, range [1, int.MaxValue].
/// </summary>
[IndicatorDescription("Weighted Moving Average. Linearly weights the last Period bars from 1 (oldest) to N (most recent), giving more emphasis to recent price than a simple SMA but less smoothing than an EMA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Wma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double[] _window = [];
    private int _count;
    private int _writeIdx;

    public Wma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("WMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window size.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _window = new double[Period];
        _count = 0;
        _writeIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++)
            Consume(src[src.Count - 1 - absIdx]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        Consume(src[0]);
    }

    private void Consume(double v)
    {
        _window[_writeIdx] = v;
        _writeIdx = (_writeIdx + 1) % Period;
        if (_count < Period) _count++;
        // weight i+1 to newest, ..., 1 to oldest (within window).
        int n = _count;
        double weightedSum = 0;
        int weightSum = 0;
        // Walk from oldest to newest. Oldest = _window[(_writeIdx - n + Period) % Period].
        int idx = (_writeIdx - n + Period) % Period;
        for (int i = 0; i < n; i++)
        {
            int w = i + 1;
            weightedSum += _window[idx] * w;
            weightSum += w;
            idx = (idx + 1) % Period;
        }
        _output.Append(weightedSum / weightSum);
    }
}
