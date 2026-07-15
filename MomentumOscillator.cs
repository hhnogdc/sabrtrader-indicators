using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Momentum Oscillator — EMA-smoothed N-bar momentum with a signal
/// line, distinct from the raw single-line <see cref="Momentum"/>: the
/// smoothing tames the spikiness and the signal crossover (Momentum ×
/// Signal) plus the zero-line crossing become the tradeable events.
///
/// Math:
///   <code>
///   mom      = Close − Close[min(CurrentBar, Period)]
///   Momentum = EMA(mom, Smooth)
///   Signal   = EMA(Momentum, SignalPeriod)
///   </code>
///
/// Defaults Period=14, Smooth=3, SignalPeriod=3. Plots Momentum
/// (DarkCyan) / Signal (Goldenrod), zero centerline, sub-panel.
/// </summary>
[IndicatorDescription("Momentum Oscillator: EMA-smoothed N-bar price momentum with an EMA signal line. Smoother than raw Momentum; the Momentum/Signal crossover and zero-line crossing are the signals.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MomentumOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _mom = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _momWrap, _sigWrap;

    private double _aMom, _aSig, _momEma, _sigEma;
    private bool _hasMom, _hasSig;
    private int _barIdx;

    public MomentumOscillator(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var sem = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline");
        AddPlot(new Plot("Momentum", _mom,    PlotStyle.Line, NTColors.DarkCyan,  1.0) { Semantics = sem });
        AddPlot(new Plot("Signal",   _signal, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = sem });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bars back for the momentum comparison.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth", Description = "EMA smoothing over the raw momentum.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    [IndicatorParameter(DisplayName = "SignalPeriod", Description = "EMA period of the signal line.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriod { get; set; } = 3;

    public ISeries<double> Output => _momWrap ??= new IndicatorOutputSeries(_mom, Bars);
    public ISeries<double> Signal => _sigWrap ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || Smooth < 1 || SignalPeriod < 1) throw new ArgumentOutOfRangeException(nameof(Period), "All periods must be at least 1.");
        _aMom = 2.0 / (1 + Smooth);
        _aSig = 2.0 / (1 + SignalPeriod);
        _momEma = _sigEma = 0;
        _hasMom = _hasSig = false;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++) Consume(src, src.Count - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        double mom = _barIdx == 0 ? 0 : src[barsAgoOfCurrent] - src[barsAgoOfCurrent + Math.Min(_barIdx, Period)];
        _barIdx++;

        if (!_hasMom) { _momEma = mom; _hasMom = true; }
        else           _momEma = _aMom * mom + (1 - _aMom) * _momEma;
        _mom.Append(_momEma);

        if (!_hasSig) { _sigEma = _momEma; _hasSig = true; }
        else           _sigEma = _aSig * _momEma + (1 - _aSig) * _sigEma;
        _signal.Append(_sigEma);
    }
}
