using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Momentum.
/// <c>Value = Input[0] − Input[min(CurrentBar, Period)]</c>. Bar 0 → 0.
/// Default Period=14, DarkCyan plot, zero reference line SlateBlue, sub-panel.
/// </summary>
[IndicatorDescription("Momentum: current input minus the input N bars ago. Positive = rising, negative = falling; zero-line crossings flag trend changes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Momentum : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public Momentum(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Momentum", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        // The original uses SlateBlue (0x6A,0x5A,0xCD) for the Momentum zero line.
        AddLine("Zero line", 0, new ChartColor(0x6A, 0x5A, 0xCD));
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
        if (_barIdx == 0) { _output.Append(0); _barIdx++; return; }
        int back = Math.Min(_barIdx, Period);
        _output.Append(src[barsAgoOfCurrent] - src[barsAgoOfCurrent + back]);
        _barIdx++;
    }
}
