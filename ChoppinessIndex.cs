using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Choppiness Index — 1-on-1 port of NT @ChoppinessIndex.
/// Value = 100·log10(ΣATR(1)/(MAX(H,P)−MIN(L,P))) / log10(Period).
/// Singularity (zero numerator/denominator) → 0.
/// Defaults Period=14, DodgerBlue plot, 38.2/62.8 reference lines DarkCyan, sub-panel.
/// </summary>
[IndicatorDescription("Choppiness Index (E.W. Dreiss). 0-100 gauge of how trending vs sideways the market is, derived from the ratio of summed true ranges to the period high-low envelope. Above ~62 = choppy, below ~38 = trending.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ChoppinessIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMinMax _maxH = null!, _minL = null!;
    private RollingSum _sumTr = null!;
    private double? _prevClose;
    private double _log10P;

    public ChoppinessIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Choppiness", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.None, "38.2 trending / 61.8 choppy") });
        AddLine("Strong", 38.2, NTColors.DarkCyan);
        AddLine("Choppy", 62.8, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback window.",
        MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxH = new RollingMinMax(Period, trackMax: true);
        _minL = new RollingMinMax(Period, trackMax: false);
        _sumTr = new RollingSum(Period);
        _prevClose = null;
        _log10P = Math.Log10(Period);
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
        double tr = _prevClose is null ? bar.High - bar.Low
                  : Math.Max(Math.Max(bar.High - bar.Low, Math.Abs(bar.High - _prevClose.Value)),
                             Math.Abs(bar.Low - _prevClose.Value));
        _maxH.Push(bar.High); _minL.Push(bar.Low); _sumTr.Push(tr);
        double range = _maxH.Value - _minL.Value;
        double sum = _sumTr.Sum;
        _output.Append(range == 0 || sum == 0 ? 0 : 100 * Math.Log10(sum / range) / _log10P);
        _prevClose = bar.Close;
    }
}
