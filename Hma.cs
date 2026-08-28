using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Hull Moving Average.
/// HMA = WMA(2·WMA(Period/2) − WMA(Period), √Period).
/// Default Period=14, Goldenrod, range [2, int.MaxValue].
/// </summary>
[IndicatorDescription("Hull Moving Average (Alan Hull): WMA(2·WMA(Period/2) − WMA(Period), √Period). Drastically lower lag than a comparable SMA/EMA while staying smooth.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Hma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _diff = new();
    private IndicatorOutputSeries? _wrap;
    private Wma _wma1 = null!;   // WMA(input, Period/2)
    private Wma _wma2 = null!;   // WMA(input, Period)
    private Wma _wmaDiff = null!; // WMA(diff, sqrt(Period))
    private IndicatorOutputSeries? _diffAsBarSourced;

    public Hma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("HMA", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Window size; the original range [2, ∞).",
        MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period), "HMA period must be ≥ 2.");
        _wma1 = new Wma(this, input: Input) { Period = Period / 2 };
        _wma2 = new Wma(this, input: Input) { Period = Period };
        // Wrap _diff as a bar-sourced series so wmaDiff binds to our primary bars.
        _diffAsBarSourced = new IndicatorOutputSeries(_diff, Bars);
        // parent=null: _wmaDiff reads parent-filled _diff; auto-dispatch would
        // double-process. _wma1 and _wma2 read source Input so are safe as children.
        _wmaDiff = new Wma(parent: null, input: _diffAsBarSourced) { Period = (int)Math.Sqrt(Period) };
        _wmaDiff.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // _wma1 and _wma2 are backfilled by cascade. _diff and _wmaDiff are NOT —
        // we drive them inline by appending to _diff one bar at a time, then asking
        // _wmaDiff to consume that single value via its OnBarUpdate. But _wmaDiff was
        // already initialised+backfilled too (empty input series at that time). So
        // we re-run from scratch here, appending each diff and computing wmaDiff.
        //
        // Simpler: walk Input directly and recompute everything in-loop. The children
        // are still there for live OnBarUpdate.
        int n = _wma1.Output.Count;
        // _diff already exists possibly with leftover backfill from initial wmaDiff
        // attach (it was empty). Make sure we start clean.
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            double diff = 2 * _wma1.Output[barsAgo] - _wma2.Output[barsAgo];
            _diff.Append(diff);
        }
        // _wmaDiff was attached when _diff was empty. Re-run its OnDataLoaded so it
        // reads the now-populated _diff series.
        _wmaDiff.OnDataLoaded(ctx);
        // Mirror _wmaDiff output as our value series.
        int m = _wmaDiff.Output.Count;
        for (int i = 0; i < m; i++)
        {
            int b = m - 1 - i;
            _output.Append(_wmaDiff.Output[b]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        if (_wma1.Output.Count == 0) return;
        double diff = 2 * _wma1.Output[0] - _wma2.Output[0];
        _diff.Append(diff);
        _wmaDiff.OnBarUpdate(ctx);
        if (_wmaDiff.Output.Count > 0) _output.Append(_wmaDiff.Output[0]);
    }
}
