using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Tick Counter.
/// Surfaces <see cref="Bar.TickCount"/> per bar as a single histogram
/// plot. No parameters. Sub-panel, DodgerBlue 2px.
/// </summary>
[IndicatorDescription("Plots the number of ticks (price updates) contained in each bar. Useful for gauging intra-bar activity, especially on time-based bars.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class TickCounter : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public TickCounter(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Tick Count", _output, PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, 0, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) _output.Append(bars[bars.Count - 1 - i].TickCount);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        _output.Append(Bars[0].TickCount);
    }
}
