using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Triple EMA.
/// Value = 3·EMA1 − 3·EMA2 + EMA3. Default Period=14, Goldenrod.
/// </summary>
[IndicatorDescription("Triple Exponential Moving Average. Value = 3*EMA - 3*EMA(EMA) + EMA(EMA(EMA)), reducing lag compared with a single EMA of the same period.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Tema : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Ema _ema1 = null!;
    private Ema _ema2 = null!;
    private Ema _ema3 = null!;

    public Tema(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("TEMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _ema1 = new Ema(this, input: Input)        { Period = Period };
        _ema2 = new Ema(this, input: _ema1.Output) { Period = Period };
        _ema3 = new Ema(this, input: _ema2.Output) { Period = Period };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _ema1.Output.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            _output.Append(3 * _ema1.Output[barsAgo] - 3 * _ema2.Output[barsAgo] + _ema3.Output[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_ema1.Output.Count == 0) return;
        _output.Append(3 * _ema1.Output[0] - 3 * _ema2.Output[0] + _ema3.Output[0]);
    }
}
