using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Angle — the slope of the least-squares regression line over the last
/// <see cref="Period"/> inputs, expressed as an angle in degrees:
/// <code>angle = atan(slope) · 180 / π</code>
/// where <c>slope</c> is the per-bar rise of the OLS fit (the same fit
/// <see cref="LinRegSlope"/> reports). Positive = up-sloping, negative =
/// down-sloping; the magnitude (bounded to ±90°) expresses how steep the
/// trend is.
///
/// <para>As with the original's / the original's Angle study, the conversion uses
/// the RAW price-per-bar slope: an angle in chart degrees would depend on
/// the pixel scaling of the panel (which an indicator has no access to),
/// so the value is the mathematical arctangent of the slope and is most
/// useful read relatively (rising vs falling steepness) rather than as a
/// literal on-screen angle.</para>
///
/// Bar 0 → 0. Defaults Period=14, DodgerBlue, sub-panel, 0 reference line.
/// </summary>
[IndicatorDescription("Angle: the linear-regression slope over Period expressed in degrees — atan(slope) * 180/pi. Positive = up-trend, negative = down-trend; magnitude (bounded +/-90) measures steepness. Uses the raw price-per-bar slope (scale-independent).")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Angle : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public Angle(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Angle", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -90, 90, PlotValueUnit.Raw, PlotBullishDirection.Up, "0 = flat; degrees, bounded +/-90") });
        AddLine("Zero", 0, PlotColors.DarkGray);
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
        if (_barIdx == 0) { _output.Append(0); _barIdx++; return; }
        int p = Math.Min(_barIdx + 1, Period);
        OlsFit.Compute(src, barsAgoOfCurrent, p, out double slope, out _);
        _output.Append(Math.Atan(slope) * (180.0 / Math.PI));
        _barIdx++;
    }
}
