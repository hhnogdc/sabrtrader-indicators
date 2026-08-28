using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Time Series Forecast.
/// Like LinReg but one bar projected forward: intercept + slope·Period.
/// Period=14, Goldenrod, overlay, range [2,∞).
/// </summary>
[IndicatorDescription("Time Series Forecast. The endpoint of a linear regression line over the last Period bars projected one bar forward (intercept + slope*Period).")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Tsf : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public Tsf(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("TSF", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
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
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
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
        _output.Append(intercept + slope * p);
        _barIdx++;
    }
}
