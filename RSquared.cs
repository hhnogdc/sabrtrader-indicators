using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// R-Squared.
/// myPeriod = min(barIdx+1, Period). sumX = N(N+1)/2, sumX2 = sumX(2N+1)/3.
/// Rolling sums of input (y), input² (y²), and i·input (xy).
/// r² = (N·sumXy − sumX·sumY)² / ((N·sumX2 − sumX²)·(N·sumY2 − sumY²)).
/// Period=8 default. Crimson plot, 0.2 / 0.75 reference lines (SlateBlue/Goldenrod).
/// </summary>
[IndicatorDescription("R-Squared: coefficient of determination of a linear regression over the last Period bars (range 0-1). Higher values indicate a stronger linear trend; 0.75 marks a strong fit.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class RSquared : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _priorSumXy, _priorSumY, _priorSumY2;
    private int _barIdx;

    public RSquared(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("R-Squared", _output, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 1, PlotValueUnit.Ratio, PlotBullishDirection.None, "goodness of fit") });
        AddLine("Weak fit",   0.2,  PlotColors.SlateBlue);
        AddLine("Strong fit", 0.75, PlotColors.Goldenrod);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 8;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _priorSumXy = _priorSumY = _priorSumY2 = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        double sumXyPrior = _priorSumXy;
        double sumYPrior  = _priorSumY;
        double sumY2Prior = _priorSumY2;

        int myPeriod = Math.Min(_barIdx + 1, Period);
        double sumX  = myPeriod * (myPeriod + 1) * 0.5;
        double sumX2 = sumX * (2 * myPeriod + 1) / 3.0;

        double input0 = src[barsAgoOfCurrent];
        int periodOffset = Math.Min(Period, _barIdx);
        double inputPeriod = src[barsAgoOfCurrent + periodOffset];

        double sumXy = sumXyPrior - (_barIdx >= Period ? sumYPrior : 0) + myPeriod * input0;
        double sumY  = sumYPrior  + input0 - (_barIdx >= Period ? inputPeriod : 0);
        double sumY2 = sumY2Prior + input0 * input0 - (_barIdx >= Period ? inputPeriod * inputPeriod : 0);

        double denom = (myPeriod * sumX2 - sumX * sumX) * (myPeriod * sumY2 - sumY * sumY);
        double r = denom > 0 ? (myPeriod * sumXy - sumX * sumY) / Math.Sqrt(denom) : 0;
        _output.Append(r * r);

        _priorSumXy = sumXy;
        _priorSumY  = sumY;
        _priorSumY2 = sumY2;
        _barIdx++;
    }
}
