using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Fisher Transform (Ehlers) — 1-on-1 port of NT @FisherTransform.
/// Normalises Input to [-1,+1] over a Period window, applies the
/// arctanh transform, smoothed with prev value.
/// Defaults Period=10. Plot DodgerBlue 2px Histogram. Sub-panel.
/// </summary>
[IndicatorDescription("Fisher Transform (Ehlers): normalises Input to [−1,+1] over a Period window then applies arctanh to sharpen turning points — pushes extreme moves toward visible spikes vs. a smooth oscillator.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class FisherTransform : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMinMax _maxW = null!, _minW = null!;
    private double _prevTmp, _prevFish;
    private bool _hasPrev;

    public FisherTransform(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Fisher Transform", _output, PlotStyle.Histogram, NTColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback for high/low normalisation.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    [IndicatorParameter(DisplayName = "TickSize", Description = "Instrument tick size — guards div-by-zero on flat ranges.",
        MinValue = 0.0001, MaxValue = int.MaxValue, Step = 0.0001)]
    public double TickSize { get; set; } = 0.01;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxW = new RollingMinMax(Period, trackMax: true);
        _minW = new RollingMinMax(Period, trackMax: false);
        _prevTmp = 0;
        _prevFish = 0;
        _hasPrev = false;
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
        _maxW.Push(v); _minW.Push(v);
        double mx = _maxW.Value, mn = _minW.Value;
        double num = mx - mn;
        if (num < TickSize / 10) num = TickSize / 10;
        double tmp = 0.66 * ((v - mn) / num - 0.5) + 0.67 * (_hasPrev ? _prevTmp : 0);
        if (tmp > 0.99) tmp = 0.999;
        else if (tmp < -0.99) tmp = -0.999;
        double fish = 0.5 * Math.Log((1 + tmp) / (1 - tmp)) + 0.5 * (_hasPrev ? _prevFish : 0);
        _output.Append(fish);
        _prevTmp = tmp;
        _prevFish = fish;
        _hasPrev = true;
    }
}
