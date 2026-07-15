using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Linear Regression line value at the current bar — 1-on-1 port
/// of NT @LinReg. Defaults Period=14 (range [2,∞)), Goldenrod, overlay.
/// Value = intercept + slope·(Period−1).
/// </summary>
[IndicatorDescription("Linear Regression: value of the least-squares best-fit line at the current bar over the last N inputs (intercept + slope*(N-1)). A statistical trend line.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class LinReg : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public LinReg(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("LinReg", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Regression window.",
        MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++) Compute(src, n - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx == 0) { _output.Append(src[barsAgoOfCurrent]); _barIdx++; return; }
        int p = Math.Min(_barIdx + 1, Period);
        OlsFit.Compute(src, barsAgoOfCurrent, p, out double slope, out double intercept);
        _output.Append(intercept + slope * (p - 1));
        _barIdx++;
    }
}

/// <summary>OLS helper shared by LinReg / LinRegIntercept / LinRegSlope / TSF.</summary>
internal static class OlsFit
{
    /// <summary>
    /// Fit y = slope·x + intercept using x = 0..p-1 over the window
    /// ending at <paramref name="barsAgoOfCurrent"/> of <paramref name="src"/>.
    /// Newest sample = x = p-1.
    /// </summary>
    public static void Compute(ISeries<double> src, int barsAgoOfCurrent, int p,
                               out double slope, out double intercept)
    {
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int k = 0; k < p; k++)
        {
            int barsAgo = barsAgoOfCurrent + (p - 1 - k);
            double y = src[barsAgo];
            sumX  += k;
            sumY  += y;
            sumXY += k * y;
            sumXX += (double)k * k;
        }
        double denom = p * sumXX - sumX * sumX;
        slope = denom == 0 ? 0 : (p * sumXY - sumX * sumY) / denom;
        intercept = (sumY - slope * sumX) / p;
    }
}
