using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// True Range — 1-on-1 port of Skender Tr.
/// TR[i] = max(H − L, |H − prevClose|, |L − prevClose|). Bar 0 = NaN.
/// No parameters. DarkCyan Histogram, sub-panel.
/// </summary>
[IndicatorDescription("True Range. Per-bar volatility measure: max(High-Low, |High-prevClose|, |Low-prevClose|). The raw input series behind ATR and ADX.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Tr : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double? _prevClose;

    public Tr(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("TR", _output, PlotStyle.Histogram, NTColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _prevClose = null;
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
        if (_prevClose is null)
        {
            _output.Append(double.NaN);
            _prevClose = bar.Close;
            return;
        }
        double pc = _prevClose.Value;
        double tr = Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - pc), Math.Abs(bar.Low - pc)));
        _output.Append(tr);
        _prevClose = bar.Close;
    }
}
