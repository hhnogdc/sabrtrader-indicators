using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Which sessions the Initial Balance indicator paints (NT <c>TDUInitialBalanceDisplay</c>).</summary>
public enum InitialBalanceDisplay
{
    AllDays,
    CurrentDayOnly,
}

/// <summary>Where the level lines are drawn (NT <c>TDUInitialBalanceLinePlacement</c>).</summary>
public enum InitialBalanceLinePlacement
{
    /// <summary>Lines span each session from its open, extending past the last bar.</summary>
    Session,

    /// <summary>A compact 100-px band of today's levels pinned at the chart's right edge.</summary>
    RightEdge,
}

/// <summary>
/// Initial Balance — 1-on-1 port of NT8 <c>TDUInitialBalance</c> (licensing dropped).
///
/// Tracks, per trading day, the manual session window (default 09:30–16:00):
/// <list type="bullet">
///   <item><b>Pre-market</b> high/low/mid from <see cref="PremarketStart"/> until the session open.</item>
///   <item><b>Opening Range</b> (OR) — open price + high/low/mid of the first
///   <see cref="OpeningRangeMinutes"/> minutes.</item>
///   <item><b>Initial Balance</b> (IB) — high/low/mid of the first
///   <see cref="InitialBalanceMinutes"/> minutes, with developing / virgin / broken line
///   states (a level is "broken" once a later bar crosses it) and 100%–600% IB extensions
///   that appear as the session trades through each threshold.</item>
///   <item><b>Session</b> high/low.</item>
/// </list>
///
/// All state is computed from a dedicated 1-minute secondary series (NT's
/// <c>BarsArray[1]</c>) so the levels are identical on every chart bar type. The visuals
/// are painted through <see cref="IChartCustomRender"/> exactly like NT's
/// <c>OnRender</c> — lines, translucent building/committed areas, price/range labels with
/// collision avoidance, an optional statistics card, and optional session / pre-market
/// background shading (painted behind the candles via <see cref="IChartBackgroundRender"/>).
/// The chart-toolbar "IB" dropdown mirrors NT's toolbar menu 1-on-1.
///
/// Ten transparent plots publish the levels to the data box / automation surface,
/// matching NT's plot set (Initial Balance High/Mid/Low, Opening Range High/Low,
/// Pre-market High/Low, Session High/Low, Signal State 0–3).
/// </summary>
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed partial class InitialBalance : IndicatorBase,
    IChartCustomRender, IChartBackgroundRender,
    IIndicatorToolbarMenu, IRepaintNotifier, IRecalculateNotifier
{
    // ───── Per-session model (NT inner class InitialBalance) ───────────────

    /// <summary>One trading day's running OR/IB/session state (NT's inner <c>InitialBalance</c> class).</summary>
    internal sealed class IbSession
    {
        public bool ORSet;
        public bool IBSet;
        public double OpenPrice;
        public double OpeningRangeHigh;
        public double OpeningRangeLow;
        public double InitialBalanceHigh;
        public double InitialBalanceLow;
        public double SessionHigh;
        public double SessionLow;
        public DateTime StartTime;
        public DateTime EndTime;
        public DateTime OpeningRangeEndTime;
        public bool IBStarted;
        public bool IBLowBroken;
        public bool IBHighBroken;
        public DateTime IBLowBreakDateTime;
        public DateTime IBHighBreakDateTime;
    }

    // ───── Plots (NT-parity: ten transparent data-box channels) ────────────

    public const int PlotCount = 10;

    public const int PlotInitialBalanceHigh = 0;
    public const int PlotInitialBalanceMid  = 1;
    public const int PlotInitialBalanceLow  = 2;
    public const int PlotOpeningRangeHigh   = 3;
    public const int PlotOpeningRangeLow    = 4;
    public const int PlotPreMarketHigh      = 5;
    public const int PlotPreMarketLow       = 6;
    public const int PlotSessionHigh        = 7;
    public const int PlotSessionLow         = 8;
    public const int PlotSignalState        = 9;

    private static readonly string[] PlotNames =
    {
        "Initial Balance High", "Initial Balance Mid", "Initial Balance Low",
        "Opening Range High", "Opening Range Low",
        "Pre-market High", "Pre-market Low",
        "Session High", "Session Low",
        "Signal State",
    };

    private readonly ChunkedSeries<double>[] _plots = new ChunkedSeries<double>[PlotCount];
    private readonly IndicatorOutputSeries?[] _plotWrappers = new IndicatorOutputSeries?[PlotCount];

    // ───── Calculation state (NT fields) ───────────────────────────────────

    private readonly object _gate = new();                  // guards _sessions vs the render thread
    private readonly List<IbSession> _sessions = new();     // NT _initialBalances
    private IbSession? _current;                            // NT _currentInitialBalance
    private IbSession[] _renderSnapshot = Array.Empty<IbSession>();

    private bool _preMarketSet;
    private DateTime _preMarketStartTime;
    private double _preMarketHigh;
    private double _preMarketLow;
    private DateTime _prevDate = DateTime.MinValue;
    private DateTime _chartTime;                            // NT _chartTime  (latest 1-min bar time)
    private DateTime _now;                                  // NT _now        (latest primary bar time)

    // Derived session windows, NT State.Configure (all TimeSpan time-of-day).
    private TimeSpan _sessionStart, _sessionEnd, _preMarketStart;
    private TimeSpan _ibOpenTime, _ibEndTime, _orEndTime;

    // Secondary 1-minute series (NT AddDataSeries(Minute, 1)).
    private BarSpecification? _minuteSpec;
    private int _minuteSeriesIndex = -1;
    private DateTime _lastMinuteEndUtc = DateTime.MinValue;

    private double _tickSize = 0.25;   // cached for rendering (render ctx has no tick size)

    public InitialBalance(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // NT: Calculate = Calculate.OnPriceChange.
        Calculate = CalculationMode.OnPriceChange;

        for (int i = 0; i < PlotCount; i++)
        {
            _plots[i] = new ChunkedSeries<double>();
            // NT AddPlot(Brushes.Transparent, ...) — data-box channels only;
            // the indicator paints its own visuals via OnCustomRender.
            var semantics = i == PlotSignalState
                ? new PlotSemantics(PlotKind.Signal, 0, 3, PlotValueUnit.Raw, PlotBullishDirection.None,
                    "0 = outside session, 1 = in session, 2 = opening range complete, 3 = initial balance complete")
                : new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
            AddPlot(new Plot(PlotNames[i], _plots[i], PlotStyle.Line, ChartColor.Transparent, 1.0)
            {
                Visible = false,
                Semantics = semantics,
            });
        }
    }

    // ───── Parameters — 01. Parameters (NT GroupName "01. Parameters") ─────

    [IndicatorParameter]
    public bool ShowAll { get; set; } = true;

    [IndicatorParameter]
    public TimeOnly SessionStart { get; set; } = new(9, 30);

    [IndicatorParameter]
    public TimeOnly SessionEnd { get; set; } = new(16, 0);

    [IndicatorParameter]
    public TimeOnly PremarketStart { get; set; } = new(4, 0);

    [IndicatorParameter]
    public int OpeningRangeMinutes { get; set; } = 30;

    [IndicatorParameter]
    public int InitialBalanceMinutes { get; set; } = 60;

    [IndicatorParameter]
    public bool ShowPremarket { get; set; } = true;

    [IndicatorParameter]
    public bool ShowSessionHighLow { get; set; } = true;

    [IndicatorParameter]
    public bool ShowIB { get; set; } = true;

    [IndicatorParameter]
    public bool ShowOR { get; set; } = true;

    [IndicatorParameter]
    public bool ShowTodaysIBExtensions { get; set; } = true;

    [IndicatorParameter]
    public bool ShowPreviousIBExtensions { get; set; } = true;

    [IndicatorParameter]
    public bool ShowStatistics { get; set; }

    [IndicatorParameter]
    public bool ShowPrice { get; set; } = true;

    [IndicatorParameter]
    public bool ShowRange { get; set; } = true;

    [IndicatorParameter]
    public InitialBalanceDisplay Display { get; set; } = InitialBalanceDisplay.AllDays;

    [IndicatorParameter]
    public InitialBalanceLinePlacement LinesPlacement { get; set; } = InitialBalanceLinePlacement.Session;

    [IndicatorParameter]
    public bool ShowSessionBackground { get; set; }

    [IndicatorParameter]
    public bool ShowPreMarketBackground { get; set; }

    // ───── Parameters — 02. Colors (NT GroupName "02. Colors") ─────────────
    // Each NT Stroke (brush + dash style + width + opacity) flattens to
    // colour / width / line-style parameters; fill-only strokes (NT only used
    // their Brush + Opacity) flatten to colour + opacity %. Defaults are the
    // exact NT SetDefaults values (Azure F0FFFF, DarkSlateGray 2F4F4F,
    // SlateGray 708090, MediumSlateBlue 7B68EE).

    [IndicatorParameter]
    public string TextFontFamily { get; set; } = "";

    [IndicatorParameter]
    public double TextFontSize { get; set; } = 10;

    [IndicatorParameter]
    public ChartColor TextColor { get; set; } = NTColors.White;

    [IndicatorParameter]
    public ChartColor PreMarketColor { get; set; } = new(0x2F, 0x4F, 0x4F);          // DarkSlateGray

    [IndicatorParameter]
    public double PreMarketWidth { get; set; } = 2;

    [IndicatorParameter]
    public LineStyle PreMarketStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor PremarketAreaColor { get; set; } = new(0x2F, 0x4F, 0x4F);      // DarkSlateGray

    [IndicatorParameter]
    public double PremarketAreaOpacity { get; set; }

    [IndicatorParameter]
    public ChartColor IbDevelopingColor { get; set; } = NTColors.DimGray;

    [IndicatorParameter]
    public double IbDevelopingWidth { get; set; } = 3;

    [IndicatorParameter]
    public LineStyle IbDevelopingStyle { get; set; } = LineStyle.Dashed;

    [IndicatorParameter]
    public ChartColor IbVirginColor { get; set; } = new(0xF0, 0xFF, 0xFF);           // Azure

    [IndicatorParameter]
    public double IbVirginWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle IbVirginStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor IbBrokenColor { get; set; } = NTColors.DimGray;

    [IndicatorParameter]
    public double IbBrokenWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle IbBrokenStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor IbMidColor { get; set; } = NTColors.DeepSkyBlue;

    [IndicatorParameter]
    public double IbMidWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle IbMidStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor IbAreaColor { get; set; } = new(0xF0, 0xFF, 0xFF);             // Azure

    [IndicatorParameter]
    public double IbAreaOpacity { get; set; }

    [IndicatorParameter]
    public ChartColor IbBuildingAreaColor { get; set; } = new(0x7B, 0x68, 0xEE);     // MediumSlateBlue

    [IndicatorParameter]
    public double IbBuildingAreaOpacity { get; set; }

    [IndicatorParameter]
    public ChartColor SessionHighColor { get; set; } = new(0xF0, 0xFF, 0xFF);        // Azure

    [IndicatorParameter]
    public double SessionHighWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle SessionHighStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor SessionLowColor { get; set; } = new(0xF0, 0xFF, 0xFF);         // Azure

    [IndicatorParameter]
    public double SessionLowWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle SessionLowStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor SessionOpenColor { get; set; } = new(0xF0, 0xFF, 0xFF);        // Azure

    [IndicatorParameter]
    public double SessionOpenWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle SessionOpenStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor OrDevelopingColor { get; set; } = NTColors.Gold;

    [IndicatorParameter]
    public double OrDevelopingWidth { get; set; } = 3;

    [IndicatorParameter]
    public LineStyle OrDevelopingStyle { get; set; } = LineStyle.Dashed;

    [IndicatorParameter]
    public ChartColor OrHiLoColor { get; set; } = NTColors.Gold;

    [IndicatorParameter]
    public double OrHiLoWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle OrHiLoStyle { get; set; } = LineStyle.Solid;

    [IndicatorParameter]
    public ChartColor OrMidColor { get; set; } = NTColors.Gold;

    [IndicatorParameter]
    public double OrMidWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle OrMidStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor OrBackgroundColor { get; set; } = NTColors.DarkOrange;

    [IndicatorParameter]
    public double OrBackgroundOpacity { get; set; }

    [IndicatorParameter]
    public ChartColor OrBuildingAreaColor { get; set; } = NTColors.DarkOrange;

    [IndicatorParameter]
    public double OrBuildingAreaOpacity { get; set; }

    // NT defaults the six extension strokes to Azure / Dot / 1 px at 80% opacity —
    // the 80% is folded into the colour's alpha (0xCC).

    [IndicatorParameter]
    public ChartColor Ib100ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib100ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib100ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor Ib200ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib200ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib200ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor Ib300ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib300ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib300ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor Ib400ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib400ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib400ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor Ib500ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib500ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib500ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor Ib600ExtensionColor { get; set; } = new(0xF0, 0xFF, 0xFF, 0xCC);

    [IndicatorParameter]
    public double Ib600ExtensionWidth { get; set; } = 1;

    [IndicatorParameter]
    public LineStyle Ib600ExtensionStyle { get; set; } = LineStyle.Dotted;

    [IndicatorParameter]
    public ChartColor StatisticsBackgroundColor { get; set; } = NTColors.DimGray;

    [IndicatorParameter]
    public double StatisticsBackgroundOpacity { get; set; } = 30;

    [IndicatorParameter]
    public ChartColor SessionBackgroundColor { get; set; } = new(0x70, 0x80, 0x90);  // SlateGray

    [IndicatorParameter]
    public double SessionBackgroundOpacity { get; set; } = 10;

    [IndicatorParameter]
    public ChartColor PreMarketBackgroundColor { get; set; } = new(0x70, 0x80, 0x90); // SlateGray

    [IndicatorParameter]
    public double PreMarketBackgroundOpacity { get; set; } = 10;

    // ───── Outputs (NT public Series<double> accessors) ────────────────────

    public ISeries<double> InitialBalanceHigh => Output(PlotInitialBalanceHigh);
    public ISeries<double> InitialBalanceMid  => Output(PlotInitialBalanceMid);
    public ISeries<double> InitialBalanceLow  => Output(PlotInitialBalanceLow);
    public ISeries<double> OpeningRangeHigh   => Output(PlotOpeningRangeHigh);
    public ISeries<double> OpeningRangeLow    => Output(PlotOpeningRangeLow);
    public ISeries<double> PreMarketHigh      => Output(PlotPreMarketHigh);
    public ISeries<double> PreMarketLow       => Output(PlotPreMarketLow);
    public ISeries<double> SessionHigh        => Output(PlotSessionHigh);
    public ISeries<double> SessionLow         => Output(PlotSessionLow);
    public ISeries<double> SignalState        => Output(PlotSignalState);

    private ISeries<double> Output(int i) => _plotWrappers[i] ??= new IndicatorOutputSeries(_plots[i], Bars);

    // ───── Diagnostics (read-only; consumed by tests) ──────────────────────

    /// <summary>Local series index of the 1-minute series, or -1 until resolved.</summary>
    public int MinuteSeriesIndex => _minuteSeriesIndex;

    /// <summary>The per-day session list (oldest first) — exposed for tests.</summary>
    internal IReadOnlyList<IbSession> SessionsForTest
    {
        get { lock (_gate) return _sessions.ToArray(); }
    }

    // ───── Session-window accessors (NT derived TimeSpans) ─────────────────
    // NT end-stamps its 1-minute bars, so every window shifts one minute:
    // SessionStartTime/IbOpenTime = start + 1 min; IbEndTime/OrEndTime =
    // start + duration (configured as duration - 1, then + 1 min).

    internal TimeSpan SessionStartTime => _sessionStart.Add(TimeSpan.FromMinutes(1));
    internal TimeSpan SessionEndTime   => _sessionEnd;
    internal TimeSpan PremarketStartTime => _preMarketStart;
    internal TimeSpan IbOpenTime => _ibOpenTime.Add(TimeSpan.FromMinutes(1));
    internal TimeSpan IbEndTime  => _ibEndTime.Add(TimeSpan.FromMinutes(1));
    internal TimeSpan OrEndTime  => _orEndTime.Add(TimeSpan.FromMinutes(1));

    /// <summary>NT <c>InSession</c> — inclusive time-of-day window, wrapping midnight when end &lt; start.</summary>
    private static bool InSession(DateTime dateTime, TimeSpan startTime, TimeSpan endTime)
    {
        if (endTime >= startTime)
            return dateTime.TimeOfDay >= startTime && dateTime.TimeOfDay <= endTime;   // e.g. 09:30-16:00
        return dateTime.TimeOfDay >= startTime || dateTime.TimeOfDay <= endTime;       // e.g. 21:00-07:00
    }

    // ───── Lifecycle ────────────────────────────────────────────────────────

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);

        // NT State.Configure.
        _sessionStart = SessionStart.ToTimeSpan();
        _sessionEnd = SessionEnd.ToTimeSpan();
        _preMarketStart = PremarketStart.ToTimeSpan();
        _ibOpenTime = _sessionStart;
        _ibEndTime = _ibOpenTime.Add(TimeSpan.FromMinutes(InitialBalanceMinutes - 1));
        _orEndTime = _ibOpenTime.Add(TimeSpan.FromMinutes(OpeningRangeMinutes - 1));

        ResetState();

        _tickSize = ctx.TickSize ?? _tickSize;

        // NT AddDataSeries(BarsPeriodType.Minute, 1) — all levels compute from
        // 1-minute bars so they are identical on every chart bar type. When the
        // primary series already is 1-minute (or finer) the primary IS the tape:
        // no secondary subscription. Otherwise the host loads venue 1-minute bars
        // (strategy hosting, charts, analyzer) — never a tick rebuild.
        _minuteSeriesIndex = -1;
        _minuteSpec = null;
        if (Bars.Spec is not TimeBarSpec primaryTime || primaryTime.Period > TimeSpan.FromMinutes(1))
        {
            _minuteSpec = new TimeBarSpec(Bars.Spec.InstrumentId, TimeSpan.FromMinutes(1));
            _ = ctx.AddDataSeries(_minuteSpec);
        }
    }

    private void ResetState()
    {
        lock (_gate)
        {
            _sessions.Clear();
            _renderSnapshot = Array.Empty<IbSession>();
        }
        _current = null;
        _preMarketSet = false;
        _preMarketStartTime = default;
        _preMarketHigh = 0;
        _preMarketLow = 0;
        _prevDate = DateTime.MinValue;
        _chartTime = default;
        _now = default;
        _lastMinuteEndUtc = DateTime.MinValue;
    }

    public override void OnDataLoaded(IIndicatorContext ctx) => RebuildFromSeries(ctx);

    public override void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) => RebuildFromSeries(ctx);

    /// <summary>
    /// Full (re)build: replay the backfilled 1-minute series against the primary
    /// series in chronological interleave — absorb every minute bar up to each
    /// primary bar's close, then stamp that primary bar's plot row (the NT
    /// BarsInProgress 1 / 0 interleave).
    /// </summary>
    private void RebuildFromSeries(IIndicatorContext ctx)
    {
        ResolveMinuteSeriesIndex(ctx);
        ResetState();
        for (int i = 0; i < PlotCount; i++) _plots[i].Clear();

        _tickSize = ctx.TickSize ?? _tickSize;

        IBarSeries primary = Bars;
        // When no separate 1-minute series exists (primary is already 1-minute or
        // finer) the primary series is the tape.
        IBarSeries minute = _minuteSeriesIndex >= 1 && _minuteSeriesIndex < ctx.SeriesCount
            ? ctx.Bars(_minuteSeriesIndex)
            : primary;

        int minuteCount = minute.Count;
        int m = 0;   // next minute bar to absorb, oldest-first offset

        for (int p = primary.Count - 1; p >= 0; p--)
        {
            Bar pb = primary[p];
            _now = pb.End;

            while (m < minuteCount)
            {
                Bar mb = minute[minuteCount - 1 - m];
                if (mb.EndUtc > pb.EndUtc) break;
                Bar prev = m > 0 ? minute[minuteCount - m] : mb;
                AbsorbMinuteBar(in mb, in prev);
                _lastMinuteEndUtc = mb.EndUtc;
                m++;
            }

            StampPrimaryRow();
        }

        // Minute bars newer than the last primary close (the forming chart bar).
        while (m < minuteCount)
        {
            Bar mb = minute[minuteCount - 1 - m];
            Bar prev = m > 0 ? minute[minuteCount - m] : mb;
            AbsorbMinuteBar(in mb, in prev);
            _lastMinuteEndUtc = mb.EndUtc;
            m++;
        }

        PublishRenderSnapshot();
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        ResolveMinuteSeriesIndex(ctx);

        int bip = ctx.BarsInProgress;
        if (bip == 0)
        {
            // NT BarsInProgress == 0: stamp the plot row for the chart bar. With no
            // separate 1-minute series the primary IS the tape — absorb it first.
            if (Bars.Count == 0) return;
            _now = Bars[0].End;
            if (_minuteSeriesIndex < 1)
            {
                Bar bar0 = Bars[0];
                if (bar0.EndUtc >= _lastMinuteEndUtc)
                {
                    Bar prev0 = Bars.Count > 1 ? Bars[1] : bar0;
                    AbsorbMinuteBar(in bar0, in prev0);
                    if (ctx.IsBarClosed) _lastMinuteEndUtc = bar0.EndUtc;
                    PublishRenderSnapshot();
                }
            }
            StampPrimaryRow();
            return;
        }

        if (bip != _minuteSeriesIndex || _minuteSeriesIndex < 1) return;

        IBarSeries minute = ctx.Bars(_minuteSeriesIndex);
        if (minute.Count == 0) return;

        Bar bar = minute[0];
        if (bar.EndUtc < _lastMinuteEndUtc) return;   // stale dispatch

        Bar prevBar = minute.Count > 1 ? minute[1] : bar;
        // NT runs the same OnBarUpdate body on every price change of the forming
        // 1-minute bar; the accumulation is monotone (min/max + latches), so
        // re-absorbing the same forming bar as it grows reproduces that exactly.
        AbsorbMinuteBar(in bar, in prevBar);
        if (ctx.IsBarClosed) _lastMinuteEndUtc = bar.EndUtc;

        PublishRenderSnapshot();
        RequestRepaint();
    }

    private void ResolveMinuteSeriesIndex(IIndicatorContext ctx)
    {
        if (_minuteSeriesIndex >= 0 || _minuteSpec is null) return;
        // Match by (instrument, period) — NOT full spec equality. The host stamps
        // the secondary with the PRIMARY's trading-hours template
        // (SecondarySpecTradingHours.InheritFromPrimary), so on RTH/ETH futures
        // the stored spec no longer .Equals the bare _minuteSpec. Same fix as
        // Market Profile / TduPivots.
        for (int i = 1; i < ctx.SeriesCount; i++)
        {
            if (ctx.Bars(i).Spec is TimeBarSpec t
                && t.Period == TimeSpan.FromMinutes(1)
                && string.Equals(t.InstrumentId, _minuteSpec.InstrumentId, StringComparison.OrdinalIgnoreCase))
            {
                _minuteSeriesIndex = i;
                return;
            }
        }
    }

    // ───── Core accumulation (NT OnBarUpdate, BarsInProgress == 1) ─────────

    private void AbsorbMinuteBar(in Bar bar, in Bar prevBar)
    {
        // NT Times[1][0] — its 1-minute bars are end-stamped.
        DateTime t = bar.End;
        _chartTime = t;

        // Pre-market accumulation (NT lines: premarket window vs midnight wrap).
        if (_preMarketStart < _sessionStart)
        {
            // e.g. 04:00 - 09:30
            if (t.TimeOfDay >= _preMarketStart && t.TimeOfDay < _sessionStart)
            {
                if (!_preMarketSet)
                {
                    _preMarketSet = true;
                    _preMarketStartTime = t;
                    _preMarketLow = bar.Low;
                    _preMarketHigh = bar.High;
                }
                _preMarketLow = Math.Min(_preMarketLow, bar.Low);
                _preMarketHigh = Math.Max(_preMarketHigh, bar.High);
            }
            else
            {
                _preMarketSet = false;
            }
        }
        else
        {
            // e.g. 18:00 - 09:30 (wraps midnight)
            if (t.TimeOfDay >= _preMarketStart)
            {
                // 18:00 - 23:59
                if (!_preMarketSet)
                {
                    _preMarketSet = true;
                    _preMarketStartTime = t;
                    _preMarketLow = bar.Low;
                    _preMarketHigh = bar.High;
                }
                _preMarketLow = Math.Min(_preMarketLow, bar.Low);
                _preMarketHigh = Math.Max(_preMarketHigh, bar.High);
            }
            else if (t.Date != _preMarketStartTime.Date && t.TimeOfDay < _sessionStart)
            {
                // 00:00 - 09:30
                _preMarketLow = Math.Min(_preMarketLow, bar.Low);
                _preMarketHigh = Math.Max(_preMarketHigh, bar.High);
            }
            else
            {
                _preMarketSet = false;
            }
        }

        // New calendar day → forget the current session (NT _prevDate latch).
        if (_prevDate.Date != t.Date)
        {
            _current = null;
            _prevDate = t;
        }

        if (InSession(t, SessionStartTime, SessionEndTime))
        {
            if (_current is null)
            {
                // Start of session.
                _preMarketSet = false;
                _current = new IbSession
                {
                    SessionHigh = bar.High,
                    SessionLow = bar.Low,
                    InitialBalanceHigh = bar.High,
                    InitialBalanceLow = bar.Low,
                    OpenPrice = bar.Open,
                    StartTime = t.Date.Add(SessionStartTime),
                    EndTime = t.Date.Add(SessionEndTime),
                    OpeningRangeEndTime = t.Date.Add(OrEndTime),
                    OpeningRangeHigh = bar.High,
                    OpeningRangeLow = bar.Low,
                    IBStarted = false,
                };
                lock (_gate) _sessions.Add(_current);
            }

            _current.SessionLow = Math.Min(_current.SessionLow, bar.Low);
            _current.SessionHigh = Math.Max(_current.SessionHigh, bar.High);
            _current.EndTime = t;

            if (InSession(t, IbOpenTime, IbEndTime))
            {
                if (t.TimeOfDay >= OrEndTime)
                {
                    _current.ORSet = true;
                    _current.IBStarted = true;
                }
                _current.InitialBalanceLow = Math.Min(_current.InitialBalanceLow, bar.Low);
                _current.InitialBalanceHigh = Math.Max(_current.InitialBalanceHigh, bar.High);
            }

            if (InSession(t, IbOpenTime, OrEndTime))
            {
                _current.OpeningRangeLow = Math.Min(_current.OpeningRangeLow, bar.Low);
                _current.OpeningRangeHigh = Math.Max(_current.OpeningRangeHigh, bar.High);
                _current.OpeningRangeEndTime = t;
            }

            if (t.TimeOfDay > IbEndTime)
            {
                _current.IBSet = true;
                if (!_current.IBHighBroken &&
                    prevBar.Low < _current.InitialBalanceHigh && bar.High > _current.InitialBalanceHigh)
                {
                    _current.IBHighBroken = true;
                    _current.IBHighBreakDateTime = t;
                }
                if (!_current.IBLowBroken &&
                    prevBar.High > _current.InitialBalanceLow && bar.Low < _current.InitialBalanceLow)
                {
                    _current.IBLowBroken = true;
                    _current.IBLowBreakDateTime = t;
                }
            }
        }
        else
        {
            _current = null;
        }
    }

    /// <summary>
    /// Write the ten plot values for the current primary bar from the running state —
    /// NT's BarsInProgress == 0 stamping. Appends a new row for a new bar and
    /// re-stamps the forming bar on intra-bar updates.
    /// </summary>
    private void StampPrimaryRow()
    {
        Span<double> v = stackalloc double[PlotCount];

        v[PlotPreMarketHigh] = _preMarketHigh;
        v[PlotPreMarketLow] = _preMarketLow;

        IbSession? cur = _current;
        if (cur is null)
        {
            v[PlotInitialBalanceHigh] = 0;
            v[PlotInitialBalanceMid] = 0;
            v[PlotInitialBalanceLow] = 0;
            v[PlotOpeningRangeHigh] = 0;
            v[PlotOpeningRangeLow] = 0;
            v[PlotSessionHigh] = 0;
            v[PlotSessionLow] = 0;
            v[PlotSignalState] = 0;
        }
        else
        {
            v[PlotInitialBalanceHigh] = cur.InitialBalanceHigh;
            v[PlotInitialBalanceMid] = (cur.InitialBalanceHigh + cur.InitialBalanceLow) / 2.0;
            v[PlotInitialBalanceLow] = cur.InitialBalanceLow;
            v[PlotOpeningRangeHigh] = cur.OpeningRangeHigh;
            v[PlotOpeningRangeLow] = cur.OpeningRangeLow;
            v[PlotSessionHigh] = cur.SessionHigh;
            v[PlotSessionLow] = cur.SessionLow;

            double signal = 0;
            if (InSession(_chartTime, SessionStartTime, SessionEndTime)) signal = 1;
            if (_chartTime.TimeOfDay >= OrEndTime) signal = 2;
            if (_chartTime.TimeOfDay >= IbEndTime) signal = 3;
            v[PlotSignalState] = signal;
        }

        int barCount = Bars.Count;
        for (int i = 0; i < PlotCount; i++)
        {
            var series = _plots[i];
            while (series.Count < barCount) series.Append(v[i]);
            if (series.Count > 0) series.UpdateLast(v[i]);
        }
    }

    private void PublishRenderSnapshot()
    {
        lock (_gate)
        {
            if (_renderSnapshot.Length != _sessions.Count)
                _renderSnapshot = _sessions.ToArray();
        }
    }

    private IbSession[] RenderSessions()
    {
        lock (_gate) return _renderSnapshot.Length == _sessions.Count ? _renderSnapshot : _sessions.ToArray();
    }

    // ───── Toolbar dropdown (IIndicatorToolbarMenu) + notifiers ────────────
    // 1-on-1 with NT addToolBar(): the chart-toolbar "IB" menu with the same
    // items in the same order. Render-only toggles fire RequestRepaint;
    // RE-CALCULATE asks the host to replay OnInit + OnDataLoaded (NT's F5).

    public string MenuTitle => "IB";

    public event Action? RepaintRequested;
    public void RequestRepaint() => RepaintRequested?.Invoke();

    public event Action? RecalculateRequested;
    private void RequestRecalculate() => RecalculateRequested?.Invoke();

    public event Action? MenuChanged;
    private void RaiseMenuChanged() => MenuChanged?.Invoke();

    private ToolbarCheckItem ToggleRepaint(string label, Func<bool> get, Action<bool> set)
        => new(label, get, v => { set(v); RequestRepaint(); });

    public IReadOnlyList<ToolbarMenuItem> BuildMenu() => new ToolbarMenuItem[]
    {
        ToggleRepaint("Enable: " + (ShowAll ? "ON" : "OFF"), () => ShowAll, v => { ShowAll = v; RaiseMenuChanged(); }),
        new ToolbarCommand("Display: " + Display, () =>
        {
            Display = Display == InitialBalanceDisplay.AllDays
                ? InitialBalanceDisplay.CurrentDayOnly
                : InitialBalanceDisplay.AllDays;
            RaiseMenuChanged();
            RequestRepaint();
        }),
        ToggleRepaint("Show pre-market: " + (ShowPremarket ? "ON" : "OFF"),
            () => ShowPremarket, v => { ShowPremarket = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show session high/low: " + (ShowSessionHighLow ? "ON" : "OFF"),
            () => ShowSessionHighLow, v => { ShowSessionHighLow = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Session Background: " + (ShowSessionBackground ? "ON" : "OFF"),
            () => ShowSessionBackground, v => { ShowSessionBackground = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Initial Balance: " + (ShowIB ? "ON" : "OFF"),
            () => ShowIB, v => { ShowIB = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Opening Range: " + (ShowOR ? "ON" : "OFF"),
            () => ShowOR, v => { ShowOR = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Todays IB Extensions: " + (ShowTodaysIBExtensions ? "ON" : "OFF"),
            () => ShowTodaysIBExtensions, v => { ShowTodaysIBExtensions = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Previous IB Extensions: " + (ShowPreviousIBExtensions ? "ON" : "OFF"),
            () => ShowPreviousIBExtensions, v => { ShowPreviousIBExtensions = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Price: " + (ShowPrice ? "ON" : "OFF"),
            () => ShowPrice, v => { ShowPrice = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Range: " + (ShowRange ? "ON" : "OFF"),
            () => ShowRange, v => { ShowRange = v; RaiseMenuChanged(); }),
        ToggleRepaint("Show Statistics: " + (ShowStatistics ? "ON" : "OFF"),
            () => ShowStatistics, v => { ShowStatistics = v; RaiseMenuChanged(); }),
        new ToolbarCommand("Show lines: " + LinesPlacement, () =>
        {
            LinesPlacement = LinesPlacement == InitialBalanceLinePlacement.Session
                ? InitialBalanceLinePlacement.RightEdge
                : InitialBalanceLinePlacement.Session;
            RaiseMenuChanged();
            RequestRepaint();
        }),
        new ToolbarSeparator(),
        new ToolbarCommand("RE-CALCULATE", RequestRecalculate),
    };
}
