using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Current Day OHL — port of NT @CurrentDayOHL with session awareness.
///
/// Three plots showing today's (or today's session's) Open / High / Low as horizontal
/// lines that update through the day. Session-boundary detection is selected by the
/// <see cref="SessionTimes"/> dropdown and delegated to the shared
/// <see cref="SessionResetTracker"/>:
/// <list type="bullet">
///   <item><b>InstrumentTradingHours</b> (default) — boundaries come from the
///   instrument's Trading Hours template (holiday/DST-correct, e.g. ES rolls at the CME
///   ETH 18:00 ET open). Out-of-session bars don't move the running H/L.</item>
///   <item><b>Custom</b> — the manual <see cref="SessionStart"/>–<see cref="SessionEnd"/>
///   window (Start == End for a 24/7 window rolling at calendar midnight).</item>
/// </list>
/// Plots: Open (DodgerBlue), High (DarkCyan), Low (Crimson). Overlay.
/// </summary>
[IndicatorDescription("Today's Open, High and Low drawn as horizontal lines. By default the day flips at the instrument's Trading Hours session open (holiday/DST-correct) and ignores out-of-session bars; switch Session Times to Custom for a manual session window.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class CurrentDayOhl : IndicatorBase
{
    private readonly ChunkedSeries<double> _open = new();
    private readonly ChunkedSeries<double> _high = new();
    private readonly ChunkedSeries<double> _low  = new();
    private IndicatorOutputSeries? _oW, _hW, _lW;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double _dayOpen, _dayHigh, _dayLow;

    public CurrentDayOhl(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var ohlLevel = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("Open", _open, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = ohlLevel });
        AddPlot(new Plot("High", _high, PlotStyle.Line, NTColors.DarkCyan,   1.0) { Semantics = ohlLevel });
        AddPlot(new Plot("Low",  _low,  PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = ohlLevel });
    }

    [IndicatorParameter(DisplayName = "Session Times",
        Description = "Where the session boundary comes from. Instrument Trading Hours (default): the data series' Trading Hours template — holiday/DST-correct. Custom: the manual Session Start / Session End window below; out-of-session bars don't update the day's H/L (set Start == End for a 24/7 window rolling over at calendar midnight).",
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

    public ISeries<double> Open => _oW ??= new IndicatorOutputSeries(_open, Bars);
    public ISeries<double> High => _hW ??= new IndicatorOutputSeries(_high, Bars);
    public ISeries<double> Low  => _lW ??= new IndicatorOutputSeries(_low,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : SessionResetTracker.Create(null, sessionAware: true, SessionStart, SessionEnd);
        _started = false;
        _dayOpen = 0; _dayHigh = 0; _dayLow = 0;
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
        bool newSession = _tracker!.OnBar(bar.Start, bar.End);
        if (newSession)
        {
            _started = true;
            _dayOpen = bar.Open; _dayHigh = bar.High; _dayLow = bar.Low;
        }
        else if (_started && _tracker.InSession)
        {
            if (bar.High > _dayHigh) _dayHigh = bar.High;
            if (bar.Low  < _dayLow)  _dayLow  = bar.Low;
        }
        _open.Append(_dayOpen); _high.Append(_dayHigh); _low.Append(_dayLow);
    }
}
