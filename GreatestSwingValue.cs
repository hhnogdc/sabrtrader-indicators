using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Greatest Swing Value (Larry Williams) — measures the average
/// adverse swing on up vs down bars, the building block of Williams'
/// breakout-entry levels (enter long at <c>Open + Mult·GSVBuy</c>,
/// short at <c>Open − Mult·GSVSell</c>).
///
/// Math (per bar, averaged over the last <c>Period</c> QUALIFYING bars):
///   <code>
///   down bar (Close &lt; Open):  buySwing  = High − Open      → GSVBuy  = Mult · avg(buySwing)
///   up bar   (Close &gt; Open):  sellSwing = Open − Low       → GSVSell = Mult · avg(sellSwing)
///   </code>
/// Each average is taken over only the bars that qualified (down bars
/// for the buy swing, up bars for the sell swing), so doji/zero bars
/// don't dilute the measure.
///
/// Defaults Period=5, Mult=1.0 (Williams' originals). Plots GSVBuy
/// (ForestGreen) / GSVSell (Crimson) in price units, sub-panel.
/// </summary>
[IndicatorDescription("Greatest Swing Value (Larry Williams): the average High-minus-Open swing on down bars (GSVBuy) and Open-minus-Low swing on up bars (GSVSell) over N qualifying bars, scaled by a multiplier — the basis for Williams' volatility breakout entry stops.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class GreatestSwingValue : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _buy = new();
    private readonly ChunkedSeries<double> _sell = new();
    private IndicatorOutputSeries? _buyWrap, _sellWrap;

    private double[] _buyBuf = [];
    private double[] _sellBuf = [];
    private int _buyCount, _buyIdx;
    private int _sellCount, _sellIdx;
    private double _buyAvg, _sellAvg;   // carried so non-qualifying bars repeat the last value

    public GreatestSwingValue(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var sem = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("GSV Buy",  _buy,  PlotStyle.Line, PlotColors.ForestGreen, 1.0) { Semantics = sem });
        AddPlot(new Plot("GSV Sell", _sell, PlotStyle.Line, PlotColors.Crimson,     1.0) { Semantics = sem });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Number of qualifying bars to average.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 5;

    [IndicatorParameter(DisplayName = "Mult", Description = "Multiplier applied to the average swing.", MinValue = 0.0, MaxValue = double.MaxValue, Step = 0.1)]
    public double Mult { get; set; } = 1.0;

    public ISeries<double> Buy  => _buyWrap  ??= new IndicatorOutputSeries(_buy,  Bars);
    public ISeries<double> Sell => _sellWrap ??= new IndicatorOutputSeries(_sell, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Mult < 0) throw new ArgumentOutOfRangeException(nameof(Mult));
        _buyBuf = new double[Period];
        _sellBuf = new double[Period];
        _buyCount = _buyIdx = _sellCount = _sellIdx = 0;
        _buyAvg = _sellAvg = 0;
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
        if (bar.Close < bar.Open)
        {
            double swing = bar.High - bar.Open;
            if (_buyCount < Period) _buyCount++;
            _buyBuf[_buyIdx] = swing;
            _buyIdx = (_buyIdx + 1) % Period;
            _buyAvg = Average(_buyBuf, _buyCount);
        }
        else if (bar.Close > bar.Open)
        {
            double swing = bar.Open - bar.Low;
            if (_sellCount < Period) _sellCount++;
            _sellBuf[_sellIdx] = swing;
            _sellIdx = (_sellIdx + 1) % Period;
            _sellAvg = Average(_sellBuf, _sellCount);
        }
        // Doji (Close == Open) contributes to neither side; both lines hold.

        _buy.Append(Mult * _buyAvg);
        _sell.Append(Mult * _sellAvg);
    }

    private static double Average(double[] buf, int count)
    {
        if (count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < count; i++) sum += buf[i];
        return sum / count;
    }
}
