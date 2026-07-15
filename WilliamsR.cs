using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Williams %R — 1-on-1 port of NT @WilliamsR.
/// <c>Value = −100·(maxHigh − close) / (maxHigh − minLow)</c> over
/// the trailing Period bars. Flat-window guard → denom = 1.
/// Defaults Period=14, Goldenrod plot, ±25 / ±75 reference lines
/// (DarkGray). Sub-panel.
/// </summary>
[IndicatorDescription("Williams %R momentum oscillator. -100*(maxHigh - close)/(maxHigh - minLow) over Period bars. Ranges -100 to 0; readings above -20 indicate overbought, below -80 oversold.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class WilliamsR : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMinMax _maxHigh = null!;
    private RollingMinMax _minLow = null!;

    public WilliamsR(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddLine("Overbought", -25, NTColors.DarkGray);
        AddLine("Oversold",   -75, NTColors.DarkGray);
        AddPlot(new Plot("Williams %R", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -100, 0, PlotValueUnit.Percent, PlotBullishDirection.Up, "-25/-75") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxHigh = new RollingMinMax(Period, trackMax: true);
        _minLow  = new RollingMinMax(Period, trackMax: false);
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
        _maxHigh.Push(bar.High);
        _minLow.Push(bar.Low);
        double mx = _maxHigh.Value, mn = _minLow.Value;
        double denom = mx - mn == 0 ? 1 : mx - mn;
        _output.Append(-100 * (mx - bar.Close) / denom);
    }
}
