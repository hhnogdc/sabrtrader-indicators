using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// N consecutive down-bars.
/// Mirror of <see cref="NBarsUp"/> with strictly lower close / open /
/// high / low. Plot Crimson 2px Histogram, sub-panel.
/// </summary>
[IndicatorDescription("N Bars Down: prints 1 when the last N bars all show strictly lower Close, with optional requirements for lower High, lower Low and bearish (Close<Open) bodies.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class NBarsDown : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public NBarsDown(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("NBarsDown", _output, PlotStyle.Histogram, PlotColors.Crimson, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "BarCount",  MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int BarCount { get; set; } = 3;

    [IndicatorParameter(DisplayName = "BarDown",   MinValue = 0, MaxValue = 1, Step = 1)]
    public bool BarDown { get; set; } = true;

    [IndicatorParameter(DisplayName = "LowerHigh", MinValue = 0, MaxValue = 1, Step = 1)]
    public bool LowerHigh { get; set; } = true;

    [IndicatorParameter(DisplayName = "LowerLow",  MinValue = 0, MaxValue = 1, Step = 1)]
    public bool LowerLow { get; set; } = true;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (BarCount < 2) throw new ArgumentOutOfRangeException(nameof(BarCount));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++) Eval(bars, bars.Count - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Eval(Bars, 0);
    }

    private void Eval(IBarSeries src, int barsAgoOfCurrent)
    {
        if (_barIdx < BarCount) { _output.Append(0); _barIdx++; return; }
        bool ok = true;
        for (int i = 0; i < BarCount; i++)
        {
            var a = src[barsAgoOfCurrent + i];
            var b = src[barsAgoOfCurrent + i + 1];
            if (!(a.Close < b.Close)) { ok = false; break; }
            if (BarDown   && !(a.Close < a.Open)) { ok = false; break; }
            if (LowerHigh && !(a.High < b.High))  { ok = false; break; }
            if (LowerLow  && !(a.Low  < b.Low))   { ok = false; break; }
        }
        _output.Append(ok ? 1 : 0);
        _barIdx++;
    }
}
