using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Up/Down.
/// Two histogram plots: UpVolume (DarkCyan, when Close ≥ Open) and
/// DownVolume (Crimson, when Close &lt; Open). Whichever is active
/// shows the bar's volume; the other emits 0 (the original's <c>Reset()</c>
/// becomes NaN there; we use 0 to match the histogram baseline).
/// Sub-panel, no parameters.
/// </summary>
[IndicatorDescription("Splits bar volume into two histograms: up volume on bars closing at or above the open, down volume on bars closing below it. Visualises buy- vs sell-side participation.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class VolumeUpDown : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _up   = new();
    private readonly ChunkedSeries<double> _down = new();
    private IndicatorOutputSeries? _upWrap, _downWrap;

    public VolumeUpDown(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Up Volume",   _up,   PlotStyle.Histogram, PlotColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Down Volume", _down, PlotStyle.Histogram, PlotColors.Crimson,  2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    public ISeries<double> UpVolume   => _upWrap   ??= new IndicatorOutputSeries(_up,   Bars);
    public ISeries<double> DownVolume => _downWrap ??= new IndicatorOutputSeries(_down, Bars);

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
        if (bar.Close >= bar.Open) { _up.Append(bar.Volume); _down.Append(0); }
        else                        { _up.Append(0); _down.Append(bar.Volume); }
    }
}
