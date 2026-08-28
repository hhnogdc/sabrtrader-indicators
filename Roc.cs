using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rate-of-Change (%).
/// <c>Value = 100·(Input[0] − Input[min(CurrentBar,Period)]) / Input[min(CurrentBar,Period)]</c>.
/// When the denominator ≤ 0, the original skips assignment (leaves previous value);
/// we emit 0 to keep the series dense and the chart line continuous.
/// Default Period=14, DodgerBlue plot, zero reference line DarkGray, sub-panel.
/// </summary>
[IndicatorDescription("Rate of Change: 100 * (Input[0] - Input[Period]) / Input[Period]. Percentage momentum oscillator centred on zero; crossings of zero indicate trend reversal.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Roc : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public Roc(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddLine("Zero line", 0, PlotColors.DarkGray);
        AddPlot(new Plot("ROC", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bars back for the comparison.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

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
        for (int absIdx = 0; absIdx < src.Count; absIdx++) Consume(src, src.Count - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        int back = Math.Min(_barIdx, Period);
        double current = src[barsAgoOfCurrent];
        double prior = src[barsAgoOfCurrent + back];
        double v = prior <= 0 ? 0 : (current - prior) / prior * 100;
        _output.Append(v);
        _barIdx++;
    }
}
