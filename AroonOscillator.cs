using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Aroon Oscillator — 1-on-1 port of NT @AroonOscillator.
/// Value = AroonUp − AroonDown. Range −100..+100.
/// Default Period=14, DodgerBlue plot, zero reference line, sub-panel.
/// </summary>
[IndicatorDescription("Aroon Oscillator. Single -100 to +100 line equal to Aroon Up minus Aroon Down. Positive values indicate uptrend strength, negative values downtrend strength, and zero crossings mark trend reversals.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class AroonOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Aroon _aroon = null!;

    public AroonOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Aroon Oscillator", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -100, 100, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _aroon = new Aroon(this, bars: Bars) { Period = Period };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _aroon.Up.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            _output.Append(_aroon.Up[b] - _aroon.Down[b]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (_aroon.Up.Count == 0) return;
        _output.Append(_aroon.Up[0] - _aroon.Down[0]);
    }
}
