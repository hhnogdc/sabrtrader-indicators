using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// SWWMA — Sine-Wave Weighted Moving Average. 1-on-1 port of ATAS's
/// <c>SWWMA</c> (Technical/SWWMA.cs).
///
/// A fixed 5-tap filter over the FIVE PRIOR bars (the current bar is not
/// included): the i-th prior bar (i = 1..5) is weighted by
/// <c>sin(i·π/6)</c> and the sum is normalised by the constant
/// <c>Σ sin(i·π/6) = 3.73205080757</c>. Weights peak (1.0) on the
/// 3-bars-ago sample and taper to 0.5 at both ends, giving a smooth,
/// centred response. For the first five bars (<c>bar &lt; 5</c>) the raw
/// input is emitted unchanged. Overlay; no parameters (the window and
/// weights are fixed, exactly as ATAS ships it).
/// </summary>
[IndicatorDescription("Sine-Wave Weighted Moving Average: a fixed 5-tap filter over the five prior bars weighted by sin(i*pi/6) and normalised by their sum. Centred, smooth response peaking on the 3-bars-ago sample.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Swwma : IndicatorBase
{
    // Σ sin(i·π/6) for i = 1..5 = 0.5 + √3/2 + 1 + √3/2 + 0.5.
    private const double SinSum = 3.73205080757;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    // Precomputed weights for the five prior bars (i = 1..5).
    private static readonly double[] Weights =
    {
        Math.Sin(1 * Math.PI / 6.0),
        Math.Sin(2 * Math.PI / 6.0),
        Math.Sin(3 * Math.PI / 6.0),
        Math.Sin(4 * Math.PI / 6.0),
        Math.Sin(5 * Math.PI / 6.0),
    };

    public Swwma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("SWWMA", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++)
            ComputeAt(src, src.Count - 1 - absIdx, absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        ComputeAt(src, 0, src.Count - 1);
    }

    /// <param name="barsAgo">Position of the bar being computed within <paramref name="src"/>.</param>
    /// <param name="barIndex">0-based absolute index of that bar (its CurrentBar).</param>
    private void ComputeAt(ISeries<double> src, int barsAgo, int barIndex)
    {
        if (barIndex < 5)
        {
            _output.Append(src[barsAgo]);
            return;
        }
        double sum = 0;
        for (int i = 1; i <= 5; i++)
            sum += Weights[i - 1] * src[barsAgo + i];   // src[bar - i]
        _output.Append(sum / SinSum);
    }
}
