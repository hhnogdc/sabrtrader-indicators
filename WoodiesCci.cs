using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Woodies CCI — Ken Wood's CCI system: the standard CCI(14) drawn as
/// a histogram (the "CCI bars") alongside a faster Turbo CCI(6) line.
/// Trades are read from the histogram's side of the zero line, the
/// ±100 trend bands and the ±200 extremes, with the Turbo line giving
/// earlier turns.
///
/// Composed from two <see cref="Cci"/> built-ins (periods 14 and 6) —
/// no math duplication.
///
/// Defaults Period=14, TurboPeriod=6. Plots CCI histogram (DodgerBlue)
/// + Turbo CCI line (Crimson); reference lines at 0, ±100, ±200;
/// sub-panel.
/// </summary>
[IndicatorDescription("Woodies CCI (Ken Wood): the standard CCI(14) as a histogram plus a Turbo CCI(6) line, with 0 / +/-100 / +/-200 reference bands. The histogram side and the Turbo crossover drive Woodies' trend and reversal setups.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class WoodiesCci : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _hist = new();
    private readonly ChunkedSeries<double> _turbo = new();
    private IndicatorOutputSeries? _histWrap, _turboWrap;
    private Cci _cci = null!;
    private Cci _turboCci = null!;

    public WoodiesCci(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // Histogram added first so the Turbo line renders on top of it.
        AddPlot(new Plot("CCI",   _hist,  PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram,  null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 / +/-100 / +/-200") });
        AddPlot(new Plot("Turbo", _turbo, PlotStyle.Line,      PlotColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Level 2",  200, PlotColors.DarkGray, participatesInAutoScale: false);
        AddLine("Level 1",  100, PlotColors.DarkGray, participatesInAutoScale: false);
        AddLine("Zero line",  0, PlotColors.DarkGray);
        AddLine("Level -1", -100, PlotColors.DarkGray, participatesInAutoScale: false);
        AddLine("Level -2", -200, PlotColors.DarkGray, participatesInAutoScale: false);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Standard CCI period (the histogram).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "TurboPeriod", Description = "Faster Turbo CCI period (the line).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int TurboPeriod { get; set; } = 6;

    public ISeries<double> Output => _histWrap ??= new IndicatorOutputSeries(_hist, Bars);
    public ISeries<double> Turbo  => _turboWrap ??= new IndicatorOutputSeries(_turbo, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (TurboPeriod < 1) throw new ArgumentOutOfRangeException(nameof(TurboPeriod));
        _cci = new Cci(this, bars: Bars) { Period = Period };
        _turboCci = new Cci(this, bars: Bars) { Period = TurboPeriod };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _cci.Output.Count;
        for (int i = 0; i < n; i++)
        {
            _hist.Append(_cci.Output[n - 1 - i]);
            _turbo.Append(_turboCci.Output[n - 1 - i]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_cci.Output.Count == 0) return;
        _hist.Append(_cci.Output[0]);
        _turbo.Append(_turboCci.Output[0]);
    }
}
