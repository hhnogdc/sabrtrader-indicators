using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Percentage Volume Oscillator (PVO) — derived from Skender.Stock.Indicators (Apache-2.0).
///   PVO = 100·(EMA(V, Fast) − EMA(V, Slow)) / EMA(V, Slow)
///   Signal = EMA(PVO, SignalPeriods)
///   Histogram = PVO − Signal
/// EMAs seeded with SMA at bar (Period − 1). Defaults Fast=12, Slow=26, SignalPeriods=9.
/// DodgerBlue PVO, Crimson Signal, DarkCyan Histogram, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Percentage Volume Oscillator: MACD-style oscillator applied to Volume instead of price. Plots PVO, signal-line EMA and PVO-minus-signal histogram to flag volume momentum shifts.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Pvo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _pvo       = new();
    private readonly ChunkedSeries<double> _signal    = new();
    private readonly ChunkedSeries<double> _histogram = new();
    private IndicatorOutputSeries? _pW, _sW, _hW;

    private double _emaFast, _emaSlow, _emaSig;
    private bool _seedFast, _seedSlow, _seedSig;
    private double _sumFast, _sumSlow, _sumSig;
    private int _countFast, _countSlow, _countSig;
    private double _kFast, _kSlow, _kSig;

    public Pvo(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("PVO",       _pvo,       PlotStyle.Line,      PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal",    _signal,    PlotStyle.Line,      PlotColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Histogram", _histogram, PlotStyle.Histogram, PlotColors.DarkCyan,   2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast",          MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast          { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow",          MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow          { get; set; } = 26;

    [IndicatorParameter(DisplayName = "SignalPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriods { get; set; } = 9;

    public ISeries<double> Output    => _pW ??= new IndicatorOutputSeries(_pvo,       Bars);
    public ISeries<double> Signal    => _sW ??= new IndicatorOutputSeries(_signal,    Bars);
    public ISeries<double> Histogram => _hW ??= new IndicatorOutputSeries(_histogram, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || SignalPeriods < 1 || Fast >= Slow) throw new ArgumentOutOfRangeException();
        _kFast = 2d / (Fast + 1);
        _kSlow = 2d / (Slow + 1);
        _kSig  = 2d / (SignalPeriods + 1);
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
        double v = bar.Volume;

        double emaFast = StepEma(v, ref _emaFast, ref _seedFast, ref _sumFast, ref _countFast, Fast, _kFast);
        double emaSlow = StepEma(v, ref _emaSlow, ref _seedSlow, ref _sumSlow, ref _countSlow, Slow, _kSlow);

        if (!_seedFast || !_seedSlow)
        {
            _pvo.Append(double.NaN);
            _signal.Append(double.NaN);
            _histogram.Append(double.NaN);
            return;
        }

        double pvo = emaSlow != 0 ? 100 * (emaFast - emaSlow) / emaSlow : double.NaN;
        _pvo.Append(pvo);

        if (double.IsNaN(pvo)) { _signal.Append(double.NaN); _histogram.Append(double.NaN); return; }

        double sig = StepEma(pvo, ref _emaSig, ref _seedSig, ref _sumSig, ref _countSig, SignalPeriods, _kSig);
        if (!_seedSig) { _signal.Append(double.NaN); _histogram.Append(double.NaN); return; }

        _signal.Append(sig);
        _histogram.Append(pvo - sig);
    }

    /// <summary>EMA stepper that seeds with SMA at bar (period − 1).</summary>
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
