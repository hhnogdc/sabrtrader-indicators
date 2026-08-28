using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Price Volume Trend.
/// Cumulative: PVT[0] = PVT[1] + Volume·(C − prevC)/prevC. Bar 0 → 0.
/// Plot DodgerBlue. Sub-panel. No parameters.
/// </summary>
[IndicatorDescription("Price Volume Trend: cumulative running sum of Volume * (Close - PrevClose) / PrevClose. A volume-weighted momentum line that confirms price moves when volume agrees.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class PriceVolumeTrend : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double? _prevClose;
    private double _running;

    public PriceVolumeTrend(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("PVT", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

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
        if (_prevClose is null || _prevClose.Value == 0)
        {
            _prevClose = bar.Close;
            _output.Append(0);
            return;
        }
        _running += bar.Volume * (bar.Close - _prevClose.Value) / _prevClose.Value;
        _prevClose = bar.Close;
        _output.Append(_running);
    }
}
