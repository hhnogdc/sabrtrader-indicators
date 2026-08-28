using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// N consecutive up-bars.
/// Value = 1 when the last BarCount bars all satisfy:
///   close[i] &gt; close[i+1]  AND (BarUp ? close[i]&gt;open[i] : true)
///   AND (HigherHigh ? H[i]&gt;H[i+1] : true)
///   AND (HigherLow  ? L[i]&gt;L[i+1] : true).
/// Else 0. Defaults BarCount=3, BarUp=HigherHigh=HigherLow=true.
/// Plot DarkCyan 2px Histogram, sub-panel.
/// </summary>
[IndicatorDescription("N Bars Up: prints 1 when the last N bars all show strictly higher Close, with optional requirements for higher High, higher Low and bullish (Close>Open) bodies.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class NBarsUp : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public NBarsUp(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("NBarsUp", _output, PlotStyle.Histogram, PlotColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    [IndicatorParameter(DisplayName = "BarCount",   Description = "Number of consecutive up-bars to require.",
        MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int BarCount { get; set; } = 3;

    [IndicatorParameter(DisplayName = "BarUp",      Description = "Require Close > Open for every bar.",
        MinValue = 0, MaxValue = 1, Step = 1)]
    public bool BarUp { get; set; } = true;

    [IndicatorParameter(DisplayName = "HigherHigh", Description = "Require strictly rising High.",
        MinValue = 0, MaxValue = 1, Step = 1)]
    public bool HigherHigh { get; set; } = true;

    [IndicatorParameter(DisplayName = "HigherLow",  Description = "Require strictly rising Low.",
        MinValue = 0, MaxValue = 1, Step = 1)]
    public bool HigherLow { get; set; } = true;

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
            if (!(a.Close > b.Close)) { ok = false; break; }
            if (BarUp     && !(a.Close > a.Open)) { ok = false; break; }
            if (HigherHigh && !(a.High > b.High)) { ok = false; break; }
            if (HigherLow  && !(a.Low  > b.Low))  { ok = false; break; }
        }
        _output.Append(ok ? 1 : 0);
        _barIdx++;
    }
}
