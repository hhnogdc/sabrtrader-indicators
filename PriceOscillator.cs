using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Price Oscillator — 1-on-1 port of NT @PriceOscillator.
/// raw = EMA(Fast) − EMA(Slow); Value = EMA(raw, Smooth).
/// Defaults Fast=12, Slow=26, Smooth=9, Goldenrod, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Price Oscillator: EMA(Smooth) applied to the absolute spread EMA(Fast) - EMA(Slow). Like MACD but smoothed by an extra EMA rather than presented with a histogram.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class PriceOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _raw = new();
    private IndicatorOutputSeries? _rawW, _wrap;
    private Ema _emaFast = null!, _emaSlow = null!, _emaSmooth = null!;

    public PriceOscillator(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddLine("Zero line", 0, NTColors.DarkGray);
        AddPlot(new Plot("Price Oscillator", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Fast",   Description = "Fast EMA period.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow",   Description = "Slow EMA period.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Smooth", Description = "Smoothing EMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 9;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || Smooth < 1) throw new ArgumentOutOfRangeException();
        _emaFast = new Ema(this, input: Input) { Period = Fast };
        _emaSlow = new Ema(this, input: Input) { Period = Slow };
        _rawW = new IndicatorOutputSeries(_raw, Bars);
        // parent=null: parent-filled _rawW; avoid auto-dispatch double-processing.
        _emaSmooth = new Ema(parent: null, input: _rawW) { Period = Smooth };
        _emaSmooth.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _emaFast.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            _raw.Append(_emaFast.Output[b] - _emaSlow.Output[b]);
        }
        _emaSmooth.OnDataLoaded(ctx);
        int m = _emaSmooth.Output.Count;
        for (int i = 0; i < m; i++) _output.Append(_emaSmooth.Output[m - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        if (_emaFast.Output.Count == 0) return;
        _raw.Append(_emaFast.Output[0] - _emaSlow.Output[0]);
        _emaSmooth.OnBarUpdate(ctx);
        _output.Append(_emaSmooth.Output[0]);
    }
}
