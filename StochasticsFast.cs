using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Fast Stochastic.
/// K = raw %K (no Smooth step). D = SMA(K, PeriodD).
/// Defaults PeriodD=3, PeriodK=14. D DodgerBlue, K Goldenrod.
/// 20/80 reference lines DarkCyan. Sub-panel.
/// </summary>
[IndicatorDescription("Fast Stochastic Oscillator: K = raw %K over PeriodK with no smoothing, D = SMA(K, PeriodD). More responsive but noisier than the slow stochastic; 20/80 mark oversold/overbought.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class StochasticsFast : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _k = new();
    private readonly ChunkedSeries<double> _d = new();
    private IndicatorOutputSeries? _kW, _dW;
    private RollingMinMax _maxH = null!, _minL = null!;
    private RollingSum _smaD = null!;
    private double _prevK = 50;
    private int _barIdx;

    public StochasticsFast(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("D", _d, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddPlot(new Plot("K", _k, PlotStyle.Line, PlotColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "80/20") });
        AddLine("Oversold",   20, PlotColors.DarkCyan);
        AddLine("Overbought", 80, PlotColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "PeriodD", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodD { get; set; } = 3;

    [IndicatorParameter(DisplayName = "PeriodK", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodK { get; set; } = 14;

    public ISeries<double> K => _kW ??= new IndicatorOutputSeries(_k, Bars);
    public ISeries<double> D => _dW ??= new IndicatorOutputSeries(_d, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (PeriodK < 1) throw new ArgumentOutOfRangeException(nameof(PeriodK));
        if (PeriodD < 1) throw new ArgumentOutOfRangeException(nameof(PeriodD));
        _maxH = new RollingMinMax(PeriodK, trackMax: true);
        _minL = new RollingMinMax(PeriodK, trackMax: false);
        _smaD = new RollingSum(PeriodD);
        _prevK = 50;
        _barIdx = 0;
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
        _maxH.Push(bar.High); _minL.Push(bar.Low);
        double hh = _maxH.Value, ll = _minL.Value;
        double den = hh - ll;
        double k = den == 0 ? (_barIdx == 0 ? 50.0 : _prevK)
                            : Math.Min(100, Math.Max(0, 100 * (bar.Close - ll) / den));
        _prevK = k;
        _k.Append(k);
        _smaD.Push(k);
        int cnt = Math.Min(PeriodD, _barIdx + 1);
        _d.Append(_smaD.Sum / cnt);
        _barIdx++;
    }
}
