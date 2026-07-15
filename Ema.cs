using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Exponential Moving Average — 1-on-1 port of NinjaTrader's
/// <c>@EMA</c> built-in.
///
/// NT formula (no SMA seed): bar 0 → <c>Value[0] = Input[0]</c>.
/// Subsequent bars: <c>Value[0] = α·Input[0] + (1-α)·Value[1]</c>
/// where <c>α = 2 / (Period + 1)</c>. Result: every bar has a
/// defined value from bar 0 (no NaN warmup).
///
/// Default plot: Goldenrod, line width 1, name "EMA".
/// Range: Period in [1, int.MaxValue].
/// </summary>
[IndicatorDescription("Exponential Moving Average: α·Input + (1−α)·prev, α = 2/(Period+1). NT-style seeding with the first input — every bar has a value from bar 0 (no NaN warmup).")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Ema : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputAsBarSourced;
    private double _value;
    private double _alpha;
    private double _oneMinusAlpha;
    private bool _hasValue;

    public Ema(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot(
            name: "EMA",
            values: _output,
            style: PlotStyle.Line,
            color: NTColors.Goldenrod,
            lineWidthPx: 1.0)
        { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(
        DisplayName = "Period",
        Description = "Period of the EMA — α = 2/(Period+1).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    /// <summary>Per-bar EMA value as a chainable bar-sourced series.</summary>
    public ISeries<double> Output =>
        _outputAsBarSourced ??= new IndicatorOutputSeries(_output, Bars);

    /// <summary>NT's EMA emits a value on every bar (seeded with the first input). Always true after the first bar.</summary>
    public bool IsSeeded => _hasValue;

    /// <summary>Latest EMA value. Equivalent to <c>Output[0]</c>.</summary>
    public double Value => _value;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period),
                "EMA period must be at least 1.");
        _alpha = 2.0 / (Period + 1.0);
        _oneMinusAlpha = 1.0 - _alpha;
        _value = 0;
        _hasValue = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            ConsumeAndAppend(src[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        ConsumeAndAppend(src[0]);
    }

    private void ConsumeAndAppend(double input)
    {
        if (!_hasValue)
        {
            // NT: first bar = first input (no SMA seeding).
            _value = input;
            _hasValue = true;
            _output.Append(_value);
            return;
        }
        _value = _alpha * input + _oneMinusAlpha * _value;
        _output.Append(_value);
    }
}
