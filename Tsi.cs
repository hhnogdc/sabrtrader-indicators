using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// True Strength Index.
/// Double-EMA(Slow,Fast) of momentum / double-EMA(Slow,Fast) of |momentum|, ·100.
/// Defaults Fast=3, Slow=14, DarkCyan, sub-panel.
/// </summary>
[IndicatorDescription("True Strength Index (Blau). Double-EMA-smoothed price momentum divided by double-EMA-smoothed absolute momentum, scaled to +/-100. Identifies trend direction and overbought/oversold extremes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Tsi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _c1, _c2, _c3, _c4;
    private double _fastEma, _fastAbsEma, _slowEma, _slowAbsEma;
    private double? _prev;
    private bool _firstBar = true;

    public Tsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("TSI", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Fast", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 3;

    [IndicatorParameter(DisplayName = "Slow", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1) throw new ArgumentOutOfRangeException();
        _c1 = 2.0 / (1 + Slow);   _c2 = 1 - _c1;
        _c3 = 2.0 / (1 + Fast);   _c4 = 1 - _c3;
        _fastEma = _fastAbsEma = _slowEma = _slowAbsEma = 0;
        _prev = null; _firstBar = true;
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
        if (_firstBar) { _firstBar = false; _prev = v; _output.Append(0); return; }
        double mom = v - _prev!.Value;
        _slowEma    = mom * _c1 + _c2 * _slowEma;
        _fastEma    = _slowEma * _c3 + _c4 * _fastEma;
        _slowAbsEma = Math.Abs(mom) * _c1 + _c2 * _slowAbsEma;
        _fastAbsEma = _slowAbsEma * _c3 + _c4 * _fastAbsEma;
        _output.Append(_fastAbsEma == 0 ? 0 : 100 * _fastEma / _fastAbsEma);
        _prev = v;
    }
}
