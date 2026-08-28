using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rahul Mohindar Oscillator (RMO) — a multiple-moving-average swing
/// oscillator. It builds a "rainbow" of ten successively-smoothed
/// 2-period SMAs, measures where the close sits relative to that
/// rainbow inside the recent range, then triple-smooths the result
/// into a slow trend line (RMO) and a fast/slow swing pair whose
/// crossover gives the buy/sell signal.
///
/// Math:
///   <code>
///   ma[0]      = SMA(Close, 2);  ma[i] = SMA(ma[i-1], 2)  for i = 1..9
///   rainbow    = average(ma[0..9])
///   SwingTrd1  = 100 · (Close − rainbow) / (HHV(High,10) − LLV(Low,10))
///   SwingTrd2  = EMA(SwingTrd1, SwingPeriod)
///   SwingTrd3  = EMA(SwingTrd2, SwingPeriod)
///   RMO        = EMA(SwingTrd1, RmoPeriod)
///   </code>
///
/// Defaults SwingPeriod=30, RmoPeriod=81. Plots RMO (DarkCyan) +
/// SwingTrd2 (DodgerBlue) + SwingTrd3 (Crimson), zero centerline,
/// sub-panel.
/// </summary>
[IndicatorDescription("Rahul Mohindar Oscillator: a 10-stage rainbow of smoothed SMAs measuring the close's position in the recent range, triple-smoothed into a slow RMO trend line plus a SwingTrd2/SwingTrd3 fast-slow pair whose crossover is the buy/sell trigger.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Rmo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private const int RainbowStages = 10;
    private const int RangeLookback = 10;

    private readonly ChunkedSeries<double> _rmo = new();
    private readonly ChunkedSeries<double> _swing2 = new();
    private readonly ChunkedSeries<double> _swing3 = new();
    private IndicatorOutputSeries? _rmoWrap, _s2Wrap, _s3Wrap;

    private RollingSum[] _rainbow = [];
    private RollingMinMax _hh = null!, _ll = null!;
    private double _aSwing, _aRmo;
    private double _ema2, _ema3, _emaRmo;
    private bool _hasEma;

    public Rmo(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("RMO",       _rmo,    PlotStyle.Line, PlotColors.DarkCyan,   1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("SwingTrd2", _swing2, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("SwingTrd3", _swing3, PlotStyle.Line, PlotColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "SwingPeriod", Description = "EMA period for the SwingTrd2 / SwingTrd3 pair.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SwingPeriod { get; set; } = 30;

    [IndicatorParameter(DisplayName = "RmoPeriod", Description = "EMA period for the slow RMO line.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RmoPeriod { get; set; } = 81;

    public ISeries<double> Output    => _rmoWrap ??= new IndicatorOutputSeries(_rmo, Bars);
    public ISeries<double> SwingTrd2 => _s2Wrap ??= new IndicatorOutputSeries(_swing2, Bars);
    public ISeries<double> SwingTrd3 => _s3Wrap ??= new IndicatorOutputSeries(_swing3, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (SwingPeriod < 1) throw new ArgumentOutOfRangeException(nameof(SwingPeriod));
        if (RmoPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RmoPeriod));
        _rainbow = new RollingSum[RainbowStages];
        for (int i = 0; i < RainbowStages; i++) _rainbow[i] = new RollingSum(2);
        _hh = new RollingMinMax(RangeLookback, trackMax: true);
        _ll = new RollingMinMax(RangeLookback, trackMax: false);
        _aSwing = 2.0 / (1 + SwingPeriod);
        _aRmo = 2.0 / (1 + RmoPeriod);
        _ema2 = _ema3 = _emaRmo = 0;
        _hasEma = false;
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
        // Rainbow: ten chained 2-period SMAs, each fed the previous stage's value.
        double input = bar.Close;
        double rainbowSum = 0;
        for (int i = 0; i < RainbowStages; i++)
        {
            _rainbow[i].Push(input);
            double v = _rainbow[i].Sum / Math.Min(_rainbow[i].Count, 2);
            rainbowSum += v;
            input = v;
        }
        double rainbow = rainbowSum / RainbowStages;

        _hh.Push(bar.High);
        _ll.Push(bar.Low);
        double range = _hh.Value - _ll.Value;
        double swing1 = range > 0 ? 100.0 * (bar.Close - rainbow) / range : 0;

        if (!_hasEma)
        {
            _ema2 = _ema3 = _emaRmo = swing1;
            _hasEma = true;
        }
        else
        {
            _ema2 = _aSwing * swing1 + (1 - _aSwing) * _ema2;
            _ema3 = _aSwing * _ema2 + (1 - _aSwing) * _ema3;
            _emaRmo = _aRmo * swing1 + (1 - _aRmo) * _emaRmo;
        }
        _rmo.Append(_emaRmo);
        _swing2.Append(_ema2);
        _swing3.Append(_ema3);
    }
}
