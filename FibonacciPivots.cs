using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Fibonacci Pivots — port of NT @FibonacciPivots.
/// PP=(H+L+C)/3. R/S levels:
///   R1=PP+0.382·(H-L), R2=PP+0.618·(H-L), R3=PP+1.000·(H-L)
///   S1=PP-0.382·(H-L), S2=PP-0.618·(H-L), S3=PP-1.000·(H-L)
///
/// <para>Boundary modes (<see cref="SessionTimes"/> dropdown):</para>
/// <list type="bullet">
///   <item><b>InstrumentTradingHours</b> (default) — boundaries from the instrument's
///   Trading Hours template (holiday/DST-correct).</item>
///   <item><b>Custom</b> — boundary triggered when the bar's time-of-day
///   re-enters <see cref="SessionStart"/>–<see cref="SessionEnd"/>. The previous
///   session's H/L/C feed today's pivot values. Out-of-session bars don't update the
///   running H/L/C, so an overnight gap stays clean. Bars are compared in the chart's
///   display timezone — set the chart TZ to the exchange's TZ for intuitive defaults
///   (09:30 / 16:00 = US equities + futures regular session).</item>
/// </list>
/// </summary>
[IndicatorDescription("Fibonacci Pivot Points: classic PP = (H+L+C)/3 with R1/R2/R3 and S1/S2/S3 placed at 0.382, 0.618 and 1.000 of the prior session's range. Session-aware boundaries or UTC-date rollover.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class FibonacciPivots : IndicatorBase, IChartCustomRender
{

    private readonly ChunkedSeries<double> _pp = new();
    private readonly ChunkedSeries<double> _r1 = new(), _r2 = new(), _r3 = new();
    private readonly ChunkedSeries<double> _s1 = new(), _s2 = new(), _s3 = new();
    private IndicatorOutputSeries? _ppW, _r1W, _r2W, _r3W, _s1W, _s2W, _s3W;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double _dayHigh, _dayLow, _dayClose;
    private double? _ppV, _r1V, _r2V, _r3V, _s1V, _s2V, _s3V;

    public FibonacciPivots(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("PP", _pp, PlotStyle.Line, NTColors.Goldenrod,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R1", _r1, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R2", _r2, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("R3", _r3, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S1", _s1, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S2", _s2, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("S3", _s3, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Session Times",
        Description = "Where the session boundary comes from. Instrument Trading Hours (default): the data series' Trading Hours template — holiday/DST-correct. Custom: the manual Session Start / Session End window below; out-of-session bars don't extend the running H/L/C (set Start == End for a 24/7 window rolling over at calendar midnight).",
        Group = "Session")]
    public SessionTimesSource SessionTimes { get; set; } = SessionTimesSource.InstrumentTradingHours;

    [IndicatorParameter(DisplayName = "Session Start",
        Description = "Manual session open (chart-display timezone). Only used when Session Times is Custom. Defaults to 09:30.",
        Group = "Session")]
    public TimeOnly SessionStart { get; set; } = new(9, 30);

    [IndicatorParameter(DisplayName = "Session End",
        Description = "Manual session close (chart-display timezone). Set Start > End for overnight sessions. Only used when Session Times is Custom. Defaults to 16:00.",
        Group = "Session")]
    public TimeOnly SessionEnd { get; set; } = new(16, 0);

    public ISeries<double> PP => _ppW ??= new IndicatorOutputSeries(_pp, Bars);
    public ISeries<double> R1 => _r1W ??= new IndicatorOutputSeries(_r1, Bars);
    public ISeries<double> R2 => _r2W ??= new IndicatorOutputSeries(_r2, Bars);
    public ISeries<double> R3 => _r3W ??= new IndicatorOutputSeries(_r3, Bars);
    public ISeries<double> S1 => _s1W ??= new IndicatorOutputSeries(_s1, Bars);
    public ISeries<double> S2 => _s2W ??= new IndicatorOutputSeries(_s2, Bars);
    public ISeries<double> S3 => _s3W ??= new IndicatorOutputSeries(_s3, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : SessionResetTracker.Create(null, sessionAware: true, SessionStart, SessionEnd);
        _started = false;
        _dayHigh = 0; _dayLow = 0; _dayClose = 0;
        _ppV = _r1V = _r2V = _r3V = _s1V = _s2V = _s3V = null;
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
        bool newSession = _tracker!.OnBar(bar.StartUtc, bar.EndUtc);
        if (_tracker.InSession)
        {
            if (newSession)
            {
                // Commit the previous session's pivots (not on the very first session),
                // then start a fresh running window from this bar.
                if (_started) CommitPivots();
                _started = true;
                _dayHigh = bar.High; _dayLow = bar.Low; _dayClose = bar.Close;
            }
            else
            {
                ExtendDayRange(bar);
            }
        }
        // Out-of-session bars: don't extend the running H/L/C, but still emit the
        // last-committed pivot values so the chart shows continuous level lines.
        AppendCurrent();
    }

    private void ExtendDayRange(Bar bar)
    {
        if (bar.High > _dayHigh) _dayHigh = bar.High;
        if (bar.Low  < _dayLow)  _dayLow  = bar.Low;
        _dayClose = bar.Close;
    }

    private void CommitPivots()
    {
        double pp = (_dayHigh + _dayLow + _dayClose) / 3.0;
        double rng = _dayHigh - _dayLow;
        _ppV = pp;
        _r1V = pp + 0.382 * rng; _r2V = pp + 0.618 * rng; _r3V = pp + rng;
        _s1V = pp - 0.382 * rng; _s2V = pp - 0.618 * rng; _s3V = pp - rng;
    }

    private void AppendCurrent()
    {
        _pp.Append(_ppV ?? 0); _r1.Append(_r1V ?? 0); _r2.Append(_r2V ?? 0); _r3.Append(_r3V ?? 0);
        _s1.Append(_s1V ?? 0); _s2.Append(_s2V ?? 0); _s3.Append(_s3V ?? 0);
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        double sizePx = 11;
        // The level LINES are standard plots (PlotRenderPass honours their Visible / Style /
        // Color); add only the right-edge name label, gated on each plot's Visible and painted
        // in its Colour (Plots[0..6] = PP, R1, R2, R3, S1, S2, S3).
        DrawLabel(ctx, Plots[0], _ppV, sizePx);
        DrawLabel(ctx, Plots[1], _r1V, sizePx);
        DrawLabel(ctx, Plots[2], _r2V, sizePx);
        DrawLabel(ctx, Plots[3], _r3V, sizePx);
        DrawLabel(ctx, Plots[4], _s1V, sizePx);
        DrawLabel(ctx, Plots[5], _s2V, sizePx);
        DrawLabel(ctx, Plots[6], _s3V, sizePx);
    }

    private static void DrawLabel(IIndicatorRenderContext ctx, IPlot plot, double? value, double sizePx)
    {
        if (value is null || !plot.Visible) return;
        LevelLabel.Draw(ctx.Renderer, ctx.Viewport, ctx.PlotArea, plot.Name, value.Value, plot.Color, sizePx);
    }
}
