using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Klinger Volume Oscillator (KVO) — derived from Skender.Stock.Indicators (Apache-2.0).
///   hlc          = High + Low + Close
///   dm           = High − Low                          (daily measurement)
///   t            = sign(hlc[i] − hlc[i−1])              (trend direction)
///   cm           = t == prev_t ? cm[i−1] + dm[i] : dm[i−1] + dm[i]
///   vf           = (dm == cm or vol == 0) ? 0
///                : dm == 0 ? vol·2·t·100
///                : cm != 0 ? vol · |2·(dm/cm − 1)| · t · 100
///                : prev_vf
///   Oscillator   = EMA(vf, Fast) − EMA(vf, Slow)
///   Signal       = EMA(Oscillator, SignalPeriods)
/// EMAs seeded with SMA. Defaults Fast=34, Slow=55, SignalPeriods=13.
/// DodgerBlue oscillator, Crimson signal, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Klinger Volume Oscillator: difference between fast and slow EMAs of a signed volume force derived from trend direction and daily range, plus a signal-line EMA. Used to spot volume divergence.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Kvo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _osc    = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _oW, _sW;

    private double _prevHlc;
    private bool _hasPrevHlc;
    private double _prevDm;
    private double _prevCm;
    private bool _hasPrevDm;
    private double _prevT;
    private bool _hasPrevT;
    private double _prevVf;

    private double _emaFast, _emaSlow, _emaSig;
    private bool _seedFast, _seedSlow, _seedSig;
    private double _sumFast, _sumSlow, _sumSig;
    private int _countFast, _countSlow, _countSig;
    private double _kFast, _kSlow, _kSig;

    public Kvo(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("KVO",    _osc,    PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, PlotColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast",          MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast          { get; set; } = 34;

    [IndicatorParameter(DisplayName = "Slow",          MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow          { get; set; } = 55;

    [IndicatorParameter(DisplayName = "SignalPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriods { get; set; } = 13;

    public ISeries<double> Output => _oW ??= new IndicatorOutputSeries(_osc,    Bars);
    public ISeries<double> Signal => _sW ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || SignalPeriods < 1 || Fast >= Slow) throw new ArgumentOutOfRangeException();
        _kFast = 2d / (Fast + 1);
        _kSlow = 2d / (Slow + 1);
        _kSig  = 2d / (SignalPeriods + 1);
        _hasPrevHlc = _hasPrevDm = _hasPrevT = false;
        _prevVf = 0;
        _seedFast = _seedSlow = _seedSig = false;
        _sumFast = _sumSlow = _sumSig = 0;
        _countFast = _countSlow = _countSig = 0;
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
        double hlc = bar.High + bar.Low + bar.Close;
        double dm = bar.High - bar.Low;
        double vol = bar.Volume;

        if (!_hasPrevHlc)
        {
            _prevHlc = hlc; _prevDm = dm; _hasPrevHlc = true;
            _osc.Append(double.NaN); _signal.Append(double.NaN);
            return;
        }
        double t = hlc > _prevHlc ? 1 : -1;
        if (!_hasPrevT)
        {
            _prevT = t; _hasPrevT = true; _hasPrevDm = true;
            _prevHlc = hlc; _prevDm = dm; _prevCm = 0;
            _osc.Append(double.NaN); _signal.Append(double.NaN);
            return;
        }
        double cm = t == _prevT ? _prevCm + dm : _prevDm + dm;
        double vf;
        if (dm == cm || vol == 0) vf = 0;
        else if (dm == 0) vf = vol * 2 * t * 100;
        else if (cm != 0) vf = vol * Math.Abs(2 * (dm / cm - 1)) * t * 100;
        else vf = _prevVf;

        // Step fast/slow EMA on vf.
        double emaFast = StepEma(vf, ref _emaFast, ref _seedFast, ref _sumFast, ref _countFast, Fast, _kFast);
        double emaSlow = StepEma(vf, ref _emaSlow, ref _seedSlow, ref _sumSlow, ref _countSlow, Slow, _kSlow);

        double kvo = double.NaN;
        double sig = double.NaN;
        if (_seedFast && _seedSlow)
        {
            kvo = emaFast - emaSlow;
            sig = StepEma(kvo, ref _emaSig, ref _seedSig, ref _sumSig, ref _countSig, SignalPeriods, _kSig);
            if (!_seedSig) sig = double.NaN;
        }
        _osc.Append(kvo);
        _signal.Append(sig);

        _prevHlc = hlc; _prevDm = dm; _prevCm = cm; _prevT = t; _prevVf = vf;
    }

    private static double StepEma(double v, ref double state, ref bool seeded,
                                  ref double sum, ref int count, int period, double k)
    {
        if (!seeded)
        {
            sum += v; count++;
            if (count < period) return double.NaN;
            state = sum / period;
            seeded = true;
            return state;
        }
        state = state + k * (v - state);
        return state;
    }
}
