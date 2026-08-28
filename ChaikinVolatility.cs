using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Chaikin Volatility.
/// ema = EMA(H−L, MAPeriod). Value = (ema[0] − ema[min(barIdx, ROCPeriod)]) / ema[…] · 100.
/// Bar 0 → ema[0].
/// Defaults MAPeriod=10, ROCPeriod=10, Crimson, sub-panel.
/// </summary>
[IndicatorDescription("Chaikin Volatility. Percentage rate of change of an EMA of bar range (High-Low) over ROCPeriod bars: rising values mean expanding ranges, falling values mean contraction.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ChaikinVolatility : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _range = new();
    private IndicatorOutputSeries? _rW, _wrap;
    private Ema _ema = null!;
    private int _barIdx;

    public ChaikinVolatility(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Chaikin Volatility", _output, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "MAPeriod",  Description = "EMA period over H-L.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int MAPeriod { get; set; } = 10;

    [IndicatorParameter(DisplayName = "ROCPeriod", Description = "Lookback for the % change.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int ROCPeriod { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (MAPeriod < 1)  throw new ArgumentOutOfRangeException(nameof(MAPeriod));
        if (ROCPeriod < 1) throw new ArgumentOutOfRangeException(nameof(ROCPeriod));
        _rW = new IndicatorOutputSeries(_range, Bars);
        // parent=null: parent-filled _rW; avoid auto-dispatch double-processing.
        _ema = new Ema(parent: null, input: _rW) { Period = MAPeriod };
        _ema.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            _range.Append(bar.High - bar.Low);
        }
        _ema.OnDataLoaded(ctx);
        int n = _ema.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            if (_barIdx == 0) _output.Append(_ema.Output[b]);
            else
            {
                int back = Math.Min(_barIdx, ROCPeriod);
                double now = _ema.Output[b];
                double then = _ema.Output[b + back];
                _output.Append(then == 0 ? 0 : (now - then) / then * 100);
            }
            _barIdx++;
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var bar = Bars[0];
        _range.Append(bar.High - bar.Low);
        _ema.OnBarUpdate(ctx);
        if (_barIdx == 0) { _output.Append(_ema.Output[0]); _barIdx++; return; }
        int back = Math.Min(_barIdx, ROCPeriod);
        double now = _ema.Output[0];
        double then = _ema.Output[back];
        _output.Append(then == 0 ? 0 : (now - then) / then * 100);
        _barIdx++;
    }
}
