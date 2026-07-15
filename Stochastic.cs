using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Stochastic Oscillator (slow) — 1-on-1 port of NT's @Stochastics.
///
/// Plots: <b>D</b> (DodgerBlue) + <b>K</b> (Goldenrod). Reference
/// lines at 20 (oversold) + 80 (overbought) DarkCyan.
///
/// Math (NT slow stochastic):
///   <code>
///   raw_fastK = (close − min(Low, PeriodK)) / (max(High, PeriodK) − min(Low, PeriodK)) × 100
///   K = SMA(raw_fastK, Smooth)
///   D = SMA(K, PeriodD)
///   </code>
///
/// Flat-window singularity (max == min): raw_fastK is 50 on bar 0,
/// otherwise carries forward the previous value (NT behaviour).
///
/// Defaults: PeriodK=14, PeriodD=7, Smooth=3. Range [1, int.MaxValue].
/// </summary>
[IndicatorDescription("Slow Stochastic Oscillator: raw %K = 100 * (Close - lowest Low) / (highest High - lowest Low) over PeriodK, then K = SMA(rawK, Smooth) and D = SMA(K, PeriodD). 20/80 mark oversold/overbought.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Stochastic : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _k = new();
    private readonly ChunkedSeries<double> _d = new();
    private readonly ChunkedSeries<double> _rawK = new();   // intermediate
    private IndicatorOutputSeries? _kWrap;
    private IndicatorOutputSeries? _dWrap;

    private RollingMinMax _highWindow = null!;
    private RollingMinMax _lowWindow  = null!;
    private RollingSum    _smoothKSum = null!;
    private RollingSum    _smoothDSum = null!;
    private int _rawKCount;
    private int _kCount;
    private double _prevRawK;
    private int _barIdx;

    public Stochastic(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("D", _d, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddPlot(new Plot("K", _k, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddLine("Oversold",   20, NTColors.DarkCyan);
        AddLine("Overbought", 80, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "PeriodD", Description = "SMA period over K (gives D).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodD { get; set; } = 7;

    [IndicatorParameter(DisplayName = "PeriodK", Description = "Lookback for the high/low window of raw %K.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodK { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth", Description = "SMA period over raw %K (gives K).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    /// <summary>%K (smoothed) as a chainable bar-sourced series.</summary>
    public ISeries<double> K => _kWrap ??= new IndicatorOutputSeries(_k, Bars);

    /// <summary>%D as a chainable bar-sourced series.</summary>
    public ISeries<double> D => _dWrap ??= new IndicatorOutputSeries(_d, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (PeriodK < 1) throw new ArgumentOutOfRangeException(nameof(PeriodK), "PeriodK must be at least 1.");
        if (PeriodD < 1) throw new ArgumentOutOfRangeException(nameof(PeriodD), "PeriodD must be at least 1.");
        if (Smooth  < 1) throw new ArgumentOutOfRangeException(nameof(Smooth),  "Smooth must be at least 1.");

        _highWindow = new RollingMinMax(PeriodK, trackMax: true);
        _lowWindow  = new RollingMinMax(PeriodK, trackMax: false);
        _smoothKSum = new RollingSum(Smooth);
        _smoothDSum = new RollingSum(PeriodD);
        _rawKCount = 0;
        _kCount = 0;
        _prevRawK = 50;   // NT: bar 0 with flat window → 50
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            AppendForBar(bars[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        AppendForBar(bars[0]);
    }

    private void AppendForBar(Bars.Bar bar)
    {
        _highWindow.Push(bar.High);
        _lowWindow.Push(bar.Low);

        double hh = _highWindow.Value;
        double ll = _lowWindow.Value;
        double range = hh - ll;
        // NT: flat window → bar 0 = 50, else carry previous raw %K.
        double rawK = range > 0 ? Math.Min(100, Math.Max(0, 100.0 * (bar.Close - ll) / range))
                                : (_barIdx == 0 ? 50.0 : _prevRawK);
        _rawK.Append(rawK);
        _prevRawK = rawK;
        _barIdx++;

        // Slow K = SMA(rawK, Smooth) — running mean until window full.
        _smoothKSum.Push(rawK);
        _rawKCount++;
        int kAvgCount = Math.Min(_rawKCount, Smooth);
        double kVal = _smoothKSum.Sum / kAvgCount;
        _k.Append(kVal);

        // D = SMA(K, PeriodD).
        _smoothDSum.Push(kVal);
        _kCount++;
        int dAvgCount = Math.Min(_kCount, PeriodD);
        _d.Append(_smoothDSum.Sum / dAvgCount);
    }
}
