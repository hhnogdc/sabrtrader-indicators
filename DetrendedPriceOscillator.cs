using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Detrended Price Oscillator.
/// lookback = Period/2 + 1. Value = Input[lookback] − SMA(Period)[0].
/// Bar &lt; lookback → 0.
/// Period=14, DodgerBlue, zero reference line, sub-panel.
/// </summary>
[IndicatorDescription("Detrended Price Oscillator: Input[Period/2 + 1] minus its SMA. Removes the longer trend to expose short-term cycles around zero.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class DetrendedPriceOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Sma _sma = null!;
    private int _barIdx;

    public DetrendedPriceOscillator(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("DPO", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "SMA window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sma = new Sma(this, input: Input) { Period = Period };
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int lookback = Period / 2 + 1;
        for (int absIdx = 0; absIdx < src.Count; absIdx++)
        {
            int barsAgo = src.Count - 1 - absIdx;
            if (_barIdx < lookback) _output.Append(0);
            else _output.Append(src[barsAgo + lookback] - _sma.Output[barsAgo]);
            _barIdx++;
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (Input.Count == 0) return;
        int lookback = Period / 2 + 1;
        if (_barIdx < lookback) _output.Append(0);
        else _output.Append(Input[lookback] - _sma.Output[0]);
        _barIdx++;
    }
}
