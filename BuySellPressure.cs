using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Buy/Sell Pressure.
/// the original runs at tick granularity. Bar-level approximation here:
///   buy = volume on green bars, sell = volume on red bars.
///   BuyPressure = buys/(buys+sells)·100, SellPressure = sells/(buys+sells)·100,
///   over a configurable rolling window for stability.
/// Plots DarkCyan / Crimson, 25/75 reference lines DimGray, sub-panel.
/// </summary>
[IndicatorDescription("Buy/Sell Pressure. Percentage split of recent volume between up-closing bars (buy pressure) and down-closing bars (sell pressure) over a 14-bar window; readings above 75 or below 25 mark dominant aggressor flow.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BuySellPressure : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _buy  = new();
    private readonly ChunkedSeries<double> _sell = new();
    private IndicatorOutputSeries? _bW, _sW;
    private double _buys, _sells;
    private const int Window = 14;
    private readonly System.Collections.Generic.Queue<(double b, double s)> _q = new();

    public BuySellPressure(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Buy Pressure",  _buy,  PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "25/75") });
        AddPlot(new Plot("Sell Pressure", _sell, PlotStyle.Line, PlotColors.Crimson,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Down, "25/75") });
        AddLine("Upper", 75, PlotColors.DimGray);
        AddLine("Lower", 25, PlotColors.DimGray);
    }

    public ISeries<double> BuyPressure  => _bW ??= new IndicatorOutputSeries(_buy,  Bars);
    public ISeries<double> SellPressure => _sW ??= new IndicatorOutputSeries(_sell, Bars);

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
        double b = bar.Close >= bar.Open ? bar.Volume : 0;
        double s = bar.Close < bar.Open ? bar.Volume : 0;
        _buys += b; _sells += s;
        _q.Enqueue((b, s));
        if (_q.Count > Window)
        {
            var old = _q.Dequeue();
            _buys -= old.b; _sells -= old.s;
        }
        double total = _buys + _sells;
        if (total == 0) { _buy.Append(50); _sell.Append(50); return; }
        _buy.Append(_buys / total * 100);
        _sell.Append(_sells / total * 100);
    }
}
