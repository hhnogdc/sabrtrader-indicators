using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Unit options for <see cref="NetChangeDisplay"/>. Matches NT's <c>Cbi.PerformanceUnit</c>.</summary>
public enum NetChangeUnit { Percent, Points, Ticks, Pips, Currency }

/// <summary>
/// Net Change Display — port of NT @NetChangeDisplay. NT renders this
/// as a top-right text overlay via <c>Draw.TextFixed</c>; without a
/// text-drawing primitive we surface the same number as a single-plot
/// value so chart layers can render it however they want
/// (overlay-label, mini-panel, etc.).
///
/// Value = current close vs the previous trading day's close, scaled
/// by <see cref="Unit"/>:
///   <list type="bullet">
///     <item><b>Percent</b>: (close − prevDayClose) / prevDayClose</item>
///     <item><b>Points</b>:  close − prevDayClose</item>
///     <item><b>Ticks</b>:   (close − prevDayClose) / TickSize</item>
///     <item><b>Pips</b>:    Ticks (forex pip = 10·ticks not host-aware here)</item>
///     <item><b>Currency</b>: Points · PointValue (no FX conversion)</item>
///   </list>
///
/// Day boundary detected via Bar.StartUtc.Date changing — same caveat
/// as <see cref="CurrentDayOhl"/>: real session-awareness needs
/// future ISessionSchedule infrastructure.
/// </summary>
[IndicatorDescription("Net Change Display: current close versus the previous session's close, shown as percent, points, ticks, pips or currency, with an in-chart text label.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class NetChangeDisplay : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double? _prevDayClose;
    private double? _lastClose;

    public NetChangeDisplay(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Net Change", _output, PlotStyle.Line, NTColors.LimeGreen, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Unit", Description = "Display unit.")]
    public NetChangeUnit Unit { get; set; } = NetChangeUnit.Percent;

    [IndicatorParameter(DisplayName = "TickSize", Description = "Instrument tick size (used for Ticks/Pips/Currency).",
        MinValue = 0.0001, MaxValue = int.MaxValue, Step = 0.0001)]
    public double TickSize { get; set; } = 0.01;

    [IndicatorParameter(DisplayName = "PointValue", Description = "Currency per point (used for Currency unit).",
        MinValue = 0.0001, MaxValue = int.MaxValue, Step = 0.01)]
    public double PointValue { get; set; } = 1;

    [IndicatorParameter(DisplayName = "PipSize",
        Description = "Forex pip size for the Pips unit (0.0001 for a 5-decimal pair, 0.01 for a JPY pair). 0 = derive as ten ticks.",
        MinValue = 0.0, MaxValue = int.MaxValue, Step = 0.0001)]
    public double PipSize { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Session Times",
        Description = "Where the prior-day boundary comes from. Instrument Trading Hours (default): the data series' Trading Hours template — holiday/DST-correct. Custom: the manual Session Start / Session End window below (set Start == End for a 24/7 window rolling over at calendar midnight).",
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

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : SessionResetTracker.Create(null, sessionAware: true, SessionStart, SessionEnd);
        _started = false;
        _prevDayClose = null;
        _lastClose = null;
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
            if (!_started) _started = true;
            else if (newSession) _prevDayClose = _lastClose;
            _lastClose = bar.Close;
        }

        if (_prevDayClose is null) { _output.Append(0); return; }
        double diff = bar.Close - _prevDayClose.Value;
        double v = Unit switch
        {
            NetChangeUnit.Percent  => diff / _prevDayClose.Value,
            NetChangeUnit.Points   => diff,
            NetChangeUnit.Ticks    => diff / TickSize,
            NetChangeUnit.Pips     => diff / (PipSize > 0 ? PipSize : TickSize * 10.0),
            NetChangeUnit.Currency => diff * PointValue,
            _ => 0,
        };
        _output.Append(v);
    }

    public ChartColor TextColor { get; set; } = NTColors.LimeGreen;
    public double TextSizePx { get; set; } = 14;

    /// <summary>Render the current value as a top-right text overlay (NT Draw.TextFixed parity).</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_output.Count == 0) return;
        double v = _output[0];
        string suffix = Unit switch
        {
            NetChangeUnit.Percent  => "%",
            NetChangeUnit.Points   => " pts",
            NetChangeUnit.Ticks    => " ticks",
            NetChangeUnit.Pips     => " pips",
            NetChangeUnit.Currency => "",
            _ => "",
        };
        double shown = Unit == NetChangeUnit.Percent ? v * 100 : v;
        string sign = shown >= 0 ? "+" : "";
        string text = $"Net Change: {sign}{shown:0.##}{suffix}";
        var (w, h) = ctx.Renderer.MeasureText(text, TextSizePx);
        ctx.Renderer.DrawText(ctx.PlotArea.Right - w - 8, ctx.PlotArea.Y + 8, text, TextColor, TextSizePx);
    }
}
