using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Bollinger Bands.
/// Three plots (Upper / Middle / Lower) all Goldenrod, line width 1.
/// Middle = SMA(Period). Upper/Lower = Middle ± NumStdDev · σ.
/// σ is the population standard deviation over the same window
/// (the original's @StdDev). Defaults: Period=14, NumStdDev=2. Range:
/// Period [1, int.MaxValue], NumStdDev [0, int.MaxValue].
/// </summary>
[IndicatorDescription("Bollinger Bands. SMA of price flanked by an upper and lower band at NumStdDev standard deviations: bands expand in volatile regimes and contract in quiet ones, with tags often used as mean-reversion or breakout triggers.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class BollingerBands : IndicatorBase
{
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _middle = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _upperWrap;
    private IndicatorOutputSeries? _middleWrap;
    private IndicatorOutputSeries? _lowerWrap;

    private RollingStdDev _window = null!;

    public BollingerBands(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Middle", _middle, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "NumStdDev",
        Description = "Standard deviation multiplier for the upper/lower bands.",
        MinValue = 0, MaxValue = int.MaxValue, Step = 0.5)]
    public double NumStdDev { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Period",
        Description = "Number of inputs for the SMA + StdDev window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Upper  => _upperWrap  ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Middle => _middleWrap ??= new IndicatorOutputSeries(_middle, Bars);
    public ISeries<double> Lower  => _lowerWrap  ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (NumStdDev < 0)
            throw new ArgumentOutOfRangeException(nameof(NumStdDev), "NumStdDev must be non-negative.");
        _window = new RollingStdDev(Period);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++)
        {
            int barsAgo = src.Count - 1 - absIdx;
            AppendForClose(src[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        AppendForClose(src[0]);
    }

    private void AppendForClose(double close)
    {
        _window.Push(close);
        // The original emits values from bar 0 (uses SMA + StdDev which both
        // give running values during warmup). Until the window is
        // full our RollingStdDev returns mean/stddev over what IS
        // available — same the original semantic, no NaN.
        double mean = _window.Mean;
        double sd   = _window.StdDev;
        _middle.Append(mean);
        _upper.Append(mean + NumStdDev * sd);
        _lower.Append(mean - NumStdDev * sd);
    }
}
