using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Rate of Change with Bands (RocWb) — 1-on-1 port of Skender RocWb.
///   ROC = 100·(value[0] / value[Period] − 1)        (Skender uses (val/back-1)·100)
///   RocEma = EMA(ROC, EmaPeriods), seeded with SMA at bar (Period + EmaPeriods − 1)
///   RocDev = sqrt(Σ ROC² over StdDevPeriods / StdDevPeriods)
///   Upper / Lower = ±RocDev
/// Defaults Period=20, EmaPeriods=5, StdDevPeriods=5.
/// DodgerBlue ROC, Goldenrod RocEma, DarkGray Upper/Lower, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Rate of Change with Bands: ROC over Period bars plus an EMA signal line and symmetric +/- RMS-of-ROC bands, so extreme momentum moves can be spotted against the indicator's own volatility.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class RocWb : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _roc      = new();
    private readonly ChunkedSeries<double> _rocEma   = new();
    private readonly ChunkedSeries<double> _upper    = new();
    private readonly ChunkedSeries<double> _lower    = new();
    private IndicatorOutputSeries? _rW, _eW, _uW, _lW;

    private double _emaState;
    private bool _emaSeeded;
    private double _emaSeedSum;
    private int _emaSeedCount;
    private double _kEma;

    private RollingSum _rocSqSum = null!;
    private int _barIdx;

    public RocWb(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ROC",     _roc,    PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("RocEma",  _rocEma, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Upper",   _upper,  PlotStyle.Line, NTColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",   _lower,  PlotStyle.Line, NTColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period",        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period        { get; set; } = 20;

    [IndicatorParameter(DisplayName = "EmaPeriods",    MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int EmaPeriods    { get; set; } = 5;

    [IndicatorParameter(DisplayName = "StdDevPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int StdDevPeriods { get; set; } = 5;

    public ISeries<double> Roc      => _rW ??= new IndicatorOutputSeries(_roc,    Bars);
    public ISeries<double> RocEma   => _eW ??= new IndicatorOutputSeries(_rocEma, Bars);
    public ISeries<double> Upper    => _uW ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower    => _lW ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || EmaPeriods < 1 || StdDevPeriods < 1) throw new ArgumentOutOfRangeException();
        _kEma = 2d / (EmaPeriods + 1);
        _emaSeeded = false; _emaSeedSum = 0; _emaSeedCount = 0;
        _rocSqSum = new RollingSum(StdDevPeriods);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx < Period)
        {
            _roc.Append(double.NaN);
            _rocEma.Append(double.NaN);
            _upper.Append(double.NaN);
            _lower.Append(double.NaN);
            _barIdx++;
            return;
        }
        double v = src[barsAgoOfCurrent];
        double back = src[barsAgoOfCurrent + Period];
        double roc = back == 0 ? double.NaN : 100 * (v / back - 1);
        _roc.Append(roc);

        // EMA of ROC, SMA-seeded.
        double rocEma;
        if (!_emaSeeded)
        {
            if (!double.IsNaN(roc)) { _emaSeedSum += roc; _emaSeedCount++; }
            if (_emaSeedCount >= EmaPeriods)
            {
                _emaState = _emaSeedSum / EmaPeriods;
                _emaSeeded = true;
                rocEma = _emaState;
            }
            else rocEma = double.NaN;
        }
        else
        {
            _emaState = _emaState + _kEma * (roc - _emaState);
            rocEma = _emaState;
        }
        _rocEma.Append(rocEma);

        // ROC stddev (sqrt of mean roc²) over StdDevPeriods.
        _rocSqSum.Push(double.IsNaN(roc) ? 0 : roc * roc);
        if (_rocSqSum.Count < StdDevPeriods) { _upper.Append(double.NaN); _lower.Append(double.NaN); _barIdx++; return; }
        double dev = Math.Sqrt(_rocSqSum.Sum / StdDevPeriods);
        _upper.Append(dev);
        _lower.Append(-dev);
        _barIdx++;
    }
}
