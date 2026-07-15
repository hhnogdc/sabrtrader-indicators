using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Variable Moving Average (VIDYA) — 1-on-1 port of NT @VMA.
/// VI = |CMO(VolatilityPeriod)| / 100.
/// Value = sc·VI·Input + (1 − sc·VI)·prev where sc = 2/(Period+1).
/// Bar 0 → Input. Defaults Period=9, VolatilityPeriod=9, DodgerBlue, overlay.
/// </summary>
[IndicatorDescription("Chande's Variable Moving Average (VIDYA). An EMA whose smoothing factor is scaled by the absolute Chande Momentum Oscillator, so the average reacts faster in trending markets and slower in choppy ones.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Vma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Cmo _cmo = null!;
    private double _sc;
    private double _value;
    private bool _firstBar = true;

    public Vma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("VMA", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",          MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 9;

    [IndicatorParameter(DisplayName = "VolatilityPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int VolatilityPeriod { get; set; } = 9;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (VolatilityPeriod < 1) throw new ArgumentOutOfRangeException(nameof(VolatilityPeriod));
        _sc = 2.0 / (Period + 1);
        _cmo = new Cmo(this, input: Input) { Period = VolatilityPeriod };
        _value = 0;
        _firstBar = true;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int i = 0; i < n; i++) Consume(src[n - 1 - i], _cmo.Output[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (Input.Count == 0 || _cmo.Output.Count == 0) return;
        Consume(Input[0], _cmo.Output[0]);
    }

    private void Consume(double in0, double cmoVal)
    {
        if (_firstBar) { _value = in0; _output.Append(in0); _firstBar = false; return; }
        double vi = Math.Abs(cmoVal) / 100;
        _value = _sc * vi * in0 + (1 - _sc * vi) * _value;
        _output.Append(_value);
    }
}
