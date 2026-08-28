using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Dispersion — the coefficient of variation of the input over a trailing
/// <see cref="Period"/> window, expressed as a percentage:
/// <code>dispersion = 100 · stdDev(Period) / mean(Period)</code>
/// This normalises the (population) standard deviation by the mean, giving
/// a scale-free measure of relative spread: a value of 5 means the recent
/// dispersion is 5% of the average level, directly comparable across
/// instruments and price regimes (unlike a raw std-dev, which scales with
/// price). It is the relative-spread complement to the absolute
/// <see cref="StdDev"/> indicator.
///
/// A flat window (zero mean or zero std-dev) → 0. Defaults Period=20,
/// DarkCyan, sub-panel, percent-valued.
/// </summary>
[IndicatorDescription("Dispersion (coefficient of variation): 100 * stdDev / mean over Period — the relative spread of the input as a percentage of its mean. Scale-free, so it is comparable across instruments and price levels (unlike raw standard deviation).")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Dispersion : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingStdDev _window = null!;
    private int _barIdx;

    public Dispersion(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Dispersion", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, null, PlotValueUnit.Percent, PlotBullishDirection.None, "0 = no relative spread") });
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
        double mean = _window.Mean;
        _output.Append(mean == 0 ? 0 : 100.0 * _window.StdDev / Math.Abs(mean));
        _barIdx++;
    }
}
