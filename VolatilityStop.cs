using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volatility Stop (Welles Wilder) — derived from Skender.Stock.Indicators (Apache-2.0).
/// Stop and Reverse trailing stop driven by Close vs the SAR threshold:
///   arc = Multiplier · ATR(Period)
///   sar = isLong ? sic − arc : sic + arc      (sic = significant favourable close)
///   When close breaches sar → flip direction, reset sic = close
///   Else extend sic = isLong ? max(sic, close) : min(sic, close)
/// Defaults Period=7, Multiplier=3. NaN until first true stop event (per Skender's
/// "trim warmup back to first stop" logic).
/// Plots: Sar (Goldenrod), UpperBand (Red Dots), LowerBand (LimeGreen Dots).
/// </summary>
[IndicatorDescription("Welles Wilder's Volatility Stop. ATR-multiple trailing stop-and-reverse: long stop = significant low minus Multiplier*ATR (mirror for shorts); flips direction when Close breaches the level.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class VolatilityStop : IndicatorBase
{
    private readonly ChunkedSeries<double> _sar       = new();
    private readonly ChunkedSeries<double> _upperBand = new();
    private readonly ChunkedSeries<double> _lowerBand = new();
    private IndicatorOutputSeries? _sW, _uW, _lW;

    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;
    private double? _prevClose;
    private double _sic;          // significant favourable close
    private bool _isLong;
    private int _barIdx;
    private bool _firstStopSeen;

    public VolatilityStop(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("SAR",       _sar,       PlotStyle.Dots, PlotColors.Goldenrod, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("UpperBand", _upperBand, PlotStyle.Dots, PlotColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("LowerBand", _lowerBand, PlotStyle.Dots, PlotColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int    Period    { get; set; } = 7;

    [IndicatorParameter(DisplayName = "Multiplier", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.5)]
    public double Multiplier { get; set; } = 3;

    public ISeries<double> Output    => _sW ??= new IndicatorOutputSeries(_sar,       Bars);
    public ISeries<double> UpperBand => _uW ??= new IndicatorOutputSeries(_upperBand, Bars);
    public ISeries<double> LowerBand => _lW ??= new IndicatorOutputSeries(_lowerBand, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));
        _atrSeeded = false; _atrSeedSum = 0; _atrSeedCount = 0;
        _prevClose = null;
        _isLong = true;
        _firstStopSeen = false;
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
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= Period) { _atr = _atrSeedSum / Period; _atrSeeded = true; }
        }
        else _atr = (_atr * (Period - 1) + tr) / Period;

        // Warmup: first Period bars. Skender uses 0..Period-1 to guess initial trend and sic.
        if (_barIdx < Period)
        {
            if (_barIdx == 0) { _sic = bar.Close; _isLong = true; }
            else _sic = _isLong ? Math.Max(_sic, bar.Close) : Math.Min(_sic, bar.Close);
            _sar.Append(double.NaN); _upperBand.Append(double.NaN); _lowerBand.Append(double.NaN);
            _prevClose = bar.Close;
            _barIdx++;
            if (_barIdx == Period)
                _isLong = bar.Close > _sic - Multiplier * _atr; // tentative
            return;
        }

        double arc = _atr * Multiplier;
        double sar = _isLong ? _sic - arc : _sic + arc;

        bool flipped = (_isLong && bar.Close < sar) || (!_isLong && bar.Close > sar);
        if (flipped)
        {
            _sic = bar.Close;
            _isLong = !_isLong;
            _firstStopSeen = true;
        }
        else
        {
            _sic = _isLong ? Math.Max(_sic, bar.Close) : Math.Min(_sic, bar.Close);
        }

        if (!_firstStopSeen)
        {
            // Pre-first-stop is a "guess" per Skender — emit NaN.
            _sar.Append(double.NaN); _upperBand.Append(double.NaN); _lowerBand.Append(double.NaN);
        }
        else
        {
            _sar.Append(sar);
            if (_isLong) { _lowerBand.Append(sar); _upperBand.Append(double.NaN); }
            else         { _upperBand.Append(sar); _lowerBand.Append(double.NaN); }
        }
        _prevClose = bar.Close;
        _barIdx++;
    }
}
