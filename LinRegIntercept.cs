using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Linear Regression Intercept (the y-axis intercept of the OLS fit)
/// — 1-on-1 port of NT @LinRegIntercept. Period=14 (range [2,∞)),
/// Goldenrod, overlay.
/// </summary>
[IndicatorDescription("Linear Regression Intercept: the y-intercept of the least-squares best-fit line over the last N inputs (the projected value at the start of the regression window).")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class LinRegIntercept : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public LinRegIntercept(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Intercept", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
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
        OlsFit.Compute(src, barsAgoOfCurrent, p, out _, out double intercept);
        _output.Append(intercept);
        _barIdx++;
    }
}
