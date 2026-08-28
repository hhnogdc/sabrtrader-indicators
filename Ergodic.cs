using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ergodic Oscillator (William Blau) — the double-EMA-smoothed
/// momentum ratio of the TSI family, paired with an EMA signal line.
/// Identical core to the True Strength Index but exposed with Blau's
/// signal line so the crossover (Ergodic × Signal) is a first-class
/// output.
///
/// Math:
///   <code>
///   mom      = Close − Close[1]
///   Ergodic  = 100 · DoubleEMA(mom, Slow, Fast) / DoubleEMA(|mom|, Slow, Fast)
///   Signal   = EMA(Ergodic, SignalPeriod)
///   </code>
/// where DoubleEMA(x, s, f) = EMA(EMA(x, s), f).
///
/// Defaults Slow=32, Fast=5, SignalPeriod=5 (Blau). Plots Ergodic
/// (DarkCyan) / Signal (Crimson), zero centerline, sub-panel.
/// </summary>
[IndicatorDescription("Ergodic Oscillator (Blau): the TSI's double-EMA-smoothed momentum ratio with an added EMA signal line. Ergodic-vs-signal crossovers and zero-line crossings flag momentum shifts.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Ergodic : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _erg = new();
    private readonly ChunkedSeries<double> _sig = new();
    private IndicatorOutputSeries? _ergWrap, _sigWrap;

    private double _c1, _c2, _c3, _c4, _cSig;
    private double _slowEma, _fastEma, _slowAbsEma, _fastAbsEma, _sigEma;
    private double? _prev;
    private bool _firstBar, _hasSig;

    public Ergodic(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var sem = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline");
        AddPlot(new Plot("Ergodic", _erg, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = sem });
        AddPlot(new Plot("Signal",  _sig, PlotStyle.Line, PlotColors.Crimson,  1.0) { Semantics = sem });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Slow", Description = "First (slow) EMA period of the double smoothing.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 32;

    [IndicatorParameter(DisplayName = "Fast", Description = "Second (fast) EMA period of the double smoothing.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 5;

    [IndicatorParameter(DisplayName = "SignalPeriod", Description = "EMA period of the signal line over the Ergodic.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriod { get; set; } = 5;

    public ISeries<double> Output => _ergWrap ??= new IndicatorOutputSeries(_erg, Bars);
    public ISeries<double> Signal => _sigWrap ??= new IndicatorOutputSeries(_sig, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Slow < 1 || Fast < 1 || SignalPeriod < 1) throw new ArgumentOutOfRangeException(nameof(Slow), "All periods must be at least 1.");
        _c1 = 2.0 / (1 + Slow); _c2 = 1 - _c1;
        _c3 = 2.0 / (1 + Fast); _c4 = 1 - _c3;
        _cSig = 2.0 / (1 + SignalPeriod);
        _slowEma = _fastEma = _slowAbsEma = _fastAbsEma = _sigEma = 0;
        _prev = null; _firstBar = true; _hasSig = false;
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

    private void Consume(double v)
    {
        if (_firstBar)
        {
            _firstBar = false; _prev = v;
            _erg.Append(0); _sig.Append(0); _sigEma = 0; _hasSig = true;
            return;
        }
        double mom = v - _prev!.Value;
        _prev = v;
        _slowEma = mom * _c1 + _c2 * _slowEma;
        _fastEma = _slowEma * _c3 + _c4 * _fastEma;
        _slowAbsEma = Math.Abs(mom) * _c1 + _c2 * _slowAbsEma;
        _fastAbsEma = _slowAbsEma * _c3 + _c4 * _fastAbsEma;
        double erg = _fastAbsEma == 0 ? 0 : 100 * _fastEma / _fastAbsEma;
        _erg.Append(erg);

        if (!_hasSig) { _sigEma = erg; _hasSig = true; }
        else           _sigEma = _cSig * erg + (1 - _cSig) * _sigEma;
        _sig.Append(_sigEma);
    }
}
