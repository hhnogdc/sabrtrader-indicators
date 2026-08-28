using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Standard Error of Linear Regression.
/// Fits an OLS line over the trailing <see cref="Period"/> inputs and
/// emits three plots:
///   <list type="bullet">
///     <item><b>Middle</b> = LinReg value at the current bar (intercept + slope·(Period−1)).</item>
///     <item><b>Upper</b>  = Middle + stdErr.</item>
///     <item><b>Lower</b>  = Middle − stdErr.</item>
///   </list>
/// stdErr = √(Σ(diff²) / Period) where diff is the perpendicular
/// distance from each sample to the regression line.
/// Default Period=14, plots Goldenrod (Middle) + DarkCyan (Upper/Lower).
/// </summary>
[IndicatorDescription("Standard Error Bands: linear regression value at the current bar plus/minus the standard error of the fit (RMS of residuals) over Period samples. Bands tighten when price tracks a clean trend, widen when it diverges.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class StdError : IndicatorBase
{
    private readonly ChunkedSeries<double> _middle = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _midWrap, _upWrap, _loWrap;

    public StdError(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("LinReg", _middle, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line, PlotColors.DarkCyan,  1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line, PlotColors.DarkCyan,  1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Regression window size.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Middle => _midWrap ??= new IndicatorOutputSeries(_middle, Bars);
    public ISeries<double> Upper  => _upWrap  ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _loWrap  ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        // The original's stateful incremental sums are equivalent — we just re-compute
        // per bar here. O(P) per bar, no allocations. The math matches the original's
        // IsRemoveLastBarSupported branch.
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            Compute(src, barsAgo, absIdx);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        Compute(src, 0, src.Count - 1);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent, int barIdx)
    {
        if (barIdx == 0)
        {
            // The original bar 0 in non-IsRemoveLastBarSupported branch: all three = input.
            double v0 = src[barsAgoOfCurrent];
            _middle.Append(v0); _upper.Append(v0); _lower.Append(v0);
            return;
        }
        int p = Math.Min(barIdx + 1, Period);
        // x = 0..p-1 (0 = oldest in the window). Sum_x = p*(p-1)/2.
        // We want: slope = (P*Σxy − Σx·Σy) / (P*Σx² − (Σx)²) but in the original's
        // alternative parameterisation used in IsRemoveLastBarSupported,
        // d = P²·(P-1)(2P-1)/6 − (P(P-1)/2)² ; here we use that.
        // To be safe + simple we just compute OLS slope/intercept directly.
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        // Walk the window oldest → newest. Newest x = p-1.
        for (int k = 0; k < p; k++)
        {
            int barsAgo = barsAgoOfCurrent + (p - 1 - k);   // oldest first
            double y = src[barsAgo];
            sumX  += k;
            sumY  += y;
            sumXY += k * y;
            sumXX += (double)k * k;
        }
        double denom = p * sumXX - sumX * sumX;
        double slope = denom == 0 ? 0 : (p * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / p;
        double linReg = intercept + slope * (p - 1);

        double sumSquares = 0;
        for (int k = 0; k < p; k++)
        {
            int barsAgo = barsAgoOfCurrent + (p - 1 - k);
            double y = src[barsAgo];
            double yhat = intercept + slope * k;
            double diff = y - yhat;
            sumSquares += diff * diff;
        }
        double stdErr = Math.Sqrt(sumSquares / Period);
        _middle.Append(linReg);
        _upper.Append(linReg + stdErr);
        _lower.Append(linReg - stdErr);
    }
}
