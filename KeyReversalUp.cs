using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Key Reversal Up — 1-on-1 port of NT @KeyReversalUp.
/// Value = 1 when the bar's Low is lower than the previous N lows AND
/// the Close is above the previous bar's Close; else 0.
/// Default Period=1, DarkCyan 2px Histogram, sub-panel.
/// </summary>
[IndicatorDescription("Key Reversal Up: prints 1 when the bar's Low is the lowest of the prior N bars AND its Close is above the previous Close, flagging a potential bullish reversal.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class KeyReversalUp : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public KeyReversalUp(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Key Reversal Up", _output, PlotStyle.Histogram, NTColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Number of prior bars compared.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
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
        bool lowestLow = true;
        for (int i = 1; i <= Period; i++)
        {
            if (current.Low >= src[barsAgoOfCurrent + i].Low) { lowestLow = false; break; }
        }
        bool closeUp = current.Close > src[barsAgoOfCurrent + 1].Close;
        _output.Append(lowestLow && closeUp ? 1 : 0);
        _barIdx++;
    }
}
