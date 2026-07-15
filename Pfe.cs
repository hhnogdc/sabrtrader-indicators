using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Polarized Fractal Efficiency — 1-on-1 port of NT @PFE.
/// singlePFE_i = sqrt((C[i-1] − C[i])² + 1).
/// div = Σ singlePFE over Period bars.
/// pfe = sign(C[0] − C[Period]) · sqrt((C[0] − C[Period])² + Period²) / div.
/// Value = EMA(pfe, Smooth). Defaults Period=14, Smooth=10. DodgerBlue, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Polarized Fractal Efficiency: signed ratio of straight-line price travel over Period to the actual path length, EMA-smoothed. High |value| = efficient trend, near zero = chop.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Pfe : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _pfe = new();
    private IndicatorOutputSeries? _wrap, _pW;
    private Ema _ema = null!;
    private RollingSum _divSum = null!;
    private int _barIdx;

    public Pfe(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("PFE", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1 || Smooth < 1) throw new ArgumentOutOfRangeException();
        _divSum = new RollingSum(Period);
        _pW = new IndicatorOutputSeries(_pfe, Bars);
        // parent=null: child Ema reads _pW (parent-filled).
        // If we registered it as a child the runtime would auto-dispatch
        // it BEFORE Pfe.OnBarUpdate (post-order tree walk), causing it
        // to consume stale/duplicate data. We drive its lifecycle manually.
        _ema = new Ema(parent: null, input: _pW) { Period = Smooth };
        _ema.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++)
        {
            Consume(src, src.Count - 1 - i);
            _ema.OnBarUpdate(ctx);
            _output.Append(_ema.Output[0]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
        _ema.OnBarUpdate(ctx);
        _output.Append(_ema.Output[0]);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        double cur = src[barsAgoOfCurrent];
        double single = _barIdx == 0 ? 1
            : Math.Sqrt(Math.Pow(src[barsAgoOfCurrent + 1] - cur, 2) + 1);
        _divSum.Push(single);
        if (_barIdx < Period) { _pfe.Append(0); _barIdx++; return; }
        double inPer = src[barsAgoOfCurrent + Period];
        double pfe = (cur < inPer ? -1 : 1) * Math.Sqrt(Math.Pow(cur - inPer, 2) + Math.Pow(Period, 2)) / _divSum.Sum;
        _pfe.Append(pfe);
        _barIdx++;
    }
}
