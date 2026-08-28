using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// T3 Moving Average.
/// GD = EMA·(1+VFactor) − EMA(EMA)·VFactor, applied <see cref="TCount"/>
/// times in sequence. Defaults: Period=14, TCount=3, VFactor=0.7,
/// DarkCyan. Range: Period [1,∞), TCount [1,∞), VFactor [0,∞).
/// </summary>
[IndicatorDescription("Tim Tillson's T3 moving average. Applies a generalised DEMA (GD = EMA*(1+VFactor) - EMA(EMA)*VFactor) recursively TCount times, producing a smooth low-lag trend line.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class T3 : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    // Per generalised-DD layer: ema_a + ema_b where ema_b = EMA(ema_a),
    // intermediate series carries the GD value, fed into the next layer.
    private GdLayer[] _layers = null!;

    public T3(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("T3", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",  Description = "EMA period of each layer.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "TCount",  Description = "Number of generalised-DD layers (the original default 3).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int TCount { get; set; } = 3;

    [IndicatorParameter(DisplayName = "VFactor", Description = "Volume factor for the GD term (the original default 0.7).",
        MinValue = 0, MaxValue = int.MaxValue, Step = 0.1)]
    public double VFactor { get; set; } = 0.7;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)  throw new ArgumentOutOfRangeException(nameof(Period));
        if (TCount < 1)  throw new ArgumentOutOfRangeException(nameof(TCount));
        if (VFactor < 0) throw new ArgumentOutOfRangeException(nameof(VFactor));
        _layers = new GdLayer[TCount];
        ISeries<double> layerInput = Input;
        for (int i = 0; i < TCount; i++)
        {
            _layers[i] = new GdLayer(this, layerInput, Period, VFactor);
            layerInput = _layers[i].Output;
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var last = _layers[TCount - 1].Output;
        int n = last.Count;
        for (int i = 0; i < n; i++) _output.Append(last[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        var last = _layers[TCount - 1].Output;
        if (last.Count > 0) _output.Append(last[0]);
    }

    /// <summary>
    /// One generalised-DD layer: maintains EMA(input) + EMA(EMA(input))
    /// and emits <c>ema·(1+v) − emaEma·v</c> as its output series.
    /// </summary>
    private sealed class GdLayer : IndicatorBase
    {
        private readonly ChunkedSeries<double> _out = new();
        private IndicatorOutputSeries? _wrap;
        private readonly Ema _ema;
        private readonly Ema _emaEma;
        private readonly double _v;

        public GdLayer(IIndicator parent, ISeries<double> input, int period, double vFactor)
            : base(parent, input)
        {
            _v = vFactor;
            _ema    = new Ema(this, input: input)        { Period = period };
            _emaEma = new Ema(this, input: _ema.Output) { Period = period };
        }

        public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_out, Bars);

        public override void OnDataLoaded(IIndicatorContext ctx)
        {
            int n = _ema.Output.Count;
            for (int i = 0; i < n; i++)
            {
                int b = n - 1 - i;
                _out.Append(_ema.Output[b] * (1 + _v) - _emaEma.Output[b] * _v);
            }
        }

        public override void OnBarUpdate(IIndicatorContext ctx)
        {
            AdvanceChildren(ctx);
            if (_ema.Output.Count == 0) return;
            _out.Append(_ema.Output[0] * (1 + _v) - _emaEma.Output[0] * _v);
        }
    }
}
