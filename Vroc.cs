using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Rate-of-Change.
/// raw = 100·(vol[0] − vol[min(barIdx, Period-1)]) / vol[min(barIdx, Period-1)]
/// = 100·vol[0]/back − 100. Result = SMA(raw, Smooth).
/// Defaults Period=14, Smooth=3, Goldenrod, zero reference line DarkGray,
/// sub-panel.
/// </summary>
[IndicatorDescription("Volume Rate-of-Change. SMA-smoothed percentage change of bar volume over Period bars, oscillating around zero to highlight volume expansion vs contraction.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Vroc : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _raw = new();
    private IndicatorOutputSeries? _rawWrap, _wrap;
    private Sma _sma = null!;
    private int _barIdx;

    public Vroc(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("VROC", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Lookback for volume comparison.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth", Description = "SMA smoothing period over the raw %.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        _rawWrap = new IndicatorOutputSeries(_raw, Bars);
        // parent=null: parent-filled _rawWrap; avoid auto-dispatch double-processing.
        _sma = new Sma(parent: null, input: _rawWrap) { Period = Smooth };
        _sma.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        // Compute raw first for each historical bar; SMA child gets re-driven
        // via OnDataLoaded after we've populated raw.
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            var bar = bars[barsAgo];
            int back = Math.Min(_barIdx, Period - 1);
            double bv = bars[barsAgo + back].Volume;
            double v = bar.Volume;
            double raw = 100.0 * v / (bv == 0 ? 1 : bv) - 100;
            _raw.Append(raw);
            _barIdx++;
        }
        _sma.OnDataLoaded(ctx);
        var s = _sma.Output;
        for (int i = 0; i < s.Count; i++) _output.Append(s[s.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        int back = Math.Min(_barIdx, Period - 1);
        double bv = bars[back].Volume;
        double raw = 100.0 * bars[0].Volume / (bv == 0 ? 1 : bv) - 100;
        _raw.Append(raw);
        _sma.OnBarUpdate(ctx);
        _output.Append(_sma.Output[0]);
        _barIdx++;
    }
}
