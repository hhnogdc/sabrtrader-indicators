using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Chaikin Oscillator — 1-on-1 port of NT @ChaikinOscillator.
/// MFV = Volume·(2·Close−H−L)/(H−L). Cumulative = ΣMFV (ADL).
/// Value = EMA(Cumulative, Fast) − EMA(Cumulative, Slow).
/// Defaults Fast=3, Slow=10, Goldenrod, sub-panel.
/// </summary>
[IndicatorDescription("Chaikin Oscillator. MACD-style spread between a fast (3) and slow (10) EMA of the Accumulation/Distribution Line, highlighting momentum shifts in volume-confirmed buying or selling pressure.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ChaikinOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _cumul = new();
    private IndicatorOutputSeries? _cumW, _wrap;
    private Ema _emaFast = null!, _emaSlow = null!;
    private double _running;

    public ChaikinOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Chaikin Oscillator", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Fast", Description = "Fast EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 3;

    [IndicatorParameter(DisplayName = "Slow", Description = "Slow EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1) throw new ArgumentOutOfRangeException(nameof(Fast));
        if (Slow < 1) throw new ArgumentOutOfRangeException(nameof(Slow));
        _cumW = new IndicatorOutputSeries(_cumul, Bars);
        // parent=null: read parent-filled _cumW. Manual dispatch avoids double-processing.
        _emaFast = new Ema(parent: null, input: _cumW) { Period = Fast };
        _emaSlow = new Ema(parent: null, input: _cumW) { Period = Slow };
        _emaFast.OnInit(ctx);
        _emaSlow.OnInit(ctx);
        _running = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) PushMfv(bars[bars.Count - 1 - i]);
        _emaFast.OnDataLoaded(ctx);
        _emaSlow.OnDataLoaded(ctx);
        int n = _emaFast.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            _output.Append(_emaFast.Output[b] - _emaSlow.Output[b]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        PushMfv(Bars[0]);
        _emaFast.OnBarUpdate(ctx);
        _emaSlow.OnBarUpdate(ctx);
        _output.Append(_emaFast.Output[0] - _emaSlow.Output[0]);
    }

    private void PushMfv(Bar bar)
    {
        double hl = bar.High - bar.Low;
        double mfv = hl == 0 ? 0 : bar.Volume * (2 * bar.Close - bar.High - bar.Low) / hl;
        _running += mfv;
        _cumul.Append(_running);
    }
}
