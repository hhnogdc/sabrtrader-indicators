using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Mean Deviation (mean absolute deviation) — the average absolute
/// distance of the input from its own rolling mean over the trailing
/// <see cref="Period"/> window:
/// <code>mad = (1/n) · Σ |x − mean(Period)|</code>
/// Like standard deviation it is a price-unit measure of spread, but it
/// weights every sample linearly instead of squaring, so it is less
/// sensitive to outliers than <see cref="StdDev"/>.
///
/// <para>The absolute deviations cannot be reconstructed from running
/// power-sums (the sign information needed by |·| is lost), so this
/// computes them with an O(Period) scan of the input window per bar —
/// the same approach the <see cref="StdError"/> regression study uses for
/// its residuals. For trader-realistic periods this is negligible.</para>
///
/// Bar 0 → 0. Defaults Period=20, Goldenrod, sub-panel, price-valued.
/// </summary>
[IndicatorDescription("Mean Deviation (mean absolute deviation): average absolute distance of the input from its rolling mean over Period, in price units. Like standard deviation but linear, so it is less sensitive to outliers.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MeanDeviation : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public MeanDeviation(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("MeanDev", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Rolling window for the mean and the deviations.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
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
        if (_barIdx == 0) { _output.Append(0); _barIdx++; return; }
        int p = Math.Min(_barIdx + 1, Period);

        double sum = 0;
        for (int k = 0; k < p; k++) sum += src[barsAgoOfCurrent + k];
        double mean = sum / p;

        double absSum = 0;
        for (int k = 0; k < p; k++) absSum += Math.Abs(src[barsAgoOfCurrent + k] - mean);

        _output.Append(absSum / p);
        _barIdx++;
    }
}
