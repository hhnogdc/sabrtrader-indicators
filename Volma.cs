using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Moving Average.
/// EMA of bar volume over <see cref="Period"/> bars (the original uses its own
/// recursive EMA over the Volume series). Default Period=14,
/// Goldenrod, sub-panel.
/// </summary>
[IndicatorDescription("Volume Moving Average. An exponential moving average of bar volume over Period bars, smoothing volume spikes to reveal sustained participation trends.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Volma : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _alpha, _oneMinusAlpha;
    private double _value;
    private bool _hasValue;

    public Volma(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("VOLMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period over volume.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _alpha = 2.0 / (Period + 1);
        _oneMinusAlpha = 1 - _alpha;
        _value = 0;
        _hasValue = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i].Volume);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0].Volume);
    }

    private void Consume(double v)
    {
        if (!_hasValue) { _value = v; _hasValue = true; _output.Append(v); return; }
        _value = _alpha * v + _oneMinusAlpha * _value;
        _output.Append(_value);
    }
}
