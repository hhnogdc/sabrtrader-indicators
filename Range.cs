using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Per-bar range (High − Low).
/// Single histogram-style plot, Goldenrod 2px, sub-panel.
/// No parameters.
/// </summary>
[IndicatorDescription("Per-bar price range (High - Low) as a histogram. A simple volatility gauge showing how wide each bar travelled.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Range : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public Range(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Range", _output, PlotStyle.Histogram, PlotColors.Goldenrod, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            _output.Append(bar.High - bar.Low);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        var bar = bars[0];
        _output.Append(bar.High - bar.Low);
    }
}
