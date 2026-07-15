using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Alerts;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.OrderFlow;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Cumulative Delta as OHLC candles — the running sum of per-tick
/// (AskVolume − BidVolume), captured per-bar as Open / High / Low /
/// Close of the cumulative trace. A 1-on-1 functional port of the
/// NinjaTrader <c>TDUCumulativeDeltaVolume</c> study: cum-delta
/// candle, optional Bollinger Bands over the close, and price /
/// oscillator divergence lines.
///
/// <para>Per-bar math:
///   <list type="bullet">
///     <item><b>Open</b> = the running cum delta AT BAR START
///       (= the previous bar's close).</item>
///     <item><b>Close</b> = Open + <see cref="OrderFlowBar.Delta"/>.</item>
///     <item><b>High</b> = Open + <see cref="OrderFlowBar.LocalDeltaHigh"/>.</item>
///     <item><b>Low</b> = Open + <see cref="OrderFlowBar.LocalDeltaLow"/>.</item>
///   </list>
/// </para>
///
/// <para><b>Plots</b>: index 0 is the legacy <c>CumDelta</c> <see cref="OhlcPlot"/>
/// (custom-rendered candle; kept at slot 0 so saved workspaces that stored
/// <c>PlotStyle.Candle</c> on the old candle plot still decode cleanly).
/// Indices 1–10 mirror NinjaTrader <c>TDUCumulativeDeltaVolume</c>
/// Values[0..9]: BB Low / Mid / High, Trend, High / Low / Open / Close,
/// Histogram, Divergence — all in the data window
/// (<c>IncludeInDataWindow</c>).</para>
///
/// <para><b>Bollinger Bands</b> (BB Low/Mid/High): SMA(<see cref="Period"/>)
/// of the cum-delta CLOSE flanked by ± <see cref="NumStdDev"/>·σ (population
/// σ, NT @StdDev). The window spans session resets exactly as NT's
/// <c>Bollinger(CandleClose,…)</c> does, and — matching NT's
/// <c>CurrentBar &lt; 5</c> guard — bands are NaN for the first five
/// bars. The forming bar's close feeds the window live (NT recomputes
/// the band every tick), so the bands breathe intra-bar.</para>
///
/// <para><b>Divergence</b> (<see cref="ShowDivergence"/>, default ON):
/// price swing highs/lows are detected with a hardcoded strength of 2
/// (NT uses <c>Swing(2)</c>, NOT <see cref="SwingStrength"/> — that
/// only gates the warm-up). A regular bullish divergence draws a line
/// between the price lows AND between the corresponding cum-delta lows
/// when price makes a lower-or-equal low while the cum-delta makes a
/// higher low; bearish is the mirror on highs. Lines render on both the
/// indicator's own subpanel (cum-delta lows/highs) and the price panel
/// (price lows/highs), exactly as NT's <c>DrawOnPricePanel</c> toggle
/// does. <see cref="SwingStrength"/> / <see cref="LookbackPeriod"/> are
/// retained for settings parity (LookbackPeriod is effectively inert in
/// NT too, since 30 ≫ the strength-2 confirmation distance).</para>
///
/// <para><b>Session reset</b>: the cumulator ALWAYS resets to zero at
/// each session open — same as NinjaTrader's Order Flow Cumulative
/// Delta with Period = Session. <see cref="SessionTimes"/> selects
/// where the session times come from: the data series' Trading Hours
/// template (default, holiday/DST-correct) or the manual
/// <see cref="SessionStart"/> time-of-day. The trace stays continuous
/// between resets — no NaN gaps.</para>
///
/// <para>Requires orderflow data
/// (<see cref="RequiresOrderFlowAttribute"/>). This is the one allowed
/// departure from NT: the delta is sourced from per-bar orderflow
/// (AskVolume − BidVolume) rather than recomputed tick-by-tick — the
/// two are value-equivalent for the BidAsk method. NT's alternate
/// <c>UpDownTick</c> / <c>TickCount</c> methods and its
/// (unused) SmoothPeriod are intentionally not ported.</para>
/// </summary>
[IndicatorDescription("Cumulative Delta (CVD) as OHLC candles: the running sum of per-bar (ask volume − bid volume), with optional Bollinger Bands and price/oscillator divergence lines. Rising CVD shows net aggressive buying and confirms an uptrend (falling CVD, a downtrend); a CVD-vs-price divergence warns the move is not backed by order flow. Requires order-flow data.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[RequiresOrderFlow]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class CumulativeDelta : IndicatorBase, IIndicatorPanelHint,
    IChartCustomRender, IPricePanelOverlayRender
{
    private readonly ChunkedSeries<double> _open  = new();
    private readonly ChunkedSeries<double> _high  = new();
    private readonly ChunkedSeries<double> _low   = new();
    private readonly ChunkedSeries<double> _close = new();
    private IndicatorOutputSeries? _openView, _highView, _lowView, _closeView;

    // Bollinger bands over the cum-delta close.
    private readonly ChunkedSeries<double> _bbHigh = new();
    private readonly ChunkedSeries<double> _bbMid  = new();
    private readonly ChunkedSeries<double> _bbLow  = new();
    private IndicatorOutputSeries? _bbHighView, _bbMidView, _bbLowView;
    private RollingStdDev _bbWindow = null!;

    // NT Values[3] Trend, Values[8] Histogram, Values[9] Signal ("Divergence").
    private readonly ChunkedSeries<double> _trend = new();
    private readonly ChunkedSeries<double> _histogram = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _trendView, _histogramView, _signalView;

    private static readonly ChartColor BarUpColor = NTColors.DarkSeaGreen;
    private static readonly ChartColor BarDownColor = NTColors.IndianRed;
    private const double CandleBodyFillRatio = 0.7;
    private const double CandleWickWidthPx = 1.0;

    private double _cumDelta;
    // Session tracking. With Session Times = Instrument Trading Hours the cursor resets the
    // cumulator at the SELECTED data-series template's session open (holiday/DST-correct).
    // With Session Times = Custom the manual SessionStart anchor-day logic (_prevBarTime) is used.
    private SessionResetTracker? _tracker;
    private DateTime _prevBarTime;

    // Per-bar boundary cache for the OnEachTick path. The session-reset
    // decision (which advances the stateful tracker / _prevBarTime) must run
    // exactly ONCE per bar — on the bar's FIRST event, intra-bar or close —
    // so the forming bar of a new session shows entering = 0 live and the
    // tracker never double-advances when that same bar later closes.
    // _entering is the committed cum delta at this bar's open; _cumDelta is
    // committed only when the bar actually CLOSES, so intra-bar previews
    // never poison the accumulator.
    private bool _activeBarSeen;
    private DateTime _activeBarStart;
    private double _entering;

    public CumulativeDelta(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        // NT parity: NinjaTrader's cumulative-delta study updates the
        // developing candle tick-by-tick. With the OnBarClose default the
        // dispatch cadence gate drops every intra-bar update and the forming
        // cum-delta candle only appears once the bar closes.
        Calculate = CalculationMode.OnEachTick;
        // Index 0: CumDelta OhlcPlot — custom-rendered candle. Slot 0 is deliberate:
        // IndicatorCodec restores plot style/visibility BY INDEX, and pre-refactor
        // workspaces stored PlotStyle.Candle on plots[0]. A plain Plot there would throw.
        AddPlot(new OhlcPlot(
            name:         "CumDelta",
            openValues:   _open,
            highValues:   _high,
            lowValues:    _low,
            closeValues:  _close,
            bullishColor: BarUpColor,
            bearishColor: BarDownColor,
            wickWidthPx:  CandleWickWidthPx,
            bodyFillRatio: CandleBodyFillRatio)
        {
            IsCustomRendered = true,
            IncludeInDataWindow = false,
            ShowPriceMarker = false,
            Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "cum-delta candle (custom render)"),
        });
        // NT Values[0..9] at indices 1..10 — all surface in the data window.
        AddPlot(new Plot("BB Low",  _bbLow,  PlotStyle.Line, NTColors.Yellow, 1.0)
        { Visible = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("BB Mid",  _bbMid,  PlotStyle.Dots, NTColors.Yellow, 1.0)
        { Visible = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("BB High", _bbHigh, PlotStyle.Line, NTColors.Yellow, 1.0)
        { Visible = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Trend", _trend, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, "+1 uptrend / -1 downtrend") });
        AddPlot(new Plot("High", _high, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "cum-delta candle high") });
        AddPlot(new Plot("Low", _low, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "cum-delta candle low") });
        AddPlot(new Plot("Open", _open, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "cum-delta candle open") });
        AddPlot(new Plot("Close", _close, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = true, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "cum-delta candle close") });
        AddPlot(new Plot("Histogram", _histogram, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Divergence", _signal, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, IncludeInDataWindow = true, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, "+1 bullish / -1 bearish divergence") });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CumDelta values are signed cumulative volume (often in the
    /// hundreds or thousands), unrelated to the price axis. Render on
    /// its own subpanel — same convention as RSI / MACD / OBV.
    /// </remarks>
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    [IndicatorParameter]
    public SessionTimesSource SessionTimes { get; set; } = SessionTimesSource.InstrumentTradingHours;

    [IndicatorParameter]
    public TimeOnly SessionStart { get; set; } = new(9, 30);

    // ─── Bollinger ──────────────────────────────────────────────────
    // No master "Show Bollinger" toggle: the three BB plots are hidden by
    // default and the user shows them via the chart's per-plot visibility
    // control (which persists). Period / NumStdDev still drive the math.
    [IndicatorParameter(DisplayName = "NumStdDev", MinValue = 0, MaxValue = int.MaxValue, Step = 0.5)]
    public double NumStdDev { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    // ─── Divergence ─────────────────────────────────────────────────
    [IndicatorParameter(DisplayName = "Show Divergence")]
    public bool ShowDivergence { get; set; } = true;

    [IndicatorParameter(DisplayName = "Swing strength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SwingStrength { get; set; } = 5;

    [IndicatorParameter(DisplayName = "Lookback period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriod { get; set; } = 30;

    [IndicatorParameter(DisplayName = "Enable alerts")]
    public bool EnableAlerts { get; set; }

    [IndicatorParameter(DisplayName = "Alert sound")]
    public string? AlertSound { get; set; } = BuiltinAlertSounds.Default;

    [IndicatorParameter(DisplayName = "Bullish divergence")]
    public ChartColor BullishDivergence { get; set; } = NTColors.Lime;

    [IndicatorParameter(DisplayName = "Bearish divergence")]
    public ChartColor BearishDivergence { get; set; } = NTColors.Magenta;

    /// <summary>Per-bar Open of the cum-delta candle (running cum at bar start).</summary>
    public ISeries<double> Open  => _openView  ??= new IndicatorOutputSeries(_open,  Bars);

    /// <summary>Per-bar High of the cum-delta candle (running cum + LocalDeltaHigh).</summary>
    public ISeries<double> High  => _highView  ??= new IndicatorOutputSeries(_high,  Bars);

    /// <summary>Per-bar Low of the cum-delta candle (running cum + LocalDeltaLow).</summary>
    public ISeries<double> Low   => _lowView   ??= new IndicatorOutputSeries(_low,   Bars);

    /// <summary>Per-bar Close of the cum-delta candle (= Open + bar.Delta).</summary>
    public ISeries<double> Close => _closeView ??= new IndicatorOutputSeries(_close, Bars);

    /// <summary>Bollinger upper band over the cum-delta close.</summary>
    public ISeries<double> BollingerHigh => _bbHighView ??= new IndicatorOutputSeries(_bbHigh, Bars);

    /// <summary>Bollinger middle band (SMA) over the cum-delta close.</summary>
    public ISeries<double> BollingerMid  => _bbMidView  ??= new IndicatorOutputSeries(_bbMid,  Bars);

    /// <summary>Bollinger lower band over the cum-delta close.</summary>
    public ISeries<double> BollingerLow  => _bbLowView  ??= new IndicatorOutputSeries(_bbLow,  Bars);

    /// <summary>NT <c>BBLow</c> alias.</summary>
    public ISeries<double> BBLow => BollingerLow;

    /// <summary>NT <c>BBMid</c> alias.</summary>
    public ISeries<double> BBMid => BollingerMid;

    /// <summary>NT <c>BBHigh</c> alias.</summary>
    public ISeries<double> BBHigh => BollingerHigh;

    /// <summary>NT <c>CandleOpen</c> alias.</summary>
    public ISeries<double> CandleOpen => Open;

    /// <summary>NT <c>CandleHigh</c> alias.</summary>
    public ISeries<double> CandleHigh => High;

    /// <summary>NT <c>CandleLow</c> alias.</summary>
    public ISeries<double> CandleLow => Low;

    /// <summary>NT <c>CandleClose</c> alias.</summary>
    public ISeries<double> CandleClose => Close;

    /// <summary>+1 when close &gt;= prior close, -1 otherwise (NT Values[3]; NaN for the first five bars).</summary>
    public ISeries<double> Trend => _trendView ??= new IndicatorOutputSeries(_trend, Bars);

    /// <summary>Reserved NT plot column (Values[8]); not populated by the NT study either.</summary>
    public ISeries<double> Histogram => _histogramView ??= new IndicatorOutputSeries(_histogram, Bars);

    /// <summary>Divergence signal: +1 bullish, -1 bearish, 0 otherwise (NT Values[9] / plot "Divergence").</summary>
    public ISeries<double> Signal => _signalView ??= new IndicatorOutputSeries(_signal, Bars);

    /// <summary>
    /// Alias for <see cref="Close"/> — matches the
    /// <see cref="IPlot.Values"/> convention so consumers expecting a
    /// single "current value" series (legend, price marker, crosshair)
    /// see the most recent close.
    /// </summary>
    public ISeries<double> Output => Close;

    /// <summary>Number of bullish divergences currently drawn (test/diagnostic hook).</summary>
    public int BullishDivergenceCount => _bull.Count;

    /// <summary>Number of bearish divergences currently drawn (test/diagnostic hook).</summary>
    public int BearishDivergenceCount => _bear.Count;

    /// <summary>One drawn divergence line's endpoints (absolute bar indices + the
    /// oscillator-panel and price-panel Y values), for endpoint-geometry tests.</summary>
    internal readonly record struct DivergenceEndpoints(
        int IdxA, double OscA, double PriceA, int IdxB, double OscB, double PriceB);

    /// <summary>All currently-drawn divergence lines (bullish then bearish), test/diagnostic hook.</summary>
    internal IEnumerable<DivergenceEndpoints> DivergenceLinesForTest()
    {
        foreach (var s in _bull.Values)
            yield return new DivergenceEndpoints(s.IdxA, s.OscA, s.PriceA, s.IdxB, s.OscB, s.PriceB);
        foreach (var s in _bear.Values)
            yield return new DivergenceEndpoints(s.IdxA, s.OscA, s.PriceA, s.IdxB, s.OscB, s.PriceB);
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (NumStdDev < 0) throw new ArgumentOutOfRangeException(nameof(NumStdDev), "NumStdDev must be non-negative.");
        if (SwingStrength < 1) throw new ArgumentOutOfRangeException(nameof(SwingStrength));
        if (LookbackPeriod < 1) throw new ArgumentOutOfRangeException(nameof(LookbackPeriod));

        _cumDelta = 0;
        _prevBarTime = default;
        _activeBarSeen = false;
        _activeBarStart = default;
        _entering = 0;
        _bbWindow = new RollingStdDev(Period);

        ClearDivergenceState();

        // Trading-hours-driven reset cursor, only in instrument-hours mode.
        // sessionAware:false is irrelevant here (an iterator is supplied), and the reset is
        // boundary-only (we never gate accumulation on InSession — the trace stays continuous).
        _tracker = SessionTimes == SessionTimesSource.InstrumentTradingHours
            ? SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: false, default, default)
            : null;

        RepairPlotStylesAfterWorkspaceRestore();
    }

    /// <summary>
    /// Workspace plot JSON is index-matched. After the NT plot expansion, a
    /// saved <c>PlotStyle.Candle</c> from the old plots[1..3] BB slots (or
    /// any non-OHLC plot) must not reach the standard render pass.
    /// </summary>
    private void RepairPlotStylesAfterWorkspaceRestore()
    {
        for (int i = 0; i < Plots.Count; i++)
        {
            var plot = Plots[i];
            if (plot is IOhlcPlot) continue;
            if (plot.Style == PlotStyle.Candle)
                plot.Style = PlotStyle.Line;
        }
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        ClearDivergenceState();
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        var of = ctx.OrderFlow(0);
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            Step(bars[barsAgo], of[barsAgo], isClosed: true, rowCount: absIdx + 1, ctx, isLive: false);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        Step(bars[0], ctx.OrderFlow(0)[0], ctx.IsBarClosed, rowCount: bars.Count, ctx, isLive: ctx.IsLive);
    }

    /// <summary>
    /// One bar event — closed or intra-bar (OnEachTick). Closed commits the
    /// running cumulator; intra-bar only PREVIEWS the developing candle from
    /// the uncommitted entering value (the orderflow snapshot is cumulative
    /// for the bar, so the preview is exact at every tick). The output row
    /// for the bar is appended on its first event and updated in place after
    /// (the platform's OnEachTick output convention, cf. TduPivots).
    /// Divergence is detected once, on the bar's close.
    /// </summary>
    private void Step(in Bar bar, in OrderFlowBar of, bool isClosed, int rowCount, IIndicatorContext ctx, bool isLive)
    {
        // Bar boundary work, exactly once per bar (first event wins).
        bool firstEvent = !_activeBarSeen || bar.StartUtc != _activeBarStart;
        if (firstEvent)
        {
            _activeBarSeen = true;
            _activeBarStart = bar.StartUtc;

            bool reset;
            if (_tracker is not null)
            {
                // Trading-hours mode: reset at the SELECTED template's session open (a new
                // trading day per the iterator). The trace stays continuous between sessions
                // (we don't gate accumulation on InSession), just zeroed at each open.
                reset = _tracker.OnBar(bar.StartUtc, bar.EndUtc);
            }
            else
            {
                // Custom mode: reset when the bar's "session anchor day" changed. Bars
                // at-or-after SessionStart anchor to today; bars before it to YESTERDAY
                // (their session began the prior day and continues across midnight).
                reset = _prevBarTime != default
                    && SessionAnchorDay(bar.StartUtc, SessionStart) != SessionAnchorDay(_prevBarTime, SessionStart);
                _prevBarTime = bar.StartUtc;
            }
            _entering = reset ? 0 : _cumDelta;
        }

        // OHLC of the running-delta trace within this bar. The trace
        // starts at 0 (relative to bar open), so when we shift it into
        // the cum frame, High = entering + bar.LocalDeltaHigh, etc.
        double open  = _entering;
        double high  = _entering + of.LocalDeltaHigh;
        double low   = _entering + of.LocalDeltaLow;
        double close = _entering + of.Delta;
        if (isClosed) _cumDelta = close;

        Upsert(_open,  open,  rowCount);
        Upsert(_high,  high,  rowCount);
        Upsert(_low,   low,   rowCount);
        Upsert(_close, close, rowCount);

        // Bollinger over the cum-delta close. NT recomputes the band every
        // tick against a CandleClose series whose forming-bar value updates in
        // place — so one Push per bar (first event) + UpdateLast as it grows.
        if (firstEvent) _bbWindow.Push(close);
        else _bbWindow.UpdateLast(close);
        int barIdx = rowCount - 1;
        if (barIdx < 5)
        {
            // NT guards BB behind CurrentBar >= 5.
            Upsert(_bbHigh, double.NaN, rowCount);
            Upsert(_bbMid,  double.NaN, rowCount);
            Upsert(_bbLow,  double.NaN, rowCount);
        }
        else
        {
            double mean = _bbWindow.Mean;
            double sd   = _bbWindow.StdDev;
            Upsert(_bbHigh, mean + NumStdDev * sd, rowCount);
            Upsert(_bbMid,  mean,                  rowCount);
            Upsert(_bbLow,  mean - NumStdDev * sd, rowCount);
        }

        // NT SetValues: Trend only once CurrentBar >= 5.
        if (barIdx >= 5 && _close.TryGet(1, out double prevClose))
            Upsert(_trend, close >= prevClose ? 1.0 : -1.0, rowCount);
        else
            Upsert(_trend, 0, rowCount);

        // NT declares a Histogram plot but never assigns it in the active path.
        Upsert(_histogram, 0, rowCount);

        // Divergence / signal — once per bar, on its close (NT runs it on the first tick
        // of the NEXT bar; same data, fires one bar earlier in wall-clock). Keep the
        // Divergence plot row-aligned on every bar event so the data window can read
        // the hovered bar by absolute index (same contract as OHLC / Trend).
        if (isClosed)
        {
            Upsert(_signal, 0, rowCount);
            if (ShowDivergence && bar.StartUtc != _lastDetectBarStart)
            {
                _lastDetectBarStart = bar.StartUtc;
                CommitClosedBar(barIdx, bar.High, bar.Low, oscHigh: high, oscLow: low, oscClose: close);
                DetectPriceSwings(barIdx);
                DetectDivergence(barIdx, ctx, isLive);
            }
        }
        else if (firstEvent)
        {
            Upsert(_signal, 0, rowCount);
        }
    }

    /// <summary>Grow the output to one row per bar, then overwrite the
    /// current bar's row in place (append-on-first-event / update-after).</summary>
    private static void Upsert(ChunkedSeries<double> series, double value, int rowCount)
    {
        while (series.Count < rowCount) series.Append(value);
        series.UpdateLast(value);
    }

    /// <summary>
    /// The session date this bar's accumulator belongs to. Bars at-or-
    /// after <paramref name="start"/> anchor to today; bars before
    /// <paramref name="start"/> anchor to YESTERDAY (the session that
    /// began the previous day and continues across midnight). Encoding
    /// matches <see cref="SessionWindow.SessionDate"/> for overnight
    /// sessions but is simpler because there is no end boundary.
    /// </summary>
    private static DateOnly SessionAnchorDay(DateTime barTime, TimeOnly start)
    {
        var date = DateOnly.FromDateTime(barTime);
        return TimeOnly.FromDateTime(barTime) >= start ? date : date.AddDays(-1);
    }

    // ───────────────────────── Divergence ──────────────────────────
    //
    // Committed per-closed-bar series (absolute index = closed-bar index),
    // populated on each bar close. Price High/Low drive the swing detector
    // and the price-panel lines; the cum-delta candle High/Low (NT's
    // CandleHigh/CandleLow) drive the oscillator condition + the subpanel
    // lines.

    private readonly struct SwingPt
    {
        public readonly int Bar;        // absolute closed-bar index
        public readonly double Price;
        public SwingPt(int bar, double price) { Bar = bar; Price = price; }
    }

    private readonly struct DivLine
    {
        public readonly int IdxA, IdxB;            // older → newer closed-bar index
        public readonly double PriceA, PriceB;     // price-panel endpoints (price swing High/Low)
        public readonly double OscA, OscB;         // subpanel endpoints — cum-delta CLOSE (rides the trace, not the wick)
        public readonly ChartColor Color;
        public DivLine(int idxA, double priceA, double oscA, int idxB, double priceB, double oscB, ChartColor color)
        { IdxA = idxA; PriceA = priceA; OscA = oscA; IdxB = idxB; PriceB = priceB; OscB = oscB; Color = color; }
    }

    private const int SwingDetectStrength = 2;   // NT hardcodes Swing(2)

    private readonly List<double> _pHigh = new();
    private readonly List<double> _pLow  = new();
    private readonly List<double> _oHigh = new();   // cum-delta candle High — drives the oscillator swing CONDITION (NT CandleHigh)
    private readonly List<double> _oLow  = new();   // cum-delta candle Low  — drives the oscillator swing CONDITION (NT CandleLow)
    private readonly List<double> _oClose = new();  // cum-delta candle Close — the trace the DRAWN osc line rides (so it doesn't float at the wick tips)
    private readonly List<double> _shCache = new();   // rolling price-high window for swing detect
    private readonly List<double> _slCache = new();   // rolling price-low window
    private readonly List<SwingPt> _swingHis = new();
    private readonly List<SwingPt> _swingLos = new();

    // NT-style draw-index bookkeeping (consecutive-bar bullish divergences
    // overwrite the previous line; the NT bearish path has a no-op decrement
    // bug so its lines never overwrite — replicated below for parity).
    private readonly Dictionary<int, DivLine> _bull = new();
    private readonly Dictionary<int, DivLine> _bear = new();
    private int _lastBullIndex, _lastBullBar = int.MinValue;
    private int _lastBearIndex;
    private DateTime _lastDetectBarStart = default;

    private void ClearDivergenceState()
    {
        _pHigh.Clear(); _pLow.Clear(); _oHigh.Clear(); _oLow.Clear(); _oClose.Clear();
        _shCache.Clear(); _slCache.Clear();
        _swingHis.Clear(); _swingLos.Clear();
        _bull.Clear(); _bear.Clear();
        _lastBullIndex = 0; _lastBullBar = int.MinValue;
        _lastBearIndex = 0;
        _lastDetectBarStart = default;
    }

    private void CommitClosedBar(int barIdx, double priceHigh, double priceLow, double oscHigh, double oscLow, double oscClose)
    {
        // Closes arrive in strictly increasing order, so each new bar appends
        // at its own absolute index. A re-close of the same bar is filtered out
        // by the _lastDetectBarStart guard in Step.
        if (_pHigh.Count == barIdx)
        {
            _pHigh.Add(priceHigh); _pLow.Add(priceLow);
            _oHigh.Add(oscHigh);   _oLow.Add(oscLow);   _oClose.Add(oscClose);
        }
        else if (barIdx < _pHigh.Count)
        {
            _pHigh[barIdx] = priceHigh; _pLow[barIdx] = priceLow;
            _oHigh[barIdx] = oscHigh;   _oLow[barIdx] = oscLow;   _oClose[barIdx] = oscClose;
        }
    }

    /// <summary>Strength-2 swing detection over the committed price highs/lows
    /// (NT @Swing semantics: left strictly beyond, right beyond-or-equal).</summary>
    private void DetectPriceSwings(int barIdx)
    {
        const int n = 2 * SwingDetectStrength + 1;   // 5
        _shCache.Add(_pHigh[barIdx]);
        _slCache.Add(_pLow[barIdx]);
        if (_shCache.Count > n) _shCache.RemoveAt(0);
        if (_slCache.Count > n) _slCache.RemoveAt(0);
        if (_shCache.Count < n) return;

        int candidateIdx = barIdx - SwingDetectStrength;

        // Swing high: left bars strictly less, right bars less-or-equal.
        double candHigh = _shCache[SwingDetectStrength];
        bool isHigh = true;
        for (int i = 0; i < SwingDetectStrength; i++)
            if (_shCache[i] >= candHigh) { isHigh = false; break; }
        if (isHigh)
            for (int i = SwingDetectStrength + 1; i < n; i++)
                if (_shCache[i] > candHigh) { isHigh = false; break; }
        if (isHigh) _swingHis.Add(new SwingPt(candidateIdx, candHigh));

        // Swing low: left bars strictly greater, right bars greater-or-equal.
        double candLow = _slCache[SwingDetectStrength];
        bool isLow = true;
        for (int i = 0; i < SwingDetectStrength; i++)
            if (_slCache[i] <= candLow) { isLow = false; break; }
        if (isLow)
            for (int i = SwingDetectStrength + 1; i < n; i++)
                if (_slCache[i] < candLow) { isLow = false; break; }
        if (isLow) _swingLos.Add(new SwingPt(candidateIdx, candLow));
    }

    private void DetectDivergence(int barIdx, IIndicatorContext ctx, bool isLive)
    {
        int cur = barIdx + 1;                 // NT CurrentBar (forming bar)
        if (cur < SwingStrength * 2) return;
        if (cur < 13) return;
        DetectBullishDivergence(barIdx, cur, ctx, isLive);
        DetectBearishDivergence(barIdx, cur, ctx, isLive);
    }

    private void DetectBullishDivergence(int b, int cur, IIndicatorContext ctx, bool isLive)
    {
        if (_swingLos.Count == 0) return;
        int index = _swingLos.Count - 1;
        int cnt = 0;
        while (true)
        {
            SwingPt sw = _swingLos[index];
            if (cur - 1 != sw.Bar)   // sw is not the just-closed bar
            {
                double minPriceLow = MinRange(_pLow, sw.Bar, b);
                if (_pLow[b] <= minPriceLow)
                {
                    double minOsc = MinRange(_oLow, sw.Bar, b);
                    if (_oLow[sw.Bar] <= minOsc && _oLow[b] > minOsc)
                    {
                        // Bullish divergence: price lower-low, cum-delta higher-low.
                        int key;
                        if (_lastBullBar == cur || _lastBullBar == cur - 1) key = _lastBullIndex - 1;
                        else key = _lastBullIndex;
                        // Osc endpoints ride the cum-delta CLOSE (the trace), not the
                        // Low wick the CONDITION above uses — a wick anchor floats the
                        // line off the visible trace on this candle oscillator.
                        _bull[key] = new DivLine(sw.Bar, _pLow[sw.Bar], _oClose[sw.Bar], b, _pLow[b], _oClose[b], BullishDivergence);
                        _lastBullBar = cur;
                        _lastBullIndex = key + 1;
                        Upsert(_signal, 1, b + 1);
                        if (EnableAlerts && isLive)
                            ctx.Alert("cumdelta-bull-div", "Bullish divergence", AlertSound, AlertSeverity.Info);
                    }
                }
            }

            index--;
            cnt++;
            if (index < 0) return;
            if (cnt > 5) return;
            SwingPt next = _swingLos[index];
            if (next.Price < sw.Price) return;
        }
    }

    private void DetectBearishDivergence(int b, int cur, IIndicatorContext ctx, bool isLive)
    {
        if (_swingHis.Count == 0) return;
        int index = _swingHis.Count - 1;
        // NT increments no counter in the bearish walk (a copy-paste omission),
        // so it walks every swing high back to the first monotonic break.
        while (true)
        {
            SwingPt sw = _swingHis[index];
            if (cur - 1 != sw.Bar)
            {
                double maxPriceHigh = MaxRange(_pHigh, sw.Bar, b);
                if (_pHigh[b] >= maxPriceHigh)
                {
                    double maxOsc = MaxRange(_oHigh, sw.Bar, b);
                    if (_oHigh[sw.Bar] >= maxOsc && _oHigh[b] < maxOsc)
                    {
                        // NT decrements _lastBearishDrawBar here (not the index) and
                        // then overwrites it 1:1 below — a no-op, so bearish lines
                        // never overwrite. Replicated: always a fresh key.
                        // Osc endpoints ride the cum-delta CLOSE (the trace), not the
                        // High wick the CONDITION above uses (whose tips float above the
                        // bodies — the reported "start hangs in midair").
                        _bear[_lastBearIndex] = new DivLine(sw.Bar, _pHigh[sw.Bar], _oClose[sw.Bar], b, _pHigh[b], _oClose[b], BearishDivergence);
                        _lastBearIndex++;
                        Upsert(_signal, -1, b + 1);
                        if (EnableAlerts && isLive)
                            ctx.Alert("cumdelta-bear-div", "Bearish divergence", AlertSound, AlertSeverity.Info);
                    }
                }
            }

            index--;
            if (index < 0) return;
            SwingPt next = _swingHis[index];
            if (next.Price < sw.Price) return;
        }
    }

    private static double MinRange(List<double> s, int a, int b)
    {
        double m = double.MaxValue;
        for (int i = a; i <= b; i++) if (s[i] < m) m = s[i];
        return m;
    }

    private static double MaxRange(List<double> s, int a, int b)
    {
        double m = double.MinValue;
        for (int i = a; i <= b; i++) if (s[i] > m) m = s[i];
        return m;
    }

    // ───────────────────────── Rendering ───────────────────────────

    /// <summary>Subpanel pass: cum-delta candles (NT OnRender) then divergence lines.</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (Plots.Count > 0 && Plots[0].Visible)
            RenderCumDeltaCandles(ctx);
        if (!ShowDivergence || (_bull.Count == 0 && _bear.Count == 0)) return;
        ChartViewport vp = ctx.Viewport;
        IChartRenderer r = ctx.Renderer;
        foreach (var seg in _bull.Values) DrawOsc(r, vp, seg);
        foreach (var seg in _bear.Values) DrawOsc(r, vp, seg);
    }

    private void RenderCumDeltaCandles(IIndicatorRenderContext ctx)
    {
        ChartViewport vp = ctx.Viewport;
        IChartRenderer r = ctx.Renderer;
        int count = _close.Count;
        if (count == 0) return;

        double bodyWidth = vp.BarSpacingPx * CandleBodyFillRatio;
        double halfBody = bodyWidth / 2.0;
        int first = Math.Max(0, vp.FirstVisibleBarIdx);
        int last = Math.Min(count - 1, vp.LastVisibleBarIdx);

        for (int idx = first; idx <= last; idx++)
        {
            int barsAgo = count - 1 - idx;
            if (!_close.TryGet(barsAgo, out double close) || double.IsNaN(close)) continue;
            if (!_open.TryGet(barsAgo, out double open) || double.IsNaN(open)) continue;
            if (!_high.TryGet(barsAgo, out double high) || double.IsNaN(high)) continue;
            if (!_low.TryGet(barsAgo, out double low) || double.IsNaN(low)) continue;

            double x = vp.BarIndexToPixelX(idx);
            double yO = vp.PriceToPixelY(open);
            double yH = vp.PriceToPixelY(high);
            double yL = vp.PriceToPixelY(low);
            double yC = vp.PriceToPixelY(close);

            bool bullish = close >= open;
            var color = bullish ? BarUpColor : BarDownColor;

            double wickTop = Math.Min(yH, yL);
            double wickBottom = Math.Max(yH, yL);
            r.DrawLine(x, wickTop, x, wickBottom, color, CandleWickWidthPx);

            if (bodyWidth <= 0) continue;

            double bodyTop = Math.Min(yO, yC);
            double bodyBottom = Math.Max(yO, yC);
            double bodyHeight = Math.Max(1.0, bodyBottom - bodyTop);
            r.FillRect(x - halfBody, bodyTop, bodyWidth, bodyHeight, color);
        }
    }

    /// <summary>Price-panel pass: divergence lines between the price lows/highs.</summary>
    public void OnRenderPricePanel(IIndicatorRenderContext ctx)
    {
        if (!ShowDivergence || (_bull.Count == 0 && _bear.Count == 0)) return;
        ChartViewport vp = ctx.Viewport;
        IChartRenderer r = ctx.Renderer;
        foreach (var seg in _bull.Values) DrawPrice(r, vp, seg);
        foreach (var seg in _bear.Values) DrawPrice(r, vp, seg);
    }

    private static void DrawOsc(IChartRenderer r, ChartViewport vp, in DivLine seg)
    {
        if (seg.IdxB < vp.FirstVisibleBarIdx || seg.IdxA > vp.LastVisibleBarIdx) return;
        r.DrawLine(
            vp.BarIndexToPixelX(seg.IdxA), vp.PriceToPixelY(seg.OscA),
            vp.BarIndexToPixelX(seg.IdxB), vp.PriceToPixelY(seg.OscB),
            seg.Color, 2.0);
    }

    private static void DrawPrice(IChartRenderer r, ChartViewport vp, in DivLine seg)
    {
        if (seg.IdxB < vp.FirstVisibleBarIdx || seg.IdxA > vp.LastVisibleBarIdx) return;
        r.DrawLine(
            vp.BarIndexToPixelX(seg.IdxA), vp.PriceToPixelY(seg.PriceA),
            vp.BarIndexToPixelX(seg.IdxB), vp.PriceToPixelY(seg.PriceB),
            seg.Color, 2.0);
    }
}
