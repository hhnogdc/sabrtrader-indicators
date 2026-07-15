using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// On-Balance Volume (Granville 1963). Cumulative signed-volume:
/// add the bar's volume when its close is higher than the previous
/// close, subtract when lower, ignore on equal.
///
/// First bar always emits 0 — there is no prior close to compare
/// against, so direction is undefined. Subsequent bars apply the
/// directional rule.
///
/// No parameters, single plot. Often inspected for divergence
/// against price (rising price + falling OBV = distribution).
/// </summary>
[IndicatorDescription("On-Balance Volume (Granville): cumulative signed volume - add bar volume when close rises, subtract when it falls. Watch for divergence against price to spot accumulation or distribution.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Obv : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputAsBarSourced;
    private double _running;
    private double? _prevClose;

    /// <summary>
    /// OBV needs close + volume from a full bar series. Pass a
    /// non-default <paramref name="bars"/> for MTF OBV.
    /// </summary>
    public Obv(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("OBV", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    /// <summary>Per-bar OBV as a bar-sourced series (chainable as input).</summary>
    public ISeries<double> Output =>
        _outputAsBarSourced ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _running = 0;
        _prevClose = null;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            _output.Append(ConsumeBar(bars[barsAgo]));
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        _output.Append(ConsumeBar(bars[0]));
    }

    private double ConsumeBar(Bar bar)
    {
        if (_prevClose is null)
        {
            _prevClose = bar.Close;
            return 0;
        }
        if (bar.Close > _prevClose.Value)      _running += bar.Volume;
        else if (bar.Close < _prevClose.Value) _running -= bar.Volume;
        // equal close → no change
        _prevClose = bar.Close;
        return _running;
    }
}
