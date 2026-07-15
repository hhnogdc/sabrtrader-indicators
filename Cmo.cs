using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Chande Momentum Oscillator — 1-on-1 port of NT @CMO.
/// per bar: up = max(in[0]-in[1],0), down = max(in[1]-in[0],0).
/// Σup, Σdown over Period bars.
/// Value = 100·(sumUp − sumDown) / (sumUp + sumDown). sumUp==sumDown → 0.
/// Defaults Period=14, DodgerBlue, sub-panel.
/// </summary>
[IndicatorDescription("Chande Momentum Oscillator. Bounded -100..+100 momentum reading equal to 100 x (sum of up-moves - sum of down-moves) / (sum of up-moves + sum of down-moves) over the period. Similar to RSI but uses raw price changes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Cmo : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _sumUp = null!;
    private RollingSum _sumDown = null!;
    private double? _prev;

    public Cmo(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("CMO", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -100, 100, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window for up/down sums.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumUp = new RollingSum(Period);
        _sumDown = new RollingSum(Period);
        _prev = null;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double v)
    {
        if (_prev is null)
        {
            _prev = v;
            _sumUp.Push(0);
            _sumDown.Push(0);
            _output.Append(0);
            return;
        }
        double delta = v - _prev.Value;
        _sumUp.Push(delta > 0 ?  delta : 0);
        _sumDown.Push(delta < 0 ? -delta : 0);
        _prev = v;
        double up = _sumUp.Sum, down = _sumDown.Sum;
        double denom = up + down;
        _output.Append(denom == 0 ? 0 : 100 * (up - down) / denom);
    }
}
