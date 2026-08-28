using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Gator Oscillator (Bill Williams) — derived from Skender.Stock.Indicators (Apache-2.0).
///   Upper = |Jaw − Teeth|
///   Lower = −|Teeth − Lips|
/// Driven by an internal <see cref="Alligator"/> with default parameters
/// (JawPeriods=13/JawOffset=8/TeethPeriods=8/TeethOffset=5/LipsPeriods=5/LipsOffset=3).
/// LimeGreen Upper, Red Lower, Histogram, sub-panel.
/// </summary>
[IndicatorDescription("Gator Oscillator (Bill Williams): Upper = |Jaw − Teeth|, Lower = −|Teeth − Lips| from the Alligator's three smoothed moving averages. Histogram widening = trend, contraction = sleeping market.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Gator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _upper = new();
    private readonly ChunkedSeries<double> _lower = new();
    private IndicatorOutputSeries? _uW, _lW;
    private Alligator _alligator = null!;

    public Gator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Upper", _upper, PlotStyle.Histogram, PlotColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower", _lower, PlotStyle.Histogram, PlotColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    public ISeries<double> Upper => _uW ??= new IndicatorOutputSeries(_upper, Bars);
    public ISeries<double> Lower => _lW ??= new IndicatorOutputSeries(_lower, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _alligator = new Alligator(this, bars: Bars);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Step(bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (Bars.Count == 0) return;
        Step(0);
    }

    private void Step(int barsAgo)
    {
        if (barsAgo >= _alligator.Jaw.Count) { _upper.Append(double.NaN); _lower.Append(double.NaN); return; }
        double jaw   = _alligator.Jaw[barsAgo];
        double teeth = _alligator.Teeth[barsAgo];
        double lips  = _alligator.Lips[barsAgo];
        if (double.IsNaN(jaw) || double.IsNaN(teeth) || double.IsNaN(lips))
        {
            _upper.Append(double.NaN); _lower.Append(double.NaN); return;
        }
        _upper.Append( Math.Abs(jaw - teeth));
        _lower.Append(-Math.Abs(teeth - lips));
    }
}
