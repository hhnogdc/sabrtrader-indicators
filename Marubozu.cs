using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Marubozu single-bar candlestick pattern — derived from Skender.Stock.Indicators (Apache-2.0).
/// Detects when the body (|close − open|) covers ≥ MinBodyPercent (default 95%) of the bar's
/// total range (high − low). On bullish match (close ≥ open) the dot is drawn
/// <see cref="OffsetTicks"/> ticks BELOW the bar's Low; on bearish match the same number of
/// ticks ABOVE the bar's High — so the marker sits clear of the candle instead of being
/// buried at its extreme (a marubozu's close IS the high/low). OffsetTicks defaults to 4.
/// LimeGreen Bull / Red Bear Dot plots, overlay.
/// </summary>
[IndicatorDescription("Marubozu candlestick detector: marks bars whose body covers at least N% of the high-low range (default 95%), with separate bullish (close>=open) and bearish dot plots.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Marubozu : IndicatorBase
{
    private readonly ChunkedSeries<double> _bull = new();
    private readonly ChunkedSeries<double> _bear = new();
    private IndicatorOutputSeries? _bullW, _bearW;
    private double _tickSize = 0.25;

    public Marubozu(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Bullish Marubozu", _bull, PlotStyle.Dots, PlotColors.Blue,    6.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Bearish Marubozu", _bear, PlotStyle.Dots, PlotColors.Magenta, 6.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "MinBodyPercent",
        Description = "Minimum body / range %, e.g. 95 = body must cover ≥ 95% of high−low.",
        MinValue = 50, MaxValue = 100, Step = 1)]
    public double MinBodyPercent { get; set; } = 95;

    [IndicatorParameter(DisplayName = "OffsetTicks",
        Description = "Ticks to offset the dot off the bar: bullish below the Low, bearish above the High. 0 = draw at the bar's extreme.",
        MinValue = 0, MaxValue = 1000, Step = 1)]
    public double OffsetTicks { get; set; } = 4;

    public ISeries<double> Bullish => _bullW ??= new IndicatorOutputSeries(_bull, Bars);
    public ISeries<double> Bearish => _bearW ??= new IndicatorOutputSeries(_bear, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (MinBodyPercent < 50 || MinBodyPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(MinBodyPercent));
        if (OffsetTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(OffsetTicks));
        _tickSize = ctx.TickSize is > 0 ? ctx.TickSize!.Value : 0.25;
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
        double range = bar.High - bar.Low;
        double body  = Math.Abs(bar.Close - bar.Open);
        double bodyPct = range == 0 ? 0 : body / range;
        double thresh = MinBodyPercent / 100.0;
        bool match = bodyPct >= thresh;
        bool bullish = bar.Close >= bar.Open;
        double offset = OffsetTicks * _tickSize;
        // Draw the dot clear of the candle: bullish below the Low, bearish above the High.
        _bull.Append(match &&  bullish ? bar.Low  - offset : double.NaN);
        _bear.Append(match && !bullish ? bar.High + offset : double.NaN);
    }
}
