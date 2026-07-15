using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Elder Ray Index — 1-on-1 port of Skender ElderRay.
///   ema       = EMA(Close, LookbackPeriods)
///   BullPower = High − ema
///   BearPower = Low  − ema
/// Default LookbackPeriods=13. Goldenrod EMA, LimeGreen Bull, Red Bear histograms, sub-panel.
/// </summary>
[IndicatorDescription("Elder Ray Index: Bull Power = High − EMA(Close), Bear Power = Low − EMA(Close). Histograms reveal how far buyers and sellers push price beyond the trend EMA.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ElderRay : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _ema  = new();
    private readonly ChunkedSeries<double> _bull = new();
    private readonly ChunkedSeries<double> _bear = new();
    private IndicatorOutputSeries? _eW, _bullW, _bearW;

    private double _emaState;
    private bool _emaSeeded;
    private double _emaSeedSum;
    private int _emaSeedCount;
    private double _k;

    public ElderRay(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // Only Bull/Bear power are plotted, and they oscillate around zero
        // (price distance from the EMA, typically a few ticks). The EMA itself
        // is a price-LEVEL series (~current price); plotting it in this subpanel
        // forces the shared Y-axis to span 0..price, pinning the EMA line to the
        // top and collapsing the histograms into an invisible sliver. The EMA is
        // a calculation input only — it stays exposed via the Ema property for
        // programmatic consumers, but is not a subpanel plot. (Matches the
        // canonical Elder Ray / NinjaTrader ElderRay, which plot Bull/Bear only.)
        AddPlot(new Plot("BullPower", _bull, PlotStyle.Histogram, NTColors.LimeGreen, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("BearPower", _bear, PlotStyle.Histogram, NTColors.Red,       2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "LookbackPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriods { get; set; } = 13;

    public ISeries<double> Ema       => _eW    ??= new IndicatorOutputSeries(_ema,  Bars);
    public ISeries<double> BullPower => _bullW ??= new IndicatorOutputSeries(_bull, Bars);
    public ISeries<double> BearPower => _bearW ??= new IndicatorOutputSeries(_bear, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LookbackPeriods < 1) throw new ArgumentOutOfRangeException(nameof(LookbackPeriods));
        _k = 2d / (LookbackPeriods + 1);
        _emaSeeded = false; _emaSeedSum = 0; _emaSeedCount = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    private void Consume(Bar bar)
    {
        if (!_emaSeeded)
        {
            _emaSeedSum += bar.Close; _emaSeedCount++;
            if (_emaSeedCount < LookbackPeriods)
            {
                _ema.Append(double.NaN); _bull.Append(double.NaN); _bear.Append(double.NaN);
                return;
            }
            _emaState = _emaSeedSum / LookbackPeriods;
            _emaSeeded = true;
        }
        else _emaState = _emaState + _k * (bar.Close - _emaState);

        _ema.Append(_emaState);
        _bull.Append(bar.High - _emaState);
        _bear.Append(bar.Low  - _emaState);
    }
}
