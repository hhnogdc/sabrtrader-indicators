using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum AtrStopEndType { Close, HighLow }

/// <summary>
/// ATR Trailing Stop — derived from Skender.Stock.Indicators (Apache-2.0).
/// Trend-aware trailing stop that flips between upper and lower band based on Close vs band.
/// Bands are computed from Close (EndType=Close) or High/Low (EndType=HighLow) ± Multiplier·ATR.
/// Upper band can only tighten (decrease) or reset when prevClose breaks it; same for lower.
/// Defaults Period=21, Multiplier=3, EndType=Close.
/// Plots: AtrStop (Goldenrod), BuyStop (LimeGreen Dots), SellStop (Red Dots).
/// </summary>
[IndicatorDescription("ATR Trailing Stop. Volatility-based chandelier-style stop that flips between an upper band (close + Multiplier x ATR) and lower band (close - Multiplier x ATR) when price breaks through, plotted as a trend-following stop level.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class AtrStop : IndicatorBase
{
    private readonly ChunkedSeries<double> _atrStop = new();
    private readonly ChunkedSeries<double> _buyStop  = new();
    private readonly ChunkedSeries<double> _sellStop = new();
    private IndicatorOutputSeries? _aW, _bW, _sW;

    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;
    private double? _prevClose;
    private bool _isBullish;
    private double _upperBand;
    private double _lowerBand;
    private bool _bandsInitialised;
    private int _barIdx;

    public AtrStop(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("AtrStop",  _atrStop,  PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("BuyStop",  _buyStop,  PlotStyle.Dots, PlotColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("SellStop", _sellStop, PlotStyle.Dots, PlotColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "Period",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int    Period    { get; set; } = 21;

    [IndicatorParameter(DisplayName = "Multiplier", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.5)]
    public double Multiplier { get; set; } = 3;

    [IndicatorParameter(DisplayName = "EndType")]
    public AtrStopEndType EndType { get; set; } = AtrStopEndType.Close;

    public ISeries<double> Output   => _aW ??= new IndicatorOutputSeries(_atrStop,  Bars);
    public ISeries<double> BuyStop  => _bW ??= new IndicatorOutputSeries(_buyStop,  Bars);
    public ISeries<double> SellStop => _sW ??= new IndicatorOutputSeries(_sellStop, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));
        _atrSeeded = _bandsInitialised = false;
        _atrSeedSum = 0; _atrSeedCount = 0;
        _prevClose = null;
        _isBullish = true;
        _barIdx = 0;
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
        // True Range + Wilder ATR.
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= Period) { _atr = _atrSeedSum / Period; _atrSeeded = true; }
        }
        else _atr = (_atr * (Period - 1) + tr) / Period;

        // Skender warmup: i < Period → NaN.
        if (_barIdx < Period || _prevClose is null)
        {
            _atrStop.Append(double.NaN); _buyStop.Append(double.NaN); _sellStop.Append(double.NaN);
            _prevClose = bar.Close;
            _barIdx++;
            return;
        }

        double upperEval = EndType == AtrStopEndType.Close ? bar.Close + Multiplier * _atr : bar.High + Multiplier * _atr;
        double lowerEval = EndType == AtrStopEndType.Close ? bar.Close - Multiplier * _atr : bar.Low  - Multiplier * _atr;

        if (!_bandsInitialised)
        {
            _isBullish = bar.Close >= _prevClose.Value;
            _upperBand = upperEval;
            _lowerBand = lowerEval;
            _bandsInitialised = true;
        }
        if (upperEval < _upperBand || _prevClose.Value > _upperBand) _upperBand = upperEval;
        if (lowerEval > _lowerBand || _prevClose.Value < _lowerBand) _lowerBand = lowerEval;

        // Flip: Close ≤ active stop → reverse.
        if (bar.Close <= (_isBullish ? _lowerBand : _upperBand))
        {
            _isBullish = false;
            _atrStop.Append(_upperBand);
            _buyStop.Append(_upperBand);
            _sellStop.Append(double.NaN);
        }
        else
        {
            _isBullish = true;
            _atrStop.Append(_lowerBand);
            _buyStop.Append(double.NaN);
            _sellStop.Append(_lowerBand);
        }
        _prevClose = bar.Close;
        _barIdx++;
    }
}
