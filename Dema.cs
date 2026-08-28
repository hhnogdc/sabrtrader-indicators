using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Double EMA.
/// Value = 2·EMA(input) − EMA(EMA(input)). Default Period=14, Goldenrod.
/// </summary>
[IndicatorDescription("Double Exponential Moving Average: 2·EMA(input) − EMA(EMA(input)). Reduces EMA lag while keeping the smoothing.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Dema : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Ema _ema = null!;
    private Ema _emaEma = null!;

    public Dema(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("DEMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _ema    = new Ema(this, input: Input) { Period = Period };
        _emaEma = new Ema(this, input: _ema.Output) { Period = Period };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Children cascaded + backfilled by the framework. Walk their
        // outputs and emit the DEMA value per bar.
        int n = _ema.Output.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            _output.Append(2 * _ema.Output[barsAgo] - _emaEma.Output[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // drive child EMAs on the live bar (else flat line)
        if (_ema.Output.Count == 0) return;
        _output.Append(2 * _ema.Output[0] - _emaEma.Output[0]);
    }
}
