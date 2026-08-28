using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// AMA — Adaptive Moving Average.
/// (Technical/AMA.cs). Kaufman-style efficiency-ratio adaptation, but
/// distinct from <see cref="Kama"/> in its defaults (Period=15,
/// Fast=3, Slow=20 vs KAMA's 10/2/30), its warm-up (no value until bar
/// <c>Period</c>) and its seeding branch.
///
/// Per bar (<c>bar ≥ Period</c>):
/// <code>
///   dir = value − value[bar − Period]
///   vol = Σ |value[i] − value[i−1]| over the last Period bars   (≥ 1e-6)
///   c   = (|dir/vol|·(2/(Fast+1) − 2/(Slow+1)) + 2/(Slow+1))²
///   AMA = prevAMA ≠ 0 ? prevAMA + c·(value − prevAMA)
///                     : value[bar−1] + c·(value − value[bar−1])
/// </code>
/// Bars <c>0 … Period−1</c> emit no value (NaN — the original draws nothing
/// during warm-up). DodgerBlue overlay.
/// </summary>
[IndicatorDescription("Adaptive Moving Average (the original): an efficiency-ratio adaptive average. Its smoothing constant scales between a fast and slow EMA by |direction|/volatility, tracking trends closely and flattening in chop. Distinct defaults/warm-up from KAMA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Ama : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _diffSum = null!;
    private double _fastCf, _slowCf;
    private double _value;       // last rendered AMA value (0 until first emit)
    private int _barIdx;

    public Ama(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("AMA", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Efficiency-ratio look-back.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 15;

    [IndicatorParameter(DisplayName = "Fast", Description = "Fast EMA constant — upper bound of adaptation.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public double FastConstant { get; set; } = 3;

    [IndicatorParameter(DisplayName = "Slow", Description = "Slow EMA constant — lower bound of adaptation.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public double SlowConstant { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (FastConstant < 1) throw new ArgumentOutOfRangeException(nameof(FastConstant), "Fast must be at least 1.");
        if (SlowConstant < 1) throw new ArgumentOutOfRangeException(nameof(SlowConstant), "Slow must be at least 1.");
        _diffSum = new RollingSum(Period);
        _fastCf = 2.0 / (FastConstant + 1.0);
        _slowCf = 2.0 / (SlowConstant + 1.0);
        _value = 0;
        _barIdx = 0;
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

    private void Consume(ISeries<double> src, int barsAgo)
    {
        double v = src[barsAgo];

        // The original bar 0: no diff, no rendered value.
        if (_barIdx == 0)
        {
            _output.Append(double.NaN);
            _barIdx++;
            return;
        }

        // diff[bar] = |value − value[bar-1]| (pushed for every bar ≥ 1 so
        // the rolling sum is primed by the time the window fills).
        _diffSum.Push(Math.Abs(v - src[barsAgo + 1]));

        if (_barIdx < Period)
        {
            _output.Append(double.NaN);     // the original SetPointOfEndLine — nothing drawn
            _barIdx++;
            return;
        }

        double dir = v - src[barsAgo + Period];         // value[bar - Period]
        double vol = _diffSum.Sum;
        if (vol == 0) vol = 0.000001;
        double c = Math.Abs(dir / vol) * (_fastCf - _slowCf) + _slowCf;
        c *= c;

        double prevSrc = src[barsAgo + 1];              // value[bar - 1]
        _value = _value != 0
            ? _value + c * (v - _value)
            : prevSrc + c * (v - prevSrc);

        _output.Append(_value);
        _barIdx++;
    }
}
