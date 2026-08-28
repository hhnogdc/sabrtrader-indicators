using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Counter.
/// Per-bar volume histogram, sub-panel, DodgerBlue 2px. No params.
/// </summary>
[IndicatorDescription("Per-bar volume histogram (equivalent to the Volume indicator). Plots raw traded volume in a sub-panel with no smoothing.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class VolumeCounter : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public VolumeCounter(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Volume Counter", _output, PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, 0, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
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
