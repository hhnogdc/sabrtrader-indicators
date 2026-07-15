using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Camarilla Pivots — port of NT @CamarillaPivots.
/// R/S levels from yesterday's HLC range:
///   R4=C+(H-L)·1.1/2,  R3=C+(H-L)·1.1/4,  R2=C+(H-L)·1.1/6,  R1=C+(H-L)·1.1/12
///   S1=C-(H-L)·1.1/12, S2=C-(H-L)·1.1/6,  S3=C-(H-L)·1.1/4,  S4=C-(H-L)·1.1/2
/// Day boundary via Time.Date. Plots R Crimson / S DodgerBlue. Overlay.
/// </summary>
[IndicatorDescription("Camarilla Pivots. Eight intraday support (S1-S4) and resistance (R1-R4) levels derived from the prior day's high/low/close using fixed 1.1/N coefficients. Commonly used by day traders for fade and breakout setups.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class CamarillaPivots : IndicatorBase, IChartCustomRender
{

    private readonly ChunkedSeries<double> _r1 = new(), _r2 = new(), _r3 = new(), _r4 = new();
    private readonly ChunkedSeries<double> _s1 = new(), _s2 = new(), _s3 = new(), _s4 = new();
    private IndicatorOutputSeries? _r1W, _r2W, _r3W, _r4W, _s1W, _s2W, _s3W, _s4W;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double _dayHigh, _dayLow, _dayClose;
    private double? _r1V, _r2V, _r3V, _r4V, _s1V, _s2V, _s3V, _s4V;

    [IndicatorParameter(DisplayName = "Session Times",
        Description = "Where the session boundary comes from. Instrument Trading Hours (default): the data series' Trading Hours template — holiday/DST-correct. Custom: the manual Session Start / Session End window below; out-of-session bars don't feed the H/L/C (set Start == End for a 24/7 window rolling over at calendar midnight).",
        Group = "Session")]
    public SessionTimesSource SessionTimes { get; set; } = SessionTimesSource.InstrumentTradingHours;

    [IndicatorParameter(DisplayName = "Session Start",
        Description = "Manual session open (chart-display timezone). Only used when Session Times is Custom. 09:30 = US RTH; 18:00 = CME futures.",
        Group = "Session")]
    public TimeOnly SessionStart { get; set; } = new(9, 30);

    [IndicatorParameter(DisplayName = "Session End",
        Description = "Manual session close (chart-display timezone). Set Start > End for an overnight session. Only used when Session Times is Custom.",
        Group = "Session")]
    public TimeOnly SessionEnd { get; set; } = new(16, 0);

    public CamarillaPivots(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // NT colour mapping: resistance levels = DodgerBlue, support = Crimson
        // (@CamarillaPivots.cs:71-78). Not the "intuitive" red-resistance scheme.
        var pivotLevel = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("R1", _r1, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("R2", _r2, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("R3", _r3, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("R4", _r4, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("S1", _s1, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("S2", _s2, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("S3", _s3, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = pivotLevel });
        AddPlot(new Plot("S4", _s4, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = pivotLevel });
    }

    public ISeries<double> R1 => _r1W ??= new IndicatorOutputSeries(_r1, Bars);
    public ISeries<double> R2 => _r2W ??= new IndicatorOutputSeries(_r2, Bars);
    public ISeries<double> R3 => _r3W ??= new IndicatorOutputSeries(_r3, Bars);
    public ISeries<double> R4 => _r4W ??= new IndicatorOutputSeries(_r4, Bars);
    public ISeries<double> S1 => _s1W ??= new IndicatorOutputSeries(_s1, Bars);
    public ISeries<double> S2 => _s2W ??= new IndicatorOutputSeries(_s2, Bars);
    public ISeries<double> S3 => _s3W ??= new IndicatorOutputSeries(_s3, Bars);
    public ISeries<double> S4 => _s4W ??= new IndicatorOutputSeries(_s4, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : SessionResetTracker.Create(null, sessionAware: true, SessionStart, SessionEnd);
        _started = false;
        _r1V = _r2V = _r3V = _r4V = _s1V = _s2V = _s3V = _s4V = null;
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
            if (!_started)
            {
                _started = true;
                _dayHigh = bar.High; _dayLow = bar.Low; _dayClose = bar.Close;
            }
            else if (newSession)
            {
                // Close out the previous session; compute Camarilla levels from its HLC.
                double rng = _dayHigh - _dayLow;
                _r1V = _dayClose + rng * 1.1 / 12;
                _r2V = _dayClose + rng * 1.1 / 6;
                _r3V = _dayClose + rng * 1.1 / 4;
                _r4V = _dayClose + rng * 1.1 / 2;
                _s1V = _dayClose - rng * 1.1 / 12;
                _s2V = _dayClose - rng * 1.1 / 6;
                _s3V = _dayClose - rng * 1.1 / 4;
                _s4V = _dayClose - rng * 1.1 / 2;
                _dayHigh = bar.High; _dayLow = bar.Low; _dayClose = bar.Close;
            }
            else
            {
                if (bar.High > _dayHigh) _dayHigh = bar.High;
                if (bar.Low  < _dayLow)  _dayLow  = bar.Low;
                _dayClose = bar.Close;
            }
        }
        _r1.Append(_r1V ?? 0); _r2.Append(_r2V ?? 0); _r3.Append(_r3V ?? 0); _r4.Append(_r4V ?? 0);
        _s1.Append(_s1V ?? 0); _s2.Append(_s2V ?? 0); _s3.Append(_s3V ?? 0); _s4.Append(_s4V ?? 0);
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        double sizePx = 11;
        // The level LINES are standard plots (PlotRenderPass honours their Visible / Style /
        // Color); here we add only the right-edge name label. Gate each label on its plot's
        // Visible and paint it in the plot's Colour so a hidden level drops its label too and
        // a recoloured line carries its label colour with it (Plots[0..7] = R1..R4, S1..S4).
        DrawLabel(ctx, Plots[0], _r1V, sizePx);
        DrawLabel(ctx, Plots[1], _r2V, sizePx);
        DrawLabel(ctx, Plots[2], _r3V, sizePx);
        DrawLabel(ctx, Plots[3], _r4V, sizePx);
        DrawLabel(ctx, Plots[4], _s1V, sizePx);
        DrawLabel(ctx, Plots[5], _s2V, sizePx);
        DrawLabel(ctx, Plots[6], _s3V, sizePx);
        DrawLabel(ctx, Plots[7], _s4V, sizePx);
    }

    private static void DrawLabel(IIndicatorRenderContext ctx, IPlot plot, double? value, double sizePx)
    {
        if (value is null || !plot.Visible) return;
        LevelLabel.Draw(ctx.Renderer, ctx.Viewport, ctx.PlotArea, plot.Name, value.Value, plot.Color, sizePx);
    }
}
