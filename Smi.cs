using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Stochastic Momentum Index (SMI, Blau) — derived from Skender.Stock.Indicators (Apache-2.0).
///   hH = max(High, P), lL = min(Low, P)
///   sm = Close − (hH + lL) / 2
///   hl = hH − lL
///   smEma1 = EMA(sm, FirstSmoothPeriods); hlEma1 = EMA(hl, FirstSmoothPeriods)
///   smEma2 = EMA(smEma1, SecondSmoothPeriods); hlEma2 = EMA(hlEma1, SecondSmoothPeriods)
///   SMI = 100·smEma2 / (0.5·hlEma2)
///   Signal = EMA(SMI, SignalPeriods)
/// EMAs are seeded with the first valid value (NOT SMA — matches Skender).
/// Defaults Period=13, FirstSmoothPeriods=25, SecondSmoothPeriods=2, SignalPeriods=3.
/// DodgerBlue SMI, Crimson Signal, ±40 reference lines, sub-panel.
/// </summary>
[IndicatorDescription("Stochastic Momentum Index (Blau): double-EMA-smoothed ratio of Close-to-midrange distance versus High-Low range, scaled to +/- 100. Like a stochastic but less noisy; +/- 40 mark overbought/oversold zones.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Smi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _smi = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _smiW, _sigW;

    private RollingMinMax _hMax = null!, _lMin = null!;
    private double _smEma1, _smEma2, _hlEma1, _hlEma2, _signalVal;
    private bool _seededEmas, _seededSignal;
    private double _k1, _k2, _kS;

    public Smi(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("SMI",    _smi,    PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, PlotColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Overbought",  40, PlotColors.DarkGray);
        AddLine("Oversold",   -40, PlotColors.DarkGray);
        AddLine("Zero line",    0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period",              MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period              { get; set; } = 13;

    [IndicatorParameter(DisplayName = "FirstSmoothPeriods",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int FirstSmoothPeriods  { get; set; } = 25;

    [IndicatorParameter(DisplayName = "SecondSmoothPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SecondSmoothPeriods { get; set; } = 2;

    [IndicatorParameter(DisplayName = "SignalPeriods",       MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriods       { get; set; } = 3;

    public ISeries<double> Output => _smiW ??= new IndicatorOutputSeries(_smi,    Bars);
    public ISeries<double> Signal => _sigW ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || FirstSmoothPeriods < 1 || SecondSmoothPeriods < 1 || SignalPeriods < 1)
            throw new ArgumentOutOfRangeException();
        _k1 = 2d / (FirstSmoothPeriods  + 1);
        _k2 = 2d / (SecondSmoothPeriods + 1);
        _kS = 2d / (SignalPeriods       + 1);
        _hMax = new RollingMinMax(Period, trackMax: true);
        _lMin = new RollingMinMax(Period, trackMax: false);
        _seededEmas = _seededSignal = false;
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
        _hMax.Push(bar.High);
        _lMin.Push(bar.Low);
        if (_hMax.Count < Period) { _smi.Append(double.NaN); _signal.Append(double.NaN); return; }

        double hH = _hMax.Value, lL = _lMin.Value;
        double sm = bar.Close - 0.5 * (hH + lL);
        double hl = hH - lL;

        if (!_seededEmas) { _smEma1 = sm; _smEma2 = sm; _hlEma1 = hl; _hlEma2 = hl; _seededEmas = true; }
        _smEma1 = _smEma1 + _k1 * (sm - _smEma1);
        _hlEma1 = _hlEma1 + _k1 * (hl - _hlEma1);
        _smEma2 = _smEma2 + _k2 * (_smEma1 - _smEma2);
        _hlEma2 = _hlEma2 + _k2 * (_hlEma1 - _hlEma2);

        double smi = 0.5 * _hlEma2 != 0 ? 100 * _smEma2 / (0.5 * _hlEma2) : 0;
        _smi.Append(smi);

        if (!_seededSignal) { _signalVal = smi; _seededSignal = true; }
        else                  _signalVal = _signalVal + _kS * (smi - _signalVal);
        _signal.Append(_signalVal);
    }
}
