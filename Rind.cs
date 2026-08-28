using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Range Indicator.
/// val1 = trueRange/(C-prevC) if C&gt;prevC else trueRange (trueRange = max(H,prevC)-min(L,prevC)).
/// stochRange = 100·(val1-min(val1,P))/(max(val1,P)-min(val1,P)) (else 100·(val1-min) if range=0).
/// Bar 0 → stochRange=50.
/// Output = EMA(stochRange, Smooth). Defaults PeriodQ=3, Smooth=10, DarkCyan, sub-panel.
/// Inner Ema reads parent-filled stochRange series → constructed parent=null + manually driven.
/// </summary>
[IndicatorDescription("Range Indicator (RIND): EMA-smoothed stochastic of the True Range divided by net up-close movement. Highlights bars where range is large relative to net price change, signalling potential reversals.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Rind : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _stochRange = new();
    private IndicatorOutputSeries? _wrap, _stochW;
    private Ema _ema = null!;
    private RollingMinMax _max = null!, _min = null!;
    private double? _prevClose;
    private int _barIdx;

    public Rind(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("RIND", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "PeriodQ", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodQ { get; set; } = 3;

    [IndicatorParameter(DisplayName = "Smooth", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (PeriodQ < 1 || Smooth < 1) throw new ArgumentOutOfRangeException();
        _stochW = new IndicatorOutputSeries(_stochRange, Bars);
        _ema = new Ema(parent: null, input: _stochW) { Period = Smooth };
        _ema.OnInit(ctx);
        _max = new RollingMinMax(PeriodQ, trackMax: true);
        _min = new RollingMinMax(PeriodQ, trackMax: false);
        _prevClose = null;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i], ctx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0], ctx);
    }

    private void Consume(Bar bar, IIndicatorContext ctx)
    {
        if (_barIdx == 0)
        {
            _max.Push(0); _min.Push(0);
            _stochRange.Append(50);
            _prevClose = bar.Close;
            _ema.OnBarUpdate(ctx);
            _output.Append(_ema.Output[0]);
            _barIdx++;
            return;
        }

        double pC = _prevClose!.Value;
        double trueRange = Math.Max(bar.High, pC) - Math.Min(bar.Low, pC);
        double v = bar.Close > pC ? trueRange / (bar.Close - pC) : trueRange;
        _max.Push(v); _min.Push(v);

        double mn = _min.Value, mx = _max.Value;
        double sr = mx - mn > 0 ? 100 * ((v - mn) / (mx - mn)) : 100 * (v - mn);
        _stochRange.Append(sr);
        _ema.OnBarUpdate(ctx);
        _output.Append(_ema.Output[0]);
        _prevClose = bar.Close;
        _barIdx++;
    }
}
