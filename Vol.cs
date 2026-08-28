using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume.
/// Single Histogram-style plot DodgerBlue 2px + zero reference line
/// DarkGray. Sub-panel. No parameters. The original runs OnEachTick by default;
/// our framework's Calculate cadence covers that via CalculationMode.
/// </summary>
[IndicatorDescription("Per-bar volume histogram. Plots raw bar volume in a sub-panel as the simplest volume display.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Vol : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public Vol(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Volume", _output, PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) _output.Append(bars[bars.Count - 1 - i].Volume);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        _output.Append(Bars[0].Volume);
    }
}
