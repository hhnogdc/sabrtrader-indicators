using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Mass Index — 1-on-1 port of NT @MassIndex.
/// Σ over last Period bars of EMA(Range, EmaPeriod) / EMA(EMA(Range, EmaPeriod), EmaPeriod).
/// Defaults EmaPeriod=9, Period=25. DodgerBlue, sub-panel.
/// </summary>
[IndicatorDescription("Mass Index (Dorsey): sum over Period bars of EMA(H-L)/EMA(EMA(H-L)). Detects range expansion that often precedes reversals; classic reversal bulge above 27.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MassIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _range = new();
    private IndicatorOutputSeries? _wrap, _rW;
    private Ema _emaR = null!, _emaEmaR = null!;
    private int _barIdx;

    public MassIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Mass Index", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "EmaPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int EmaPeriod { get; set; } = 9;

    [IndicatorParameter(DisplayName = "Period",    MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 25;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (EmaPeriod < 1 || Period < 1) throw new ArgumentOutOfRangeException();
        _rW = new IndicatorOutputSeries(_range, Bars);
        // parent=null both: _emaR reads parent-filled _rW; _emaEmaR reads _emaR.Output
        // which we drive manually after appending to _range each bar. _emaR MUST be
        // initialised BEFORE we read _emaR.Output to chain _emaEmaR: the Output's
        // IndicatorOutputSeries ctor throws on a null SourceBars, and _emaR.Bars is
        // only resolved (from _rW) inside _emaR.OnInit — reading Output earlier threw
        // and silently aborted the whole indicator (no output at all).
        _emaR = new Ema(parent: null, input: _rW) { Period = EmaPeriod };
        _emaR.OnInit(ctx);
        _emaEmaR = new Ema(parent: null, input: _emaR.Output) { Period = EmaPeriod };
        _emaEmaR.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[bars.Count - 1 - i];
            _range.Append(b.High - b.Low);
        }
        _emaR.OnDataLoaded(ctx);
        _emaEmaR.OnDataLoaded(ctx);
        int n = _emaR.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            // Sum the ratio over the last min(barIdx+1, Period) bars.
            int back = Math.Min(_barIdx + 1, Period);
            double sum = 0;
            for (int k = 0; k < back; k++)
            {
                double denom = _emaEmaR.Output[b + k];
                if (denom != 0) sum += _emaR.Output[b + k] / denom;
            }
            _output.Append(sum);
            _barIdx++;
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var bar = Bars[0];
        _range.Append(bar.High - bar.Low);
        _emaR.OnBarUpdate(ctx);
        _emaEmaR.OnBarUpdate(ctx);
        int back = Math.Min(_barIdx + 1, Period);
        double sum = 0;
        for (int k = 0; k < back; k++)
        {
            double denom = _emaEmaR.Output[k];
            if (denom != 0) sum += _emaR.Output[k] / denom;
        }
        _output.Append(sum);
        _barIdx++;
    }
}
