using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Doji single-bar candlestick pattern — derived from Skender.Stock.Indicators (Apache-2.0).
/// Detects when |Close/Open − 1| ≤ MaxPriceChangePercent/100. On match, emits Close as
/// the match price (otherwise NaN). Yellow Dot at the close price, no panel hint (overlay).
/// Defaults MaxPriceChangePercent=0.1 (i.e. 0.1%).
/// </summary>
[IndicatorDescription("Doji candlestick detector: flags bars where |Close/Open − 1| is below the MaxPriceChangePercent threshold, marking indecision candles at the close price.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Doji : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public Doji(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Doji", _output, PlotStyle.Dots, PlotColors.Yellow, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "MaxPriceChangePercent",
        Description = "Max |close/open − 1| in % to qualify as a doji.",
        MinValue = 0, MaxValue = 100, Step = 0.01)]
    public double MaxPriceChangePercent { get; set; } = 0.1;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (MaxPriceChangePercent < 0 || MaxPriceChangePercent > 100)
            throw new ArgumentOutOfRangeException(nameof(MaxPriceChangePercent));
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
        double thresh = MaxPriceChangePercent / 100.0;
        if (bar.Open != 0 && Math.Abs(bar.Close / bar.Open - 1) <= thresh)
            _output.Append(bar.Close);
        else
            _output.Append(double.NaN);
    }
}
