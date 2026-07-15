using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Forecast Oscillator — 1-on-1 port of NT @FOSC (Chande).
/// Value = 100·(Input − TSF(Input,Period)) / Input. Input ≤ 0 skips.
/// Defaults Period=14, DarkCyan, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Forecast Oscillator (Chande): 100·(Input − TSF) / Input, where TSF is the time-series forecast (linear regression projection). Measures percent deviation from the trend forecast.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Fosc : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Tsf _tsf = null!;

    public Fosc(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("FOSC", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _tsf = new Tsf(this, input: Input) { Period = Math.Max(2, Period) };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = _tsf.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double in0 = src[b];
            _output.Append(in0 <= 0 ? 0 : 100 * (in0 - _tsf.Output[b]) / in0);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (Input.Count == 0 || _tsf.Output.Count == 0) return;
        double in0 = Input[0];
        _output.Append(in0 <= 0 ? 0 : 100 * (in0 - _tsf.Output[0]) / in0);
    }
}
