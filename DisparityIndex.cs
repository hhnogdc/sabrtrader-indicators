using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Disparity Index — 1-on-1 port of NT @DisparityIndex.
/// Value = 100·(Close − EMA(Close, Period)) / Close. Close ≤ 0 skips emit (NaN).
/// Default Period=25, DodgerBlue, zero reference line, sub-panel.
/// </summary>
[IndicatorDescription("Disparity Index: 100·(Close − EMA(Close, Period)) / Close. Percent deviation of price from its EMA, useful for spotting overextension.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DisparityIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Ema _ema = null!;

    public DisparityIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Disparity Line", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 25;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _ema = new Ema(this, input: Bars.Close) { Period = Period };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        int n = _ema.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double close = bars[b].Close;
            _output.Append(close <= 0 ? 0 : 100 * (close - _ema.Output[b]) / close);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (Bars.Count == 0 || _ema.Output.Count == 0) return;
        double close = Bars[0].Close;
        _output.Append(close <= 0 ? 0 : 100 * (close - _ema.Output[0]) / close);
    }
}
