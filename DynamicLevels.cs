using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Indicators.Builtin.Profiles;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.MarketProfiles.Volume;
using SabrTrader.Pipeline.OrderFlow;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Dynamic Levels — 1-on-1 port of ATAS <c>DynamicLevels</c>. Maintains a rolling order-flow
/// profile over the last <see cref="Days"/> sessions and plots its Point of Control plus the
/// Value-Area-High / Value-Area-Low channel, on a selectable <see cref="ProfileMetric"/>
/// (Volume / Delta / Bid / Ask / Ticks). As price trades, the POC and value area drift — adaptive
/// support/resistance.
///
/// <para>Reuses the shared <see cref="OrderFlowMetricProfile"/> accumulator and the
/// <see cref="VolumeValueAreaCalculator.Compute"/> value-area core. The window is maintained
/// incrementally (one partial profile per session, summed into a running merged profile; the
/// oldest is subtracted when it falls out of the window) so per-bar cost is O(levels-per-bar),
/// not a full rebuild. Order flow is read at the auto-injected Resolution; when none is available
/// the indicator falls back to bar-volume distribution (meaningful for the Volume metric only).</para>
/// </summary>
[RequiresOrderFlow]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DynamicLevels : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _poc = new();
    private readonly ChunkedSeries<double> _vah = new();
    private readonly ChunkedSeries<double> _val = new();
    private IndicatorOutputSeries? _pocW, _vahW, _valW;

    private readonly List<OrderFlowMetricProfile> _sessions = new();
    private readonly OrderFlowMetricProfile _merged = new();
    private OrderFlowMetricProfile? _current;
    private SessionResetTracker? _tracker;
    private bool _started;
    private double _tickSize;
    private bool _warnedNoOrderFlow;

    // OnEachTick forming-bar state. Bar-boundary work (tracker advance,
    // session roll + window eviction) runs exactly once per bar, on its FIRST
    // event (intra-bar or close). The forming bar's cumulative orderflow
    // snapshot is kept live in _current/_merged by subtract-prev + add-new
    // (exact integer inverse — no double-count, no clone); the close replaces
    // it with the final snapshot through the same mechanism. The O(levels)
    // value-area recompute is throttled intra-bar by _vaRefreshGate.
    private bool _activeBarSeen;
    private DateTime _activeBarStart;
    private bool _formingAbsorbed;
    private OrderFlowBar _formingSnapshot;
    private readonly IntraBarRefreshGate _vaRefreshGate = new();

    // Render snapshot.
    private double _lastPoc = double.NaN, _lastVah = double.NaN, _lastVal = double.NaN;

    public DynamicLevels(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // ATAS parity: the rolling POC / value-area band drifts tick-by-tick
        // with the developing bar. The OnBarClose default freezes the levels
        // one bar behind (the forming bar invisible to the profile).
        Calculate = CalculationMode.OnEachTick;
        OrderFlowResolution = OrderFlowResolution.Second;
        AddPlot(new Plot("POC", _poc, PlotStyle.Dots, NTColors.Orange, 2.0)
        { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "rolling point of control (highest-metric price)") });
        AddPlot(new Plot("Value Area High", _vah, PlotStyle.Line, NTColors.Maroon, 1.0)
        { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "upper edge of the rolling value area") });
        AddPlot(new Plot("Value Area Low", _val, PlotStyle.Line, NTColors.Maroon, 1.0)
        { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "lower edge of the rolling value area") });
    }

    [IndicatorParameter]
    public int Days { get; set; } = 20;

    [IndicatorParameter]
    public ProfileMetric Metric { get; set; } = ProfileMetric.Volume;

    [IndicatorParameter]
    public double ValueAreaPercent { get; set; } = 70;

    public ISeries<double> Poc => _pocW ??= new IndicatorOutputSeries(_poc, Bars);
    public ISeries<double> ValueAreaHigh => _vahW ??= new IndicatorOutputSeries(_vah, Bars);
    public ISeries<double> ValueAreaLow => _valW ??= new IndicatorOutputSeries(_val, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Days < 1) throw new ArgumentOutOfRangeException(nameof(Days));
        if (ValueAreaPercent is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(ValueAreaPercent));
        _tickSize = ctx.TickSize is > 0 ? ctx.TickSize!.Value : 0.25;
        _tracker = SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: true,
            start: new TimeOnly(0, 0), end: new TimeOnly(0, 0));
        _sessions.Clear();
        _merged.Clear();
        _current = null;
        _started = false;
        _warnedNoOrderFlow = false;
        _activeBarSeen = false;
        _formingAbsorbed = false;
        _lastPoc = _lastVah = _lastVal = double.NaN;
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
        _sessions.Clear();
        _merged.Clear();
        _current = null;
        _started = false;
        _warnedNoOrderFlow = false;
        _activeBarSeen = false;
        _formingAbsorbed = false;
        _lastPoc = _lastVah = _lastVal = double.NaN;
    }

    private void Consume(in Bar bar, in OrderFlowBar ofb, IIndicatorContext ctx, bool isClosed, int rowCount)
    {
        // ── Bar boundary, exactly once per bar (first event wins). The
        // defensive forming rollback runs BEFORE the session roll so a stale
        // forming snapshot (a missed close, which the dispatch contract rules
        // out but a foreign host might not) leaves the OLD _current it was
        // absorbed into.
        if (!_activeBarSeen || bar.StartUtc != _activeBarStart)
        {
            if (_formingAbsorbed)
            {
                _current!.SubtractOrderFlowBar(in _formingSnapshot);
                _merged.SubtractOrderFlowBar(in _formingSnapshot);
                _formingAbsorbed = false;
            }
            _activeBarSeen = true;
            _activeBarStart = bar.StartUtc;

            bool newSession = _tracker!.OnBar(bar.Start, bar.End);
            if (!_started) { _started = true; StartNewSession(); }
            else if (newSession) { StartNewSession(); EvictBeyondWindow(); }
        }

        // ── Absorb. The forming bar's orderflow snapshot is CUMULATIVE, so a
        // re-absorb first removes what the previous tick absorbed (exact
        // inverse) and then adds the fresh snapshot — profile stays live
        // without double-counting. A close finalizes through the same path.
        if (_formingAbsorbed)
        {
            _current!.SubtractOrderFlowBar(in _formingSnapshot);
            _merged.SubtractOrderFlowBar(in _formingSnapshot);
            _formingAbsorbed = false;
        }
        if (ofb.LevelCount > 0)
        {
            _current!.AddOrderFlowBar(in ofb);
            _merged.AddOrderFlowBar(in ofb);
            if (!isClosed)
            {
                _formingSnapshot = ofb;
                _formingAbsorbed = true;
            }
        }
        else if (isClosed)
        {
            // Bar-volume fallback only lands on CLOSE: it spreads bar volume
            // across the final H-L range and has no exact inverse, so the
            // forming bar of an OF-less source updates per close (the data is
            // bar-coarse anyway — nothing finer exists to show).
            if (Metric != ProfileMetric.Volume && !_warnedNoOrderFlow)
            {
                ctx.LogWarning($"DynamicLevels: no order flow available; the {Metric} metric degrades to Volume.");
                _warnedNoOrderFlow = true;
            }
            _current!.AddBarFallback(in bar, _tickSize);
            _merged.AddBarFallback(in bar, _tickSize);
        }

        // ── Recompute the value area: always on close (the commit point),
        // throttled intra-bar (O(levels) over the merged window per call).
        if (isClosed || _vaRefreshGate.ShouldRefresh(force: rowCount > _poc.Count))
        {
            var va = _merged.ValueArea(Metric, ValueAreaPercent);
            if (va.HasValue)
            {
                _lastPoc = va.Poc; _lastVah = va.ValueAreaHigh; _lastVal = va.ValueAreaLow;
            }
        }

        Upsert(_poc, _lastPoc, rowCount);
        Upsert(_vah, _lastVah, rowCount);
        Upsert(_val, _lastVal, rowCount);
    }

    /// <summary>Grow the output to one row per bar, then overwrite the current
    /// bar's row in place (append-on-first-event / update-after — the
    /// platform's OnEachTick output convention).</summary>
    private static void Upsert(ChunkedSeries<double> series, double value, int rowCount)
    {
        while (series.Count < rowCount) series.Append(value);
        series.UpdateLast(value);
    }

    private void StartNewSession()
    {
        _current = new OrderFlowMetricProfile();
        _sessions.Add(_current);
    }

    private void EvictBeyondWindow()
    {
        while (_sessions.Count > Days)
        {
            _merged.SubtractFrom(_sessions[0]);
            _sessions.RemoveAt(0);
        }
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (double.IsNaN(_lastPoc)) return;
        IChartRenderer r = ctx.Renderer;
        ChartViewport vp = ctx.Viewport;

        // Shade the value-area band across the panel.
        if (!double.IsNaN(_lastVah) && !double.IsNaN(_lastVal) && Plots[1].Visible)
        {
            double yH = vp.PriceToPixelY(_lastVah);
            double yL = vp.PriceToPixelY(_lastVal);
            double top = Math.Min(yH, yL), h = Math.Abs(yL - yH);
            if (h >= 1)
            {
                var c = Plots[1].Color;
                r.FillRect(0, top, vp.PlotWidthPx, h, new ChartColor(c.R, c.G, c.B, 0x1E));
            }
            LevelLabel.Draw(r, vp, ctx.PlotArea, "VAH", _lastVah, Plots[1].Color, 11);
            LevelLabel.Draw(r, vp, ctx.PlotArea, "VAL", _lastVal, Plots[2].Color, 11);
        }
        if (Plots[0].Visible)
            LevelLabel.Draw(r, vp, ctx.PlotArea, "POC", _lastPoc, Plots[0].Color, 11);
    }
}
