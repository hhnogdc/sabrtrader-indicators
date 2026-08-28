using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Schaff Trend Cycle (STC) — derived from Skender.Stock.Indicators (Apache-2.0).
/// Pipeline:
///   1. MACD = EMA(Close, Fast) − EMA(Close, Slow)  (signal not used)
///   2. raw %K  = 100·(MACD − minMACD) / (maxMACD − minMACD) over CyclePeriods
///   3. STC     = SMA(raw %K, 3)
/// Defaults CyclePeriods=10, FastPeriods=23, SlowPeriods=50.
/// Crimson line, 25 / 75 reference lines, sub-panel. NaN until enough warmup.
/// </summary>
[IndicatorDescription("Schaff Trend Cycle: stochastic oscillator applied to a MACD line (EMA Fast - EMA Slow), smoothed by a 3-period SMA. Combines trend (MACD) and cycle (stochastic); 25/75 mark reversal zones.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Stc : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    private double _emaFast, _emaSlow;
    private bool _seedFast, _seedSlow;
    private double _sumFast, _sumSlow;
    private int _countFast, _countSlow;
    private double _kFast, _kSlow;

    private RollingMinMax _maxMacd = null!, _minMacd = null!;
    private RollingSum _kSum = null!;
    private int _kCount;
    private const int InnerSmooth = 3;   // Skender hardcoded "smoothPeriods=3"

    public Stc(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("STC", _output, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddLine("Overbought", 75, PlotColors.DarkGray);
        AddLine("Oversold",   25, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "CyclePeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int CyclePeriods { get; set; } = 10;

    [IndicatorParameter(DisplayName = "FastPeriods",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int FastPeriods  { get; set; } = 23;

    [IndicatorParameter(DisplayName = "SlowPeriods",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SlowPeriods  { get; set; } = 50;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (CyclePeriods < 1 || FastPeriods < 1 || SlowPeriods < 1 || FastPeriods >= SlowPeriods)
            throw new ArgumentOutOfRangeException();
        _kFast = 2d / (FastPeriods + 1);
        _kSlow = 2d / (SlowPeriods + 1);
        _seedFast = _seedSlow = false;
        _sumFast = _sumSlow = 0;
        _countFast = _countSlow = 0;
        _maxMacd = new RollingMinMax(CyclePeriods, trackMax: true);
        _minMacd = new RollingMinMax(CyclePeriods, trackMax: false);
        _kSum = new RollingSum(InnerSmooth);
        _kCount = 0;
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
        double emaFast = StepEma(v, ref _emaFast, ref _seedFast, ref _sumFast, ref _countFast, FastPeriods, _kFast);
        double emaSlow = StepEma(v, ref _emaSlow, ref _seedSlow, ref _sumSlow, ref _countSlow, SlowPeriods, _kSlow);
        if (!_seedFast || !_seedSlow) { _output.Append(double.NaN); return; }

        double macd = emaFast - emaSlow;
        _maxMacd.Push(macd); _minMacd.Push(macd);
        if (_maxMacd.Count < CyclePeriods) { _output.Append(double.NaN); return; }

        double mx = _maxMacd.Value, mn = _minMacd.Value;
        double rawK = mx == mn ? 0 : 100 * (macd - mn) / (mx - mn);
        _kSum.Push(rawK); _kCount++;
        if (_kCount < InnerSmooth) { _output.Append(double.NaN); return; }
        _output.Append(_kSum.Sum / InnerSmooth);
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
