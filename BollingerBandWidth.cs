using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Bollinger Band Width.
/// Width = 100 · (Upper − Lower) / Middle, where Upper/Middle/Lower
/// come from BollingerBands(Period, NumStdDev). Defaults Period=14,
/// NumStdDev=2, DarkCyan, sub-panel.
/// </summary>
[IndicatorDescription("Bollinger Band Width. Normalized spread between the upper and lower Bollinger bands as a percent of the middle band: low values indicate a volatility squeeze that often precedes a breakout.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class BollingerBandWidth : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private BollingerBands _bb = null!;

    public BollingerBandWidth(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("BBW", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Percent, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bollinger window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "NumStdDev", Description = "Bollinger stddev multiplier.",
        MinValue = 0, MaxValue = int.MaxValue, Step = 0.5)]
    public double NumStdDev { get; set; } = 2.0;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _bb = new BollingerBands(this, input: Input) { Period = Period, NumStdDev = NumStdDev };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _bb.Middle.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double m = _bb.Middle[b], u = _bb.Upper[b], l = _bb.Lower[b];
            _output.Append(m == 0 ? 0 : 100 * (u - l) / m);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (_bb.Middle.Count == 0) return;
        double m = _bb.Middle[0], u = _bb.Upper[0], l = _bb.Lower[0];
        _output.Append(m == 0 ? 0 : 100 * (u - l) / m);
    }
}
