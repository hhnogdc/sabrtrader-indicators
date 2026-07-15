using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Key Reversal Down — 1-on-1 port of NT @KeyReversalDown.
/// Mirror of <see cref="KeyReversalUp"/>: highest High over the prior
/// <see cref="Period"/> bars AND Close below the previous Close.
/// Default Period=1, Crimson 2px Histogram, sub-panel.
/// </summary>
[IndicatorDescription("Key Reversal Down: prints 1 when the bar's High is the highest of the prior N bars AND its Close is below the previous Close, flagging a potential bearish reversal.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class KeyReversalDown : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public KeyReversalDown(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Key Reversal Down", _output, PlotStyle.Histogram, NTColors.Crimson, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 1;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Eval(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Eval(Bars, 0);
    }

    private void Eval(IBarSeries src, int barsAgoOfCurrent)
    {
        if (_barIdx <= Period) { _output.Append(0); _barIdx++; return; }
        var current = src[barsAgoOfCurrent];
        bool highestHigh = true;
        for (int i = 1; i <= Period; i++)
        {
            if (current.High <= src[barsAgoOfCurrent + i].High) { highestHigh = false; break; }
        }
        bool closeDown = current.Close < src[barsAgoOfCurrent + 1].Close;
        _output.Append(highestHigh && closeDown ? 1 : 0);
        _barIdx++;
    }
}
