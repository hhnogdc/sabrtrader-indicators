using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Accumulation/Distribution Line — 1-on-1 port of NT @ADL.
/// AD[t] = AD[t-1] + ((C-L) - (H-C))/(H-L) · Volume. Bar 0 → 0.
/// Flat bars (H==L) contribute 0 (no volume credit).
/// No parameters. DarkCyan plot, sub-panel.
/// </summary>
[IndicatorDescription("Accumulation/Distribution Line. Cumulative volume-flow gauge: each bar's close-location-within-range scales its volume and adds to a running total, so divergence between this line and price hints at hidden buying or selling pressure.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Adl : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _running;
    private bool _hasFirst;

    public Adl(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("AD", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _running = 0;
        _hasFirst = false;
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
        double h = bar.High, l = bar.Low, c = bar.Close, v = bar.Volume;
        if (!_hasFirst)
        {
            _running = 0;
            _hasFirst = true;
            _output.Append(0);
            return;
        }
        double delta = h != l ? ((c - l) - (h - c)) / (h - l) * v : 0;
        _running += delta;
        _output.Append(_running);
    }
}
