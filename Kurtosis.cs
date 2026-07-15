using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Kurtosis — the population EXCESS kurtosis of the input over a trailing
/// <see cref="Period"/> window:
/// <code>kurt = m4 / m2² − 3</code>
/// where m2 / m4 are the 2nd / 4th central moments. The −3 normalises a
/// Gaussian to 0, so the reading describes tail behaviour relative to the
/// normal distribution: positive = fat tails / sharp peak (more outliers),
/// negative = thin tails / flat-topped.
///
/// Until the window holds at least two distinct values the moment is
/// undefined (zero variance) and the plot reads 0. Defaults Period=20,
/// Goldenrod, sub-panel, 0 reference line.
/// </summary>
[IndicatorDescription("Kurtosis: population excess kurtosis of the input over Period (m4/m2^2 - 3). 0 = normal-tailed; positive = fat tails / sharp peak (more outliers); negative = thin, flat-topped tails.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Kurtosis : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMoments _window = null!;

    public Kurtosis(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Kurtosis", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, "0 = normal-tailed (Gaussian)") });
        AddLine("Zero", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Rolling window over which the moments are computed.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _window = new RollingMoments(Period);
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
        // ExcessKurtosis already returns 0 for a degenerate (zero-variance)
        // window, which covers bar 0 and any flat run.
        _output.Append(_window.ExcessKurtosis);
    }
}
