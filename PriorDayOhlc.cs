using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Prior Day OHLC — port of NT @PriorDayOHLC. Four plots showing the PREVIOUS session's
/// Open/High/Low/Close as flat reference lines. Session-boundary detection (delegated to
/// the shared <see cref="SessionResetTracker"/>):
/// <list type="bullet">
///   <item><b>InstrumentTradingHours</b> (default) — boundaries from the
///   instrument's Trading Hours template (holiday/DST-correct).</item>
///   <item><b>Custom</b> — the manual <see cref="SessionStart"/>–<see cref="SessionEnd"/>
///   window (Start == End for a 24/7 window rolling at calendar midnight).</item>
/// </list>
/// Plots Open (DodgerBlue), High (DarkCyan), Low (Crimson), Close (Goldenrod). Overlay.
/// </summary>
[IndicatorDescription("Prior Day OHLC: four flat reference lines marking the previous session's Open, High, Low and Close. By default the session boundary comes from the instrument's Trading Hours template (correct for futures); turn that off for a manual window or UTC-date rollover.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class PriorDayOhlc : IndicatorBase
{
    private readonly ChunkedSeries<double> _open  = new();
    private readonly ChunkedSeries<double> _high  = new();
    private readonly ChunkedSeries<double> _low   = new();
    private readonly ChunkedSeries<double> _close = new();
    private IndicatorOutputSeries? _oW, _hW, _lW, _cW;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double _dayOpen, _dayHigh, _dayLow, _dayClose;
    private double? _priorOpen, _priorHigh, _priorLow, _priorClose;

    public PriorDayOhlc(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Prior Open",  _open,  PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Prior High",  _high,  PlotStyle.Line, NTColors.DarkCyan,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Prior Low",   _low,   PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Prior Close", _close, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

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

    public ISeries<double> Open  => _oW ??= new IndicatorOutputSeries(_open,  Bars);
    public ISeries<double> High  => _hW ??= new IndicatorOutputSeries(_high,  Bars);
    public ISeries<double> Low   => _lW ??= new IndicatorOutputSeries(_low,   Bars);
    public ISeries<double> Close => _cW ??= new IndicatorOutputSeries(_close, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : SessionResetTracker.Create(null, sessionAware: true, SessionStart, SessionEnd);
        _started = false;
        _priorOpen = _priorHigh = _priorLow = _priorClose = null;
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
        if (_tracker.InSession)
        {
            if (!_started)
            {
                _started = true;
                SeedDay(bar);
            }
            else if (newSession)
            {
                _priorOpen = _dayOpen; _priorHigh = _dayHigh;
                _priorLow = _dayLow;   _priorClose = _dayClose;
                SeedDay(bar);
            }
            else
            {
                if (bar.High > _dayHigh) _dayHigh = bar.High;
                if (bar.Low  < _dayLow)  _dayLow  = bar.Low;
                _dayClose = bar.Close;
            }
        }

        _open.Append(_priorOpen  ?? 0);
        _high.Append(_priorHigh  ?? 0);
        _low.Append (_priorLow   ?? 0);
        _close.Append(_priorClose ?? 0);
    }

    private void SeedDay(Bar bar)
    {
        _dayOpen = bar.Open; _dayHigh = bar.High; _dayLow = bar.Low; _dayClose = bar.Close;
    }
}
