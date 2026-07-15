using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// SuperTrend — 1-on-1 port of Skender SuperTrend.
/// HL2-centered ATR bands with trend-aware trailing logic:
///   mid    = (High + Low) / 2
///   atr    = Wilder ATR(Period)
///   upper  = mid + Multiplier · atr  (tightens or resets when prevClose breaks)
///   lower  = mid − Multiplier · atr  (mirror)
///   SuperTrend = active band, flips when Close crosses through.
/// Defaults Period=10, Multiplier=3.
/// Plots: SuperTrend (Goldenrod), UpperBand (Red Dots), LowerBand (LimeGreen Dots).
/// </summary>
[IndicatorDescription("SuperTrend trailing stop. Plots HL2-centered ATR bands and flips direction when close crosses through, identifying the prevailing trend and trailing-stop level.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class SuperTrend : IndicatorBase
{
    private readonly ChunkedSeries<double> _superTrend = new();
    private readonly ChunkedSeries<double> _upperOnly  = new();
    private readonly ChunkedSeries<double> _lowerOnly  = new();
    private IndicatorOutputSeries? _stW, _uW, _lW;

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

    public SuperTrend(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("SuperTrend", _superTrend, PlotStyle.Line, NTColors.Goldenrod, 1.5) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("UpperBand",  _upperOnly,  PlotStyle.Dots, NTColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("LowerBand",  _lowerOnly,  PlotStyle.Dots, NTColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int    Period    { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Multiplier", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.5)]
    public double Multiplier { get; set; } = 3;

    public ISeries<double> Output    => _stW ??= new IndicatorOutputSeries(_superTrend, Bars);
    public ISeries<double> UpperBand => _uW  ??= new IndicatorOutputSeries(_upperOnly,  Bars);
    public ISeries<double> LowerBand => _lW  ??= new IndicatorOutputSeries(_lowerOnly,  Bars);

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
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose!.Value), Math.Abs(bar.Low - _prevClose!.Value)));
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= Period) { _atr = _atrSeedSum / Period; _atrSeeded = true; }
        }
        else _atr = (_atr * (Period - 1) + tr) / Period;

        if (_barIdx < Period)
        {
            _superTrend.Append(double.NaN); _upperOnly.Append(double.NaN); _lowerOnly.Append(double.NaN);
            _prevClose = bar.Close;
            _barIdx++;
            return;
        }

        double mid = (bar.High + bar.Low) / 2;
        double upperEval = mid + Multiplier * _atr;
        double lowerEval = mid - Multiplier * _atr;

        if (!_bandsInitialised)
        {
            _isBullish = bar.Close >= mid;
            _upperBand = upperEval;
            _lowerBand = lowerEval;
            _bandsInitialised = true;
        }
        if (upperEval < _upperBand || _prevClose!.Value > _upperBand) _upperBand = upperEval;
        if (lowerEval > _lowerBand || _prevClose!.Value < _lowerBand) _lowerBand = lowerEval;

        if (bar.Close <= (_isBullish ? _lowerBand : _upperBand))
        {
            _superTrend.Append(_upperBand);
            _upperOnly.Append(_upperBand);
            _lowerOnly.Append(double.NaN);
            _isBullish = false;
        }
        else
        {
            _superTrend.Append(_lowerBand);
            _lowerOnly.Append(_lowerBand);
            _upperOnly.Append(double.NaN);
            _isBullish = true;
        }
        _prevClose = bar.Close;
        _barIdx++;
    }
}
