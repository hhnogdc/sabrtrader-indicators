using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// MACD (Moving Average Convergence Divergence)
/// the original's @MACD built-in. Three plots:
///   <list type="bullet">
///     <item><b>MACD</b> line (DarkCyan).</item>
///     <item><b>Avg</b> (signal) line (Crimson) — EMA of MACD.</item>
///     <item><b>Diff</b> histogram (DarkGray — the original @MACD default;
///       visible on both light and dark chart backgrounds, unlike
///       a literal Black which disappears against a dark theme).</item>
///   </list>
/// One reference line at zero (DarkGray).
///
/// the original formula (no SMA seed): bar 0 → fastEma = slowEma = Input[0],
/// Value = Avg = Diff = 0. Subsequent bars apply the recursive EMA
/// formula to fast, slow, then signal directly.
///
/// Defaults: Fast=12, Slow=26, Smooth=9. Range each: [1, int.MaxValue].
/// Note: The original does NOT enforce Fast &lt; Slow; we follow.
/// </summary>
[IndicatorDescription("MACD: difference between a fast and slow EMA of price (default 12/26), plus a signal-line EMA (9) and a histogram of MACD minus signal. Classic momentum and trend-change indicator.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Macd : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _macd      = new();
    private readonly ChunkedSeries<double> _signal    = new();
    private readonly ChunkedSeries<double> _histogram = new();
    private IndicatorOutputSeries? _macdWrap;
    private IndicatorOutputSeries? _signalWrap;
    private IndicatorOutputSeries? _histogramWrap;

    // Inlined recursive EMAs — the original computes all three (fast, slow,
    // signal) in OnBarUpdate without intermediate Series allocations
    // for the EMA states. We do the same; faster + matches the original
    // bar-0 behaviour exactly (no NaN warmup).
    private double _fastEma;
    private double _slowEma;
    private double _signalEma;
    private bool _hasValue;
    private double _alphaFast, _oneMinusAlphaFast;
    private double _alphaSlow, _oneMinusAlphaSlow;
    private double _alphaSig,  _oneMinusAlphaSig;

    public Macd(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot("MACD",   _macd,      PlotStyle.Line,      PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Avg",    _signal,    PlotStyle.Line,      PlotColors.Crimson,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Diff",   _histogram, PlotStyle.Histogram, PlotColors.DarkGray, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast",   Description = "Period of the fast EMA.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow",   Description = "Period of the slow EMA.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Smooth", Description = "Signal EMA period over the MACD line.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 9;

    /// <summary>MACD line — <c>EMA(Fast) − EMA(Slow)</c> of the input.</summary>
    public ISeries<double> Default => _macdWrap   ??= new IndicatorOutputSeries(_macd,      Bars);

    /// <summary>Signal line — <c>EMA(Smooth)</c> of the MACD line. The original alias: <c>Avg</c>.</summary>
    public ISeries<double> Avg     => _signalWrap ??= new IndicatorOutputSeries(_signal,    Bars);

    /// <summary>Histogram = MACD − Signal. The original alias: <c>Diff</c>.</summary>
    public ISeries<double> Diff    => _histogramWrap ??= new IndicatorOutputSeries(_histogram, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast   < 1) throw new ArgumentOutOfRangeException(nameof(Fast),   "Fast must be at least 1.");
        if (Slow   < 1) throw new ArgumentOutOfRangeException(nameof(Slow),   "Slow must be at least 1.");
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth), "Smooth must be at least 1.");

        _alphaFast = 2.0 / (1.0 + Fast);
        _oneMinusAlphaFast = 1.0 - _alphaFast;
        _alphaSlow = 2.0 / (1.0 + Slow);
        _oneMinusAlphaSlow = 1.0 - _alphaSlow;
        _alphaSig  = 2.0 / (1.0 + Smooth);
        _oneMinusAlphaSig  = 1.0 - _alphaSig;
        _fastEma = 0;
        _slowEma = 0;
        _signalEma = 0;
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
            // The original bar 0: all EMAs seed with the first input;
            // MACD line + signal + histogram all start at 0.
            _fastEma = input;
            _slowEma = input;
            _signalEma = 0;
            _hasValue = true;
            _macd.Append(0);
            _signal.Append(0);
            _histogram.Append(0);
            return;
        }
        _fastEma = _alphaFast * input + _oneMinusAlphaFast * _fastEma;
        _slowEma = _alphaSlow * input + _oneMinusAlphaSlow * _slowEma;
        double macd = _fastEma - _slowEma;
        _signalEma = _alphaSig * macd + _oneMinusAlphaSig * _signalEma;
        _macd.Append(macd);
        _signal.Append(_signalEma);
        _histogram.Append(macd - _signalEma);
    }
}
