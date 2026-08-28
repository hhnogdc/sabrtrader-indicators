using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ulcer Index — derived from Skender.Stock.Indicators (Apache-2.0).
/// UI = sqrt( Σ pctDrawdown[k]² / Period )
/// where pctDrawdown[k] = 100·(value[k] − maxValueInWindowUpToK) / maxValueInWindowUpToK.
/// NaN before bar Period−1. Default Period=14, Crimson, sub-panel.
/// </summary>
[IndicatorDescription("Ulcer Index (Peter Martin). Root-mean-square of percentage drawdown from rolling highs, measuring the depth and duration of price retracements as a downside-risk gauge.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class UlcerIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public UlcerIndex(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Ulcer Index", _output, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx + 1 < Period) { _output.Append(double.NaN); _barIdx++; return; }
        // Iterate the lookback window oldest→newest, tracking running max for each step's drawdown.
        double sumSq = 0;
        double maxClose = 0;
        for (int k = 0; k < Period; k++)
        {
            int barsAgo = barsAgoOfCurrent + (Period - 1 - k);
            double v = src[barsAgo];
            if (v > maxClose) maxClose = v;
            double dd = maxClose == 0 ? double.NaN : 100 * (v - maxClose) / maxClose;
            sumSq += dd * dd;
        }
        _output.Append(Math.Sqrt(sumSq / Period));
        _barIdx++;
    }
}
