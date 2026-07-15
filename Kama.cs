using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Kaufman Adaptive Moving Average — 1-on-1 port of NT @KAMA.
/// SC = (ER · (fastCf − slowCf) + slowCf)² where ER = signal / noise.
/// signal = |C − C[Period]|. noise = Σ|C[i] − C[i+1]| over Period.
/// Bar &lt; Period → Input. noise == 0 → carries prev.
/// Defaults Fast=2, Period=10, Slow=30. DodgerBlue, overlay.
/// </summary>
[IndicatorDescription("Kaufman Adaptive Moving Average: a moving average whose smoothing constant varies with the efficiency ratio (signal/noise) so it tracks trending price closely and flattens in chop.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Kama : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _diffSum = null!;
    private double _fastCf, _slowCf;
    private double? _prev;
    private double _value;
    private int _barIdx;

    public Kama(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("KAMA", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Fast",   MinValue = 1, MaxValue = 125, Step = 1)]
    public int Fast { get; set; } = 2;

    [IndicatorParameter(DisplayName = "Period", MinValue = 5, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Slow",   MinValue = 1, MaxValue = 125, Step = 1)]
    public int Slow { get; set; } = 30;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 5) throw new ArgumentOutOfRangeException(nameof(Period));
        _diffSum = new RollingSum(Period);
        _fastCf = 2.0 / (Fast + 1);
        _slowCf = 2.0 / (Slow + 1);
        _prev = null; _value = 0; _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        double v = src[barsAgoOfCurrent];
        double diff = _prev is null ? v : Math.Abs(v - _prev.Value);
        _diffSum.Push(diff);
        if (_barIdx < Period)
        {
            _value = v;
            _output.Append(v);
            _prev = v;
            _barIdx++;
            return;
        }
        double signal = Math.Abs(v - src[barsAgoOfCurrent + Period]);
        double noise = _diffSum.Sum;
        if (noise == 0) { _output.Append(_value); _prev = v; _barIdx++; return; }
        double sc = signal / noise * (_fastCf - _slowCf) + _slowCf;
        _value += sc * sc * (v - _value);
        _output.Append(_value);
        _prev = v;
        _barIdx++;
    }
}
