using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Aroon Up/Down.
/// Up   = 100·(Period − barsSinceHigh) / Period.
/// Down = 100·(Period − barsSinceLow)  / Period.
/// Defaults Period=14. Up DarkCyan, Down SlateBlue. Lines 30 / 70 DarkGray.
/// Sub-panel.
/// </summary>
[IndicatorDescription("Aroon Up/Down. Two 0-100 oscillators measuring how recently the highest high (Up) and lowest low (Down) occurred within the lookback window. Up near 100 with Down near 0 signals a strong uptrend, and vice versa.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Aroon : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _up   = new();
    private readonly ChunkedSeries<double> _down = new();
    private IndicatorOutputSeries? _upW, _downW;
    private int _barIdx;

    public Aroon(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Up",   _up,   PlotStyle.Line, PlotColors.DarkCyan,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "30/70") });
        AddPlot(new Plot("Down", _down, PlotStyle.Line, new ChartColor(0x6A, 0x5A, 0xCD), 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Down, "30/70") });   // SlateBlue
        AddLine("Lower", 30, PlotColors.DarkGray);
        AddLine("Upper", 70, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Up   => _upW   ??= new IndicatorOutputSeries(_up,   Bars);
    public ISeries<double> Down => _downW ??= new IndicatorOutputSeries(_down, Bars);

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
        if (_barIdx == 0) { _up.Append(0); _down.Append(0); _barIdx++; return; }
        int back = Math.Min(Period, _barIdx);
        // Find barsAgo of the highest High and lowest Low in [0..back].
        double maxH = double.MinValue, minL = double.MaxValue;
        int maxAgo = 0, minAgo = 0;
        for (int i = 0; i <= back; i++)
        {
            var b = src[barsAgoOfCurrent + i];
            if (b.High >= maxH) { maxH = b.High; maxAgo = i; }
            if (b.Low  <= minL) { minL = b.Low;  minAgo = i; }
        }
        _up.Append(100.0 * (back - maxAgo) / back);
        _down.Append(100.0 * (back - minAgo) / back);
        _barIdx++;
    }
}
