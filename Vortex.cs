using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Vortex Indicator — 1-on-1 port of NT @Vortex.
/// vm+ = |H − prevL|; vm− = |L − prevH|; TR = max(|H−prevC|, |L−prevC|, H−L).
/// VIPlus = ΣVM+ / ΣTR; VIMinus = ΣVM− / ΣTR over Period bars.
/// Defaults Period=14. VIPlus RoyalBlue, VIMinus Red, sub-panel.
/// </summary>
[IndicatorDescription("Vortex Indicator (Botes/Siepman). Two lines, VI+ and VI-, derived from the ratio of cross-bar high/low distances to true range. Crossovers identify trend reversals.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Vortex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _plus  = new();
    private readonly ChunkedSeries<double> _minus = new();
    private IndicatorOutputSeries? _plW, _miW;
    private RollingSum _sumVmPlus = null!, _sumVmMinus = null!, _sumTr = null!;
    private double? _prevHigh, _prevLow, _prevClose;

    public Vortex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("VI+", _plus,  PlotStyle.Line, NTColors.RoyalBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("VI-", _minus, PlotStyle.Line, NTColors.Red,       1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Down, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window for the running sums.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> VIPlus  => _plW ??= new IndicatorOutputSeries(_plus,  Bars);
    public ISeries<double> VIMinus => _miW ??= new IndicatorOutputSeries(_minus, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumVmPlus  = new RollingSum(Period);
        _sumVmMinus = new RollingSum(Period);
        _sumTr      = new RollingSum(Period);
        _prevHigh = _prevLow = _prevClose = null;
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
        if (_prevHigh is null)
        {
            _plus.Append(0); _minus.Append(0);
            _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
            return;
        }
        double pH = _prevHigh!.Value, pL = _prevLow!.Value, pC = _prevClose!.Value;
        double vmP = Math.Abs(bar.High - pL);
        double vmM = Math.Abs(bar.Low - pH);
        double tr = Math.Max(Math.Max(Math.Abs(bar.High - pC), Math.Abs(bar.Low - pC)), bar.High - bar.Low);
        _sumVmPlus.Push(vmP); _sumVmMinus.Push(vmM); _sumTr.Push(tr);
        double trSum = _sumTr.Sum;
        _plus.Append(trSum == 0 ? 0 : _sumVmPlus.Sum / trSum);
        _minus.Append(trSum == 0 ? 0 : _sumVmMinus.Sum / trSum);
        _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
    }
}
