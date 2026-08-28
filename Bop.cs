using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Balance of Power.
/// Per-bar BOP = (close − open) / (high − low). Flat bars → 0.
/// Output = SMA(BOP, Smooth). Default Smooth=14, DodgerBlue 2px Bar
/// plot, zero reference line DarkGray, sub-panel.
/// </summary>
[IndicatorDescription("Balance of Power. Per-bar (Close-Open)/(High-Low) smoothed with an SMA; positive readings show buyers dominating the bar range, negative readings show sellers.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Bop : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _raw = new();
    private IndicatorOutputSeries? _rawWrap, _wrap;
    private Sma _sma = null!;

    public Bop(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("BOP", _output, PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -1, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Smooth", Description = "SMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        _rawWrap = new IndicatorOutputSeries(_raw, Bars);
        // parent=null: _sma reads parent-filled _rawWrap; auto-dispatch (post-order)
        // would fire _sma BEFORE this indicator and re-fire it after, double-processing.
        _sma = new Sma(parent: null, input: _rawWrap) { Period = Smooth };
        _sma.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            double hl = bar.High - bar.Low;
            _raw.Append(hl == 0 ? 0 : (bar.Close - bar.Open) / hl);
        }
        _sma.OnDataLoaded(ctx);
        var s = _sma.Output;
        for (int i = 0; i < s.Count; i++) _output.Append(s[s.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var bar = Bars[0];
        double hl = bar.High - bar.Low;
        _raw.Append(hl == 0 ? 0 : (bar.Close - bar.Open) / hl);
        _sma.OnBarUpdate(ctx);
        _output.Append(_sma.Output[0]);
    }
}
