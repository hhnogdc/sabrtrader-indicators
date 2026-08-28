using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Strength Index.
///
/// Two plots: RSI (DodgerBlue, Default), Avg (Goldenrod, EMA of RSI
/// with α=2/(Smooth+1)). Two reference lines: 30 / 70 (DarkCyan).
///
/// the original formula:
///   Per bar (post-warmup): up = max(close-prevClose, 0),
///   down = max(prevClose-close, 0). Until bar (Period-1) accumulate
///   sums; at bar Period seed avgUp/avgDown = SMA(up,Period) /
///   SMA(down,Period); afterwards Wilder smoothing
///   <c>avg_t = (avg_{t-1}·(Period−1) + current) / Period</c>.
///   Then <c>RSI = avgDown == 0 ? 100 : 100 − 100/(1 + avgUp/avgDown)</c>.
///   Singularity: when Period &lt; 3, Avg seeded with 50 at bar 0.
///   Otherwise Avg seeds with 50 at bar Period-2.
///
/// Defaults: Period=14, Smooth=3. Range each [1, int.MaxValue].
/// Sub-panel.
/// </summary>
[IndicatorDescription("Relative Strength Index (Wilder): 100 - 100/(1 + avgGain/avgLoss) with Wilder-smoothed gains and losses over Period bars, plus an EMA-smoothed Avg line. Overbought above 70, oversold below 30.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Rsi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _rsi = new();
    private readonly ChunkedSeries<double> _avg = new();
    private IndicatorOutputSeries? _rsiWrap;
    private IndicatorOutputSeries? _avgWrap;

    private double _avgUp, _avgDown;
    private double _seedUp, _seedDown;
    private bool _seeded;
    private double _alpha, _oneMinusAlpha;
    private double _avgValue;
    private bool _avgInitialised;
    private double? _prevClose;
    private int _deltaCount;        // number of close-to-close deltas seen

    public Rsi(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        // Plot semantics (A4): bounded 0–100 momentum oscillator, higher = more
        // bullish, with the canonical 30/70 reference levels — so an automated
        // consumer interprets the value without recognising "RSI" by name.
        var oscillator = new PlotSemantics(
            PlotKind.Oscillator, RangeMin: 0, RangeMax: 100,
            PlotValueUnit.Percent, PlotBullishDirection.Up,
            Reference: "30 oversold / 70 overbought");
        AddPlot(new Plot("RSI", _rsi, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = oscillator });
        AddPlot(new Plot("Avg", _avg, PlotStyle.Line, PlotColors.Goldenrod,  1.0) { Semantics = oscillator });
        AddLine("Lower", 30, PlotColors.DarkCyan);
        AddLine("Upper", 70, PlotColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period",
        Description = "Wilder smoothing period for avg gain / loss.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth",
        Description = "EMA smoothing period applied to RSI (yields Avg).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    /// <summary>RSI (Default) — chainable bar-sourced series.</summary>
    public ISeries<double> Default => _rsiWrap ??= new IndicatorOutputSeries(_rsi, Bars);

    /// <summary>Avg (Smooth-EMA of RSI).</summary>
    public ISeries<double> Avg => _avgWrap ??= new IndicatorOutputSeries(_avg, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth), "Smooth must be at least 1.");
        _avgUp = _avgDown = 0;
        _seedUp = _seedDown = 0;
        _seeded = false;
        _alpha = 2.0 / (1 + Smooth);
        _oneMinusAlpha = 1.0 - _alpha;
        _avgValue = 0;
        _avgInitialised = false;
        _prevClose = null;
        _deltaCount = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++)
        {
            int barsAgo = src.Count - 1 - absIdx;
            ConsumeClose(src[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        ConsumeClose(src[0]);
    }

    private void ConsumeClose(double close)
    {
        if (_prevClose is null)
        {
            // The original bar 0: no delta; emit NaN for RSI and (if Period<3) 50 for Avg.
            _rsi.Append(double.NaN);
            if (Period < 3) { _avgValue = 50; _avgInitialised = true; _avg.Append(50); }
            else            _avg.Append(double.NaN);
            _prevClose = close;
            return;
        }

        double delta = close - _prevClose.Value;
        double up   = delta > 0 ?  delta : 0;
        double down = delta < 0 ? -delta : 0;
        _prevClose = close;
        _deltaCount++;

        if (!_seeded)
        {
            _seedUp   += up;
            _seedDown += down;
            // The original seeds Avg at bar Period-1 with value 50, AVANT RSI exists.
            if (_deltaCount + 1 == Period - 1 && !_avgInitialised && Period >= 3)
            {
                _avgValue = 50;
                _avgInitialised = true;
            }
            if (_deltaCount < Period)
            {
                _rsi.Append(double.NaN);
                if (_avgInitialised) _avg.Append(_avgValue);
                else                 _avg.Append(double.NaN);
                return;
            }
            // First seed reached.
            _avgUp   = _seedUp   / Period;
            _avgDown = _seedDown / Period;
            _seeded = true;
        }
        else
        {
            _avgUp   = (_avgUp   * (Period - 1) + up)   / Period;
            _avgDown = (_avgDown * (Period - 1) + down) / Period;
        }

        double rsi = _avgDown == 0 ? 100 : 100 - 100 / (1 + _avgUp / _avgDown);
        _rsi.Append(rsi);

        if (!_avgInitialised)
        {
            _avgValue = rsi;
            _avgInitialised = true;
        }
        else
        {
            _avgValue = _alpha * rsi + _oneMinusAlpha * _avgValue;
        }
        _avg.Append(_avgValue);
    }
}
