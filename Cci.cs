using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Commodity Channel Index — 1-on-1 port of NT @CCI.
/// Per-bar typical = (H+L+C)/3. SMA of typical = mean.
/// mean absolute deviation = Σ|typical[i] − sma| / N over the window.
/// Value = (typical − sma) / (0.015 · meanAbsDev). NT bar 0 = 0.
/// Singularity (mad ≈ 0) → denom = 1.
/// Defaults Period=14, Goldenrod plot, 5 reference lines (±100, ±200, 0).
/// Sub-panel.
/// </summary>
[IndicatorDescription("Commodity Channel Index (Lambert). Measures how far typical price (H+L+C)/3 is from its SMA in units of mean absolute deviation; readings above +100 or below -100 flag overbought/oversold extremes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Cci : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double[] _window = [];
    private int _count;
    private int _writeIdx;
    private double _sum;
    private int _barIdx;

    public Cci(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("CCI", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Level 2", 200, NTColors.DarkGray);
        AddLine("Level 1", 100, NTColors.DarkGray);
        AddLine("Zero line", 0, NTColors.DarkGray);
        AddLine("Level -1", -100, NTColors.DarkGray);
        AddLine("Level -2", -200, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "SMA window over typical price.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _window = new double[Period];
        _count = 0;
        _writeIdx = 0;
        _sum = 0;
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
        double typical = (bar.High + bar.Low + bar.Close) / 3.0;
        // Maintain rolling sum/window of typical prices.
        if (_count < Period)
        {
            _window[_writeIdx] = typical;
            _sum += typical;
            _count++;
        }
        else
        {
            double evicted = _window[_writeIdx];
            _window[_writeIdx] = typical;
            _sum += typical - evicted;
        }
        _writeIdx = (_writeIdx + 1) % Period;

        if (_barIdx == 0) { _output.Append(0); _barIdx++; return; }
        double mean = _sum / _count;
        double mad = 0;
        for (int i = 0; i < _count; i++) mad += Math.Abs(_window[i] - mean);
        double denom = mad == 0 ? 1 : 0.015 * (mad / _count);
        _output.Append((typical - mean) / denom);
        _barIdx++;
    }
}
