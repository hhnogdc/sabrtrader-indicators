using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Volatility Index.
/// Up/Down accumulators (Wilder smoothing) of StdDev(High,10) and
/// StdDev(Low,10) split by direction; RVI = (rviH + rviL)/2.
/// Bar 0 → 50. Defaults Period=14. Goldenrod, 50 ref line, sub-panel.
/// </summary>
[IndicatorDescription("Relative Volatility Index (Dorsey): RSI-style oscillator built from the StdDev of High and Low split into up-day vs down-day Wilder accumulators. Confirms trends by measuring volatility direction rather than price direction.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Rvi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private StdDev _sdH = null!, _sdL = null!;
    private double _upH, _dnH, _upL, _dnL;
    private double? _prevHigh, _prevLow;
    private int _barIdx;

    public Rvi(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("RVI", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "overbought/oversold") });
        AddLine("Signal", 50, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sdH = new StdDev(this, input: Bars.High) { Period = 10 };
        _sdL = new StdDev(this, input: Bars.Low)  { Period = 10 };
        _upH = _dnH = _upL = _dnL = 0;
        _prevHigh = _prevLow = null;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i], i, bars.Count);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0], 0, 0);
    }

    private void Consume(Bar bar, int absIdx, int _)
    {
        if (_barIdx == 0)
        {
            _output.Append(50);
            _prevHigh = bar.High; _prevLow = bar.Low;
            _barIdx++;
            return;
        }
        double sdH = _sdH.Output[0];
        double sdL = _sdL.Output[0];
        double up, dn;
        // High RVI
        up = bar.High > _prevHigh!.Value ? sdH : 0;
        dn = bar.High < _prevHigh.Value  ? sdH : 0;
        _upH = (_upH * (Period - 1) + up) / Period;
        _dnH = (_dnH * (Period - 1) + dn) / Period;
        double rviH = _upH + _dnH == 0 ? 0 : 100 * _upH / (_upH + _dnH);
        // Low RVI
        up = bar.Low > _prevLow!.Value ? sdL : 0;
        dn = bar.Low < _prevLow.Value  ? sdL : 0;
        _upL = (_upL * (Period - 1) + up) / Period;
        _dnL = (_dnL * (Period - 1) + dn) / Period;
        double rviL = _upL + _dnL == 0 ? 0 : 100 * _upL / (_upL + _dnL);
        _output.Append(_barIdx == 1 ? 50 : (rviH + rviL) / 2);
        _prevHigh = bar.High; _prevLow = bar.Low;
        _barIdx++;
    }
}
