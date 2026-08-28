using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Percentage Price Oscillator.
/// Default = 100·(EMA(Fast)−EMA(Slow))/EMA(Slow). Smoothed = EMA(Default, Smooth).
/// Defaults Fast=12, Slow=26, Smooth=9. Default DimGray, Smoothed Crimson,
/// zero line, sub-panel.
/// </summary>
[IndicatorDescription("Percentage Price Oscillator: 100 * (EMA(Fast) - EMA(Slow)) / EMA(Slow), plus a signal-line EMA. Measures momentum as a percentage so values are comparable across instruments.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Ppo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _default = new();
    private readonly ChunkedSeries<double> _smoothed = new();
    private IndicatorOutputSeries? _defW, _smW, _defAsBar;
    private Ema _emaFast = null!, _emaSlow = null!, _emaSmooth = null!;

    public Ppo(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Default",  _default,  PlotStyle.Line, PlotColors.DimGray, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Smoothed", _smoothed, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast",   Description = "Fast EMA period.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow",   Description = "Slow EMA period.",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Smooth", Description = "Smoothing EMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 9;

    public ISeries<double> Default  => _defW ??= new IndicatorOutputSeries(_default,  Bars);
    public ISeries<double> Smoothed => _smW  ??= new IndicatorOutputSeries(_smoothed, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1)   throw new ArgumentOutOfRangeException(nameof(Fast));
        if (Slow < 1)   throw new ArgumentOutOfRangeException(nameof(Slow));
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        _emaFast = new Ema(this, input: Input) { Period = Fast };
        _emaSlow = new Ema(this, input: Input) { Period = Slow };
        _defAsBar = new IndicatorOutputSeries(_default, Bars);
        // parent=null: parent-filled _defAsBar; avoid auto-dispatch double-processing.
        _emaSmooth = new Ema(parent: null, input: _defAsBar) { Period = Smooth };
        _emaSmooth.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _emaFast.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double slow = _emaSlow.Output[b];
            _default.Append(slow == 0 ? 0 : 100 * (_emaFast.Output[b] - slow) / slow);
        }
        _emaSmooth.OnDataLoaded(ctx);
        int m = _emaSmooth.Output.Count;
        for (int i = 0; i < m; i++) _smoothed.Append(_emaSmooth.Output[m - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        if (_emaFast.Output.Count == 0) return;
        double slow = _emaSlow.Output[0];
        _default.Append(slow == 0 ? 0 : 100 * (_emaFast.Output[0] - slow) / slow);
        _emaSmooth.OnBarUpdate(ctx);
        _smoothed.Append(_emaSmooth.Output[0]);
    }
}
