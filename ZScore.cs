using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Z-Score — the standardised distance of the input from its own rolling
/// mean, measured in standard deviations:
/// <code>z = (x − mean(Period)) / stdDev(Period)</code>
/// where the mean and (population) standard deviation are taken over the
/// trailing <see cref="Period"/> window INCLUDING the current value.
///
/// A reading of +2 means the current value sits two standard deviations
/// above its recent mean (statistically stretched high); −2 is the mirror.
/// Because it is unit-free it is comparable across instruments and time.
///
/// Bar 0 → 0. A flat window (zero std-dev) → 0 (no dispersion to scale by).
/// Defaults Period=20, DarkCyan, sub-panel, 0 / ±2 reference lines.
/// </summary>
[IndicatorDescription("Z-Score: standardised distance of the input from its rolling mean, in standard deviations — (x - mean) / stdDev over Period. +2/-2 mark statistically stretched high/low; unit-free so it is comparable across instruments.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class ZScore : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingStdDev _window = null!;
    private int _barIdx;

    public ZScore(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ZScore", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline; +2 / -2 stretched") });
        AddLine("Upper",  2,  NTColors.DarkGray);
        AddLine("Zero",   0,  NTColors.DarkGray);
        AddLine("Lower", -2,  NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Rolling window for the mean and standard deviation.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _window = new RollingStdDev(Period);
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
        _window.Push(v);
        if (_barIdx == 0) { _output.Append(0); _barIdx++; return; }
        double sd = _window.StdDev;
        _output.Append(sd == 0 ? 0 : (v - _window.Mean) / sd);
        _barIdx++;
    }
}
