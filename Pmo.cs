using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Price Momentum Oscillator (PMO, DecisionPoint) — 1-on-1 port of Skender Pmo.
///   ROC[i] = 100·(C[i]/C[i−1] − 1)
///   rocEma = custom-α EMA of ROC, α = 2/TimePeriods, seeded with SMA at bar TimePeriods
///   re = rocEma · 10
///   PMO = custom-α EMA of re, α = 2/SmoothPeriods, seeded with SMA at bar SmoothPeriods
///   Signal = EMA(PMO, SignalPeriods) with standard α = 2/(N+1), seeded with SMA at bar SignalPeriods
/// Defaults TimePeriods=35, SmoothPeriods=20, SignalPeriods=10.
/// DodgerBlue PMO, Crimson Signal, zero line, sub-panel.
/// </summary>
[IndicatorDescription("DecisionPoint Price Momentum Oscillator: doubly-smoothed rate-of-change with a signal-line EMA. A smoothed momentum gauge; signal-line crosses and zero-line crosses flag trades.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Pmo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _pmo = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _pmoW, _sigW;

    private double _prevPrice;
    private double _rocEma, _pmoValue, _signalValue;
    private bool _hasPrevPrice, _rocSeeded, _pmoSeeded, _signalSeeded;
    private double _rocSeedSum, _reSeedSum, _pmoSeedSum;
    private int _rocSeedCount, _reSeedCount, _pmoSeedCount;
    private double _kRoc, _kPmo, _kSig;
    private int _barIdx;

    public Pmo(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("PMO",    _pmo,    PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "TimePeriods",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int TimePeriods   { get; set; } = 35;

    [IndicatorParameter(DisplayName = "SmoothPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothPeriods { get; set; } = 20;

    [IndicatorParameter(DisplayName = "SignalPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriods { get; set; } = 10;

    public ISeries<double> Output => _pmoW ??= new IndicatorOutputSeries(_pmo,    Bars);
    public ISeries<double> Signal => _sigW ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (TimePeriods < 1 || SmoothPeriods < 1 || SignalPeriods < 1) throw new ArgumentOutOfRangeException();
        _kRoc = 2d / TimePeriods;
        _kPmo = 2d / SmoothPeriods;
        _kSig = 2d / (SignalPeriods + 1);
        _hasPrevPrice = _rocSeeded = _pmoSeeded = _signalSeeded = false;
        _rocSeedSum = _reSeedSum = _pmoSeedSum = 0;
        _rocSeedCount = _reSeedCount = _pmoSeedCount = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double price)
    {
        double roc = !_hasPrevPrice || _prevPrice == 0
            ? double.NaN
            : 100 * (price / _prevPrice - 1);
        _prevPrice = price;
        _hasPrevPrice = true;

        // ROC EMA (×10 stored to re-feed PMO).
        double rocEma;
        if (!_rocSeeded)
        {
            if (!double.IsNaN(roc)) { _rocSeedSum += roc; _rocSeedCount++; }
            if (_rocSeedCount >= TimePeriods)
            {
                _rocEma = _rocSeedSum / TimePeriods;
                _rocSeeded = true;
                rocEma = _rocEma;
            }
            else
            {
                _pmo.Append(double.NaN);
                _signal.Append(double.NaN);
                _barIdx++;
                return;
            }
        }
        else
        {
            _rocEma = _rocEma + _kRoc * (roc - _rocEma);
            rocEma = _rocEma;
        }
        double re = rocEma * 10;

        // PMO = EMA(re, SmoothPeriods).
        double pmo;
        if (!_pmoSeeded)
        {
            _reSeedSum += re; _reSeedCount++;
            if (_reSeedCount >= SmoothPeriods)
            {
                _pmoValue = _reSeedSum / SmoothPeriods;
                _pmoSeeded = true;
                pmo = _pmoValue;
            }
            else
            {
                _pmo.Append(double.NaN);
                _signal.Append(double.NaN);
                _barIdx++;
                return;
            }
        }
        else
        {
            _pmoValue = _pmoValue + _kPmo * (re - _pmoValue);
            pmo = _pmoValue;
        }
        _pmo.Append(pmo);

        // Signal = EMA(PMO, SignalPeriods).
        double signal;
        if (!_signalSeeded)
        {
            _pmoSeedSum += pmo; _pmoSeedCount++;
            if (_pmoSeedCount >= SignalPeriods)
            {
                _signalValue = _pmoSeedSum / SignalPeriods;
                _signalSeeded = true;
                signal = _signalValue;
            }
            else
            {
                _signal.Append(double.NaN);
                _barIdx++;
                return;
            }
        }
        else
        {
            _signalValue = _signalValue + _kSig * (pmo - _signalValue);
            signal = _signalValue;
        }
        _signal.Append(signal);
        _barIdx++;
    }
}
