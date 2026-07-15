using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Indicators.Builtin.Profiles;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.OrderFlow;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Time scope a <see cref="MaxLevels"/> profile is reset over (ATAS Period).</summary>
public enum MaxLevelsPeriod { CurrentDay = 0, LastDay = 1, CurrentWeek = 2, CurrentMonth = 3, Contract = 4 }

/// <summary>
/// Max Levels — 1-on-1 port of ATAS <c>MaxLevels</c>. Tracks the single price level carrying the
/// maximum of a selectable order-flow <see cref="ProfileMetric"/> (Volume / Delta / Bid / Ask /
/// Ticks) over a chosen <see cref="Period"/> (current/last day, current week/month, or the whole
/// contract) and plots it as a horizontal level.
///
/// <para>Reuses the shared <see cref="OrderFlowMetricProfile"/> accumulator; the profile is reset on
/// each period boundary (the session iterator supplies day boundaries, calendar keys supply week /
/// month). Order flow is read at the auto-injected Resolution, with a bar-volume fallback (Volume
/// metric only) when none is available.</para>
/// </summary>
[IndicatorDescription("Max Levels: the price level with the maximum order-flow metric (Volume/Delta/Bid/Ask/Ticks) over a period (current/last day, current week/month, or contract), drawn as a horizontal level.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[RequiresOrderFlow]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MaxLevels : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _level = new();
    private IndicatorOutputSeries? _levelW;

    private OrderFlowMetricProfile _current = new();
    private OrderFlowMetricProfile? _lastCompleted;
    private SessionResetTracker? _tracker;
    private bool _started;
    private DateTime _curWeek;
    private (int Year, int Month) _curMonth;
    private double _tickSize;
    private bool _warnedNoOrderFlow;

    // OnEachTick forming-bar state — same model as DynamicLevels: bar-boundary
    // work once per bar (first event wins); the forming bar's cumulative OF
    // snapshot is kept live in _current via subtract-prev + add-new (exact
    // integer inverse); the O(levels) argmax recompute is throttled intra-bar.
    private bool _activeBarSeen;
    private DateTime _activeBarStart;
    private bool _formingAbsorbed;
    private OrderFlowBar _formingSnapshot;
    private readonly IntraBarRefreshGate _levelRefreshGate = new();

    private double _lastLevel = double.NaN;

    public MaxLevels(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // ATAS parity: the max level drifts tick-by-tick with the developing
        // bar. The OnBarClose default freezes it one bar behind.
        Calculate = CalculationMode.OnEachTick;
        OrderFlowResolution = OrderFlowResolution.Second;
        AddPlot(new Plot("Max Level", _level, PlotStyle.Step, NTColors.Gold, 1.5)
        { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "price level holding the maximum metric over the period") });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Time scope the profile is reset over.",
        Group = "01. Parameters", Order = 1)]
    public MaxLevelsPeriod Period { get; set; } = MaxLevelsPeriod.CurrentDay;

    [IndicatorParameter(DisplayName = "Metric", Description = "Order-flow figure to maximise: Volume, Delta, Bid, Ask, or Ticks.",
        Group = "01. Parameters", Order = 2)]
    public ProfileMetric Metric { get; set; } = ProfileMetric.Volume;

    public ISeries<double> MaxLevel => _levelW ??= new IndicatorOutputSeries(_level, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _tickSize = ctx.TickSize is > 0 ? ctx.TickSize!.Value : 0.25;
        _tracker = SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: true,
            start: new TimeOnly(0, 0), end: new TimeOnly(0, 0));
        _current = new OrderFlowMetricProfile();
        _lastCompleted = null;
        _started = false;
        _warnedNoOrderFlow = false;
        _activeBarSeen = false;
        _formingAbsorbed = false;
        _lastLevel = double.NaN;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        var of = ctx.OrderFlow(0);
        int n = bars.Count;
        for (int i = 0; i < n; i++)
        {
            int ago = n - 1 - i;
            var bar = bars[ago];
            var ofb = ago < of.Count ? of[ago] : default;
            Consume(bar, in ofb, ctx, isClosed: true, rowCount: i + 1);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        var of = ctx.OrderFlow(0);
        var ofb = of.Count > 0 ? of[0] : default;
        Consume(Bars[0], in ofb, ctx, ctx.IsBarClosed, rowCount: Bars.Count);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _current = new OrderFlowMetricProfile();
        _lastCompleted = null;
        _started = false;
        _warnedNoOrderFlow = false;
        _activeBarSeen = false;
        _formingAbsorbed = false;
        _lastLevel = double.NaN;
    }

    private void Consume(in Bar bar, in OrderFlowBar ofb, IIndicatorContext ctx, bool isClosed, int rowCount)
    {
        // ── Bar boundary, exactly once per bar (first event wins). A stale
        // forming snapshot is rolled back BEFORE a period roll so it leaves
        // the profile it was absorbed into.
        if (!_activeBarSeen || bar.StartUtc != _activeBarStart)
        {
            if (_formingAbsorbed)
            {
                _current.SubtractOrderFlowBar(in _formingSnapshot);
                _formingAbsorbed = false;
            }
            _activeBarSeen = true;
            _activeBarStart = bar.StartUtc;

            bool newSession = _tracker!.OnBar(bar.StartUtc, bar.EndUtc);
            var week = WeekStart(bar.StartUtc);
            var month = (bar.StartUtc.Year, bar.StartUtc.Month);

            if (!_started)
            {
                _started = true;
                _curWeek = week;
                _curMonth = month;
            }
            else if (IsBoundary(newSession, week, month))
            {
                _lastCompleted = _current;
                _current = new OrderFlowMetricProfile();
            }
            _curWeek = week;
            _curMonth = month;
        }

        // ── Absorb: subtract the previous forming snapshot (exact inverse),
        // add the fresh one; a close finalizes through the same path. The
        // bar-volume fallback has no exact inverse → close-only.
        if (_formingAbsorbed)
        {
            _current.SubtractOrderFlowBar(in _formingSnapshot);
            _formingAbsorbed = false;
        }
        if (ofb.LevelCount > 0)
        {
            _current.AddOrderFlowBar(in ofb);
            if (!isClosed)
            {
                _formingSnapshot = ofb;
                _formingAbsorbed = true;
            }
        }
        else if (isClosed)
        {
            if (Metric != ProfileMetric.Volume && !_warnedNoOrderFlow)
            {
                ctx.LogWarning($"MaxLevels: no order flow available; the {Metric} metric degrades to Volume.");
                _warnedNoOrderFlow = true;
            }
            _current.AddBarFallback(in bar, _tickSize);
        }

        // ── Recompute the level: always on close, throttled intra-bar
        // (O(levels) argmax over the period profile per call).
        if (isClosed || _levelRefreshGate.ShouldRefresh(force: rowCount > _level.Count))
        {
            var profile = Period == MaxLevelsPeriod.LastDay ? _lastCompleted : _current;
            double level = profile is { IsEmpty: false } ? profile.MaxLevel(Metric) : double.NaN;
            if (!double.IsNaN(level)) _lastLevel = level;
        }

        while (_level.Count < rowCount) _level.Append(_lastLevel);
        _level.UpdateLast(_lastLevel);
    }

    private bool IsBoundary(bool newSession, DateTime week, (int, int) month) => Period switch
    {
        MaxLevelsPeriod.CurrentDay or MaxLevelsPeriod.LastDay => newSession,
        MaxLevelsPeriod.CurrentWeek => week != _curWeek,
        MaxLevelsPeriod.CurrentMonth => month != _curMonth,
        _ => false,   // Contract — never resets
    };

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (double.IsNaN(_lastLevel) || !Plots[0].Visible) return;
        LevelLabel.Draw(ctx.Renderer, ctx.Viewport, ctx.PlotArea, "Max", _lastLevel, Plots[0].Color, 11);
    }

    private static DateTime WeekStart(DateTime t)
    {
        int diff = ((int)t.DayOfWeek + 6) % 7;   // Mon=0 … Sun=6
        return t.Date.AddDays(-diff);
    }
}
