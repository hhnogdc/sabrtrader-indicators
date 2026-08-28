using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Stochastic RSI.
/// Value = (RSI − minRSI) / (maxRSI − minRSI) over Period bars. Range 0..1.
/// Defaults Period=14, DarkCyan, lines 0.2 (Crimson), 0.5 (DodgerBlue),
/// 0.8 (Crimson), sub-panel.
/// </summary>
[IndicatorDescription("Stochastic RSI: stochastic oscillator applied to RSI values, (RSI - minRSI) / (maxRSI - minRSI) over Period bars. Range 0-1; 0.2/0.8 mark oversold/overbought. More sensitive than RSI alone.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class StochRsi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Rsi _rsi = null!;
    private RollingMinMax _maxR = null!, _minR = null!;

    public StochRsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("StochRSI", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0.8/0.2") });
        AddLine("Overbought", 0.8, PlotColors.Crimson);
        AddLine("Neutral",    0.5, PlotColors.DodgerBlue);
        AddLine("Oversold",   0.2, PlotColors.Crimson);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _rsi = new Rsi(this, input: Input) { Period = Period, Smooth = 1 };
        _maxR = new RollingMinMax(Period, trackMax: true);
        _minR = new RollingMinMax(Period, trackMax: false);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _rsi.Default.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double rv = _rsi.Default[b];
            if (double.IsNaN(rv)) { _output.Append(0); continue; }
            _maxR.Push(rv); _minR.Push(rv);
            double mx = _maxR.Value, mn = _minR.Value;
            double denom = mx - mn;
            _output.Append(denom > double.Epsilon && Math.Abs(rv - mn) > double.Epsilon ? (rv - mn) / denom : 0);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_rsi.Default.Count == 0) return;
        double rv = _rsi.Default[0];
        if (double.IsNaN(rv)) { _output.Append(0); return; }
        _maxR.Push(rv); _minR.Push(rv);
        double mx = _maxR.Value, mn = _minR.Value;
        double denom = mx - mn;
        _output.Append(denom > double.Epsilon && Math.Abs(rv - mn) > double.Epsilon ? (rv - mn) / denom : 0);
    }
}
