using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Psychological Line — 1-on-1 port of NT @PsychologicalLine.
/// % of up-bars (Close &gt; Open) over the last Period bars · 100.
/// Defaults Period=10, DodgerBlue, 25/75 reference lines (DarkCyan),
/// sub-panel.
/// </summary>
[IndicatorDescription("Psychological Line: percentage of up-bars (Close > Open) within the last Period bars, scaled 0-100. Overbought/oversold markers at 75 and 25 highlight sentiment extremes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class PsychologicalLine : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _upBars = null!;

    public PsychologicalLine(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Psychological Line", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "overbought/oversold") });
        AddLine("Overbought", 75, NTColors.DarkCyan);
        AddLine("Oversold",   25, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window for up-bar percentage.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _upBars = new RollingSum(Period);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    private void Consume(Bar bar)
    {
        _upBars.Push(bar.Close > bar.Open ? 1 : 0);
        // Running-mean during warmup: divide by samples seen so far.
        // NT divides by min(barIdx+1, Period). RollingSum.Sum / count.
        int count = Math.Min(_upBars.Count, Period);
        _output.Append(_upBars.Sum / count * 100);
    }
}
