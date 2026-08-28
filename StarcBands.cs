using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// STARC Bands (Stoller Average Range Channels) — derived from Skender.Stock.Indicators (Apache-2.0).
///   Center = SMA(Close, SmaPeriods)
///   Upper  = Center + Multiplier · ATR(AtrPeriods)
///   Lower  = Center − Multiplier · ATR(AtrPeriods)
/// Defaults SmaPeriods=5, Multiplier=2, AtrPeriods=10. DarkGray center, DodgerBlue bands, overlay.
/// </summary>
[IndicatorDescription("STARC Bands (Stoller Average Range Channels): SMA(Close) center with upper/lower bands offset by Multiplier times Wilder ATR. An ATR-based envelope similar to Keltner Channels.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class StarcBands : IndicatorBase
{
    private readonly ChunkedSeries<double> _center = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _cW, _uW, _lW;

    private RollingSum _closeSum = null!;
    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;
    private double? _prevClose;

    public StarcBands(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Center", _center, PlotStyle.Line, PlotColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "SmaPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmaPeriods { get; set; } = 5;

    [IndicatorParameter(DisplayName = "Multiplier", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.1)]
    public double Multiplier { get; set; } = 2;

    [IndicatorParameter(DisplayName = "AtrPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int AtrPeriods { get; set; } = 10;

    public ISeries<double> Center => _cW ??= new IndicatorOutputSeries(_center, Bars);
    public ISeries<double> Upper  => _uW ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _lW ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (SmaPeriods < 1 || AtrPeriods < 1) throw new ArgumentOutOfRangeException();
        if (Multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));
        _closeSum = new RollingSum(SmaPeriods);
        _atrSeeded = false; _atrSeedSum = 0; _atrSeedCount = 0;
        _prevClose = null;
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
        _closeSum.Push(bar.Close);

        // True Range + Wilder ATR.
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        _prevClose = bar.Close;

        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= AtrPeriods)
            {
                _atr = _atrSeedSum / AtrPeriods;
                _atrSeeded = true;
            }
        }
        else _atr = (_atr * (AtrPeriods - 1) + tr) / AtrPeriods;

        if (!_atrSeeded || _closeSum.Count < SmaPeriods)
        {
            _center.Append(double.NaN);
            _upper.Append(double.NaN);
            _lower.Append(double.NaN);
            return;
        }
        double sma = _closeSum.Sum / SmaPeriods;
        double off = Multiplier * _atr;
        _center.Append(sma);
        _upper.Append(sma + off);
        _lower.Append(sma - off);
    }
}
