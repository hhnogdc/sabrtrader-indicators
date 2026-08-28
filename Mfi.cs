using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Money Flow Index.
/// Typical = (H+L+C)/3. Positive flow = Typical·Volume when typical
/// rises; negative when it falls. Σ over Period.
/// MFI = sumNeg == 0 ? 50 : 100 − 100/(1 + sumPos/sumNeg).
/// Bar 0 → 50.
/// Defaults Period=14, DarkCyan plot, 20 oversold (SlateBlue) /
/// 80 overbought (Goldenrod) lines, sub-panel.
/// </summary>
[IndicatorDescription("Money Flow Index: a volume-weighted RSI computed from typical-price * volume over Period bars. Oscillates 0-100; 20/80 mark oversold/overbought.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Mfi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _sumPos = null!;
    private RollingSum _sumNeg = null!;
    private double? _prevTypical;

    public Mfi(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("MFI", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddLine("Oversold",   20, new ChartColor(0x6A, 0x5A, 0xCD));  // SlateBlue
        AddLine("Overbought", 80, PlotColors.Goldenrod);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window over positive/negative money flow.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumPos = new RollingSum(Period);
        _sumNeg = new RollingSum(Period);
        _prevTypical = null;
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
        double typ = (bar.High + bar.Low + bar.Close) / 3.0;
        if (_prevTypical is null)
        {
            _sumPos.Push(0); _sumNeg.Push(0);
            _output.Append(50);
            _prevTypical = typ;
            return;
        }
        double flow = typ * bar.Volume;
        _sumPos.Push(typ > _prevTypical.Value ? flow : 0);
        _sumNeg.Push(typ < _prevTypical.Value ? flow : 0);
        _prevTypical = typ;
        double sn = _sumNeg.Sum;
        _output.Append(sn == 0 ? 50 : 100.0 - 100.0 / (1 + _sumPos.Sum / sn));
    }
}
