using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// KDJ — the Stochastic %K/%D plus the derived J line, hugely popular
/// in Asian markets. J leads K and D: it overshoots above 100 / below 0
/// at momentum extremes, giving earlier turn signals than the bounded
/// %K/%D pair.
///
/// Math (Wilder/China convention, recursive 1/3 smoothing):
///   <code>
///   RSV = 100·(Close − LL(Low, Period)) / (HH(High, Period) − LL(Low, Period))
///   K   = (2·K[1] + RSV) / 3      // seed K[0] = 50
///   D   = (2·D[1] + K)   / 3      // seed D[0] = 50
///   J   = 3·K − 2·D
///   </code>
/// Flat-window singularity (HH == LL): RSV is 50 on bar 0, otherwise
/// carries the previous RSV.
///
/// Defaults Period=9, SmoothK=3, SmoothD=3. Plots K (Goldenrod),
/// D (DodgerBlue), J (Crimson). 20 / 80 reference lines, sub-panel.
/// </summary>
[IndicatorDescription("KDJ: the Stochastic %K and %D plus a derived J = 3K - 2D line. J overshoots the 0-100 band at extremes to lead the %K/%D crossover, a staple momentum tool in Asian markets.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Kdj : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _k = new();
    private readonly ChunkedSeries<double> _d = new();
    private readonly ChunkedSeries<double> _j = new();
    private IndicatorOutputSeries? _kWrap, _dWrap, _jWrap;

    private RollingMinMax _hh = null!;
    private RollingMinMax _ll = null!;
    private double _kVal, _dVal;
    private double _prevRsv;
    private double _alphaK, _alphaD;
    private int _barIdx;

    public Kdj(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var bounded = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "20/80");
        // J is intentionally unbounded — it overshoots the 0-100 band, which is the whole point.
        var jSem = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Percent, PlotBullishDirection.Up, "leads K/D; overshoots 0/100");
        AddPlot(new Plot("K", _k, PlotStyle.Line, PlotColors.Goldenrod,  1.0) { Semantics = bounded });
        AddPlot(new Plot("D", _d, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = bounded });
        AddPlot(new Plot("J", _j, PlotStyle.Line, PlotColors.Crimson,    1.0) { Semantics = jSem });
        AddLine("Oversold",   20, PlotColors.DarkCyan);
        AddLine("Overbought", 80, PlotColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "RSV high/low lookback.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 9;

    [IndicatorParameter(DisplayName = "SmoothK", Description = "Recursive smoothing for K (period of the 1/N average).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothK { get; set; } = 3;

    [IndicatorParameter(DisplayName = "SmoothD", Description = "Recursive smoothing for D.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothD { get; set; } = 3;

    public ISeries<double> K => _kWrap ??= new IndicatorOutputSeries(_k, Bars);
    public ISeries<double> D => _dWrap ??= new IndicatorOutputSeries(_d, Bars);
    public ISeries<double> J => _jWrap ??= new IndicatorOutputSeries(_j, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || SmoothK < 1 || SmoothD < 1)
            throw new ArgumentOutOfRangeException(nameof(Period), "All periods must be at least 1.");
        _hh = new RollingMinMax(Period, trackMax: true);
        _ll = new RollingMinMax(Period, trackMax: false);
        // Classic KDJ uses the 1/3 weighting (SmoothK=3). Generalise via α = 1/N.
        _alphaK = 1.0 / SmoothK;
        _alphaD = 1.0 / SmoothD;
        _kVal = 50;
        _dVal = 50;
        _prevRsv = 50;
        _barIdx = 0;
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
        _hh.Push(bar.High);
        _ll.Push(bar.Low);
        double hh = _hh.Value, ll = _ll.Value;
        double range = hh - ll;
        double rsv = range > 0
            ? Math.Min(100, Math.Max(0, 100.0 * (bar.Close - ll) / range))
            : (_barIdx == 0 ? 50.0 : _prevRsv);
        _prevRsv = rsv;

        // K = (1−α)·K[1] + α·RSV ; for SmoothK=3 → (2·K[1] + RSV)/3.
        _kVal = (1 - _alphaK) * _kVal + _alphaK * rsv;
        _dVal = (1 - _alphaD) * _dVal + _alphaD * _kVal;
        double j = 3 * _kVal - 2 * _dVal;

        _k.Append(_kVal);
        _d.Append(_dVal);
        _j.Append(j);
        _barIdx++;
    }
}
