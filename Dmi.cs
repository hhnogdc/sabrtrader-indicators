using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Directional Movement Index (Wilder).
/// (DI+ − DI−) / (DI+ + DI−) where DI± = SMA(DM±)/SMA(TR) over Period.
/// Bar 0 → 0.
/// Default Period=14, DarkCyan, sub-panel.
/// </summary>
[IndicatorDescription("Directional Movement Index (Wilder): (DI+ − DI−) / (DI+ + DI−), a single normalised gauge of directional bias scaled to [−1, +1].")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Dmi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _smaTr = null!, _smaDmPlus = null!, _smaDmMinus = null!;
    private double? _prevHigh, _prevLow, _prevClose;

    public Dmi(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("DMI", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _smaTr = new RollingSum(Period);
        _smaDmPlus = new RollingSum(Period);
        _smaDmMinus = new RollingSum(Period);
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
            _smaTr.Push(bar.High - bar.Low); _smaDmPlus.Push(0); _smaDmMinus.Push(0);
            _output.Append(0);
            _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
            return;
        }
        double pH = _prevHigh!.Value, pL = _prevLow!.Value, pC = _prevClose!.Value;
        double tr = Math.Max(bar.High - bar.Low,
                    Math.Max(Math.Abs(bar.High - pC), Math.Abs(bar.Low - pC)));
        double dmP = (bar.High - pH > pL - bar.Low) ? Math.Max(bar.High - pH, 0) : 0;
        double dmM = (pL - bar.Low > bar.High - pH) ? Math.Max(pL - bar.Low, 0) : 0;
        _smaTr.Push(tr); _smaDmPlus.Push(dmP); _smaDmMinus.Push(dmM);
        double trS = _smaTr.Sum;
        double diP = trS == 0 ? 0 : _smaDmPlus.Sum  / trS;
        double diM = trS == 0 ? 0 : _smaDmMinus.Sum / trS;
        double sum = diP + diM;
        _output.Append(sum == 0 ? 0 : (diP - diM) / sum);
        _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
    }
}
