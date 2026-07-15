using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// What the VWAP cumulator is anchored to — one dropdown replacing the earlier
/// confusing Session Anchored + Use Instrument Trading Hours checkbox pair.
/// </summary>
public enum VwapAnchor
{
    /// <summary>Resets at the data series' Trading Hours template session open
    /// (holiday/DST-correct); NaN outside the session.</summary>
    InstrumentSession,

    /// <summary>Resets at the manual Session Start / Session End window;
    /// NaN outside the window.</summary>
    CustomSession,

    /// <summary>Rolling window of the last Rolling Period bars (0 = cumulative).</summary>
    Rolling,

    /// <summary>Cumulative from the first loaded bar — never resets.</summary>
    Cumulative,
}

/// <summary>
/// Volume-Weighted Average Price + optional volume-weighted standard-deviation bands.
///
/// NT TDUVWAP parity. Price = the 4-point OHLC midpoint
/// <c>(open + high + low + close) / 4</c> (NOT the classic (H+L+C)/3 typical);
/// <c>VWAP = Σ(price_i × volume_i) / Σ(volume_i)</c> per anchor window.
///
/// Bands: <c>VWAP ± k · σ_vw</c> where σ_vw is NT's 6-point weighted standard deviation
/// <c>sqrt(Σ vol·(O²+H²+L²+C²+2·mean2²+2·mean1²)/8 / Σ vol − vwap²)</c>
/// (mean1=(H+L)/2, mean2=(O+C)/2). FOUR configurable multipliers (default 1σ/2σ/3σ/4σ);
/// set a multiplier to 0 to hide that band. The VWAP renders as a slope-coloured Dot
/// (rising/falling) and the bands as dashed lines.
///
/// Anchor modes (<see cref="Anchor"/> dropdown):
///   <list type="bullet">
///     <item><b>InstrumentSession</b> (default) — VWAP resets at the data series'
///       Trading Hours template session open (holiday/DST-correct); NaN outside
///       the session.</item>
///     <item><b>CustomSession</b> — resets at the manual
///       <see cref="SessionStart"/>–<see cref="SessionEnd"/> window (chart-display
///       timezone); NaN outside the window.</item>
///     <item><b>Rolling</b> — rolling window of the last <see cref="RollingPeriod"/>
///       bars (0 = cumulative).</item>
///     <item><b>Cumulative</b> — from the first bar in the series, never resets.</item>
///   </list>
/// </summary>
[IndicatorDescription("Volume-Weighted Average Price with optional 1-sigma and 2-sigma volume-weighted standard-deviation bands. Supports session-anchored, rolling, or cumulative anchors.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Vwap : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _upper1 = new();
    private readonly ChunkedSeries<double> _lower1 = new();
    private readonly ChunkedSeries<double> _upper2 = new();
    private readonly ChunkedSeries<double> _lower2 = new();
    private readonly ChunkedSeries<double> _upper3 = new();
    private readonly ChunkedSeries<double> _lower3 = new();
    private readonly ChunkedSeries<double> _upper4 = new();
    private readonly ChunkedSeries<double> _lower4 = new();
    // Per-bar colour for the centre VWAP dots (NT PlotBrushes): green when VWAP rose
    // versus the prior bar, red when it fell. Appended in lockstep with _output —
    // including the NaN out-of-session bars — so the colour series never drifts.
    private readonly ChunkedSeries<ChartColor> _colors = new();
    private IndicatorOutputSeries? _outputAsBarSourced, _u1W, _l1W, _u2W, _l2W, _u3W, _l3W, _u4W, _l4W;

    private double _cumPV;              // cumulative price × volume
    private double _cumPV2;             // cumulative price² × volume (for variance)
    private long   _cumVol;             // cumulative volume
    private RollingSum? _rollingPV;
    private RollingSum? _rollingPV2;
    private RollingSum? _rollingVol;
    // Session cursor (built only in the session anchor modes). InstrumentSession resets at
    // the SELECTED data-series template's session open and flags out-of-session bars;
    // CustomSession uses the manual Session Start/End window.
    private SessionResetTracker? _tracker;

    public Vwap(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        // NT TDUVWAP plot styling: VWAP is a LimeGreen Dot; the four band pairs are
        // dashed Violet / DeepSkyBlue / Yellow / Peru (matching the NT SetDefaults strokes).
        AddPlot(new Plot("VWAP",   _output, PlotStyle.Dots,   NTColors.LimeGreen,  2.0) { PerBarColors = _colors, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #1 Up",   _upper1, PlotStyle.Dashed, NtViolet,      1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #1 Down", _lower1, PlotStyle.Dashed, NtViolet,      1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #2 Up",   _upper2, PlotStyle.Dashed, NTColors.DeepSkyBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #2 Down", _lower2, PlotStyle.Dashed, NTColors.DeepSkyBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #3 Up",   _upper3, PlotStyle.Dashed, NTColors.Yellow, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #3 Down", _lower3, PlotStyle.Dashed, NTColors.Yellow, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #4 Up",   _upper4, PlotStyle.Dashed, NtPeru,        1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Band #4 Down", _lower4, PlotStyle.Dashed, NtPeru,        1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    // NT brush colours not present in NTColors.
    private static readonly ChartColor NtViolet = new(0xEE, 0x82, 0xEE);
    private static readonly ChartColor NtPeru   = new(0xCD, 0x85, 0x3F);

    [IndicatorParameter(DisplayName = "Rolling Period",
        Description = "Only used when Anchor is Rolling. N > 0 = rolling window of the last N bars; 0 = cumulative from series start.",
        MinValue = 0, MaxValue = 1000000, Step = 1)]
    public int RollingPeriod { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Band1 σ",
        Description = "First band's standard-deviation multiplier. 0 = hide. Typical: 1.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band1StdDev { get; set; } = 1.0;

    [IndicatorParameter(DisplayName = "Band2 σ",
        Description = "Second band's standard-deviation multiplier. 0 = hide. Typical: 2.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band2StdDev { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Band3 σ",
        Description = "Third band's standard-deviation multiplier. 0 = hide. NT default: 3.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band3StdDev { get; set; } = 3.0;

    [IndicatorParameter(DisplayName = "Band4 σ",
        Description = "Fourth band's standard-deviation multiplier. 0 = hide. NT default: 4.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band4StdDev { get; set; } = 4.0;

    [IndicatorParameter(DisplayName = "Band Fill Opacity %",
        Description = "Shades the area inside each σ band. 0 = no fill (lines only). The fill reuses each band's line colour (RGB) at this opacity; the 2σ band paints first so the centre reads denser. Typical: 8–15.",
        MinValue = 0, MaxValue = 100, Step = 1)]
    public int BandFillOpacity { get; set; } = 12;

    // ─── Colors ──────────────────────────────────────────────────────────────
    // The centre VWAP dots are coloured per bar by slope (rising/falling). Both
    // colours are user-selectable; the bands keep their own fixed colours.

    [IndicatorParameter(DisplayName = "Rising", Group = "Colors", Order = 1,
        Description = "Colour of the VWAP dots on bars where VWAP rose versus the prior bar.")]
    public ChartColor RisingColor { get; set; } = NTColors.ForestGreen;

    [IndicatorParameter(DisplayName = "Falling", Group = "Colors", Order = 2,
        Description = "Colour of the VWAP dots on bars where VWAP fell versus the prior bar.")]
    public ChartColor FallingColor { get; set; } = NTColors.Crimson;

    [IndicatorParameter(DisplayName = "Anchor",
        Description = "What VWAP is anchored to. InstrumentSession (default): resets at the data series' Trading Hours template session open — holiday/DST-correct, the standard daily-VWAP pattern. CustomSession: resets at the manual Session Start/End window below. Rolling: rolling window of Rolling Period bars. Cumulative: from the first loaded bar, never resets.",
        Group = "Session")]
    public VwapAnchor Anchor { get; set; } = VwapAnchor.InstrumentSession;

    [IndicatorParameter(DisplayName = "Session Start",
        Description = "Time-of-day at which a new session opens (chart-display timezone). Only used when Anchor is CustomSession. Defaults to 09:30 (US equities/futures regular session).",
        Group = "Session")]
    public TimeOnly SessionStart { get; set; } = new(9, 30);

    [IndicatorParameter(DisplayName = "Session End",
        Description = "Time-of-day at which the session closes (chart-display timezone). Only used when Anchor is CustomSession. Defaults to 16:00. Set Start > End for overnight sessions; Start == End disables the window (24/7, rolls over at calendar midnight).",
        Group = "Session")]
    public TimeOnly SessionEnd { get; set; } = new(16, 0);

    public ISeries<double> Output  => _outputAsBarSourced ??= new IndicatorOutputSeries(_output, Bars);
    public ISeries<double> Upper1  => _u1W ??= new IndicatorOutputSeries(_upper1, Bars);
    public ISeries<double> Lower1  => _l1W ??= new IndicatorOutputSeries(_lower1, Bars);
    public ISeries<double> Upper2  => _u2W ??= new IndicatorOutputSeries(_upper2, Bars);
    public ISeries<double> Lower2  => _l2W ??= new IndicatorOutputSeries(_lower2, Bars);
    public ISeries<double> Upper3  => _u3W ??= new IndicatorOutputSeries(_upper3, Bars);
    public ISeries<double> Lower3  => _l3W ??= new IndicatorOutputSeries(_lower3, Bars);
    public ISeries<double> Upper4  => _u4W ??= new IndicatorOutputSeries(_upper4, Bars);
    public ISeries<double> Lower4  => _l4W ??= new IndicatorOutputSeries(_lower4, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RollingPeriod < 0) throw new ArgumentOutOfRangeException(nameof(RollingPeriod));
        if (Band1StdDev < 0)   throw new ArgumentOutOfRangeException(nameof(Band1StdDev));
        if (Band2StdDev < 0)   throw new ArgumentOutOfRangeException(nameof(Band2StdDev));
        if (Band3StdDev < 0)   throw new ArgumentOutOfRangeException(nameof(Band3StdDev));
        if (Band4StdDev < 0)   throw new ArgumentOutOfRangeException(nameof(Band4StdDev));
        _cumPV = 0; _cumPV2 = 0; _cumVol = 0;
        // Session anchor modes: build the reset cursor from the SELECTED data-series
        // template (InstrumentSession) or the manual window (CustomSession). The tracker
        // is what makes VWAP reset on the same session the user picked in the data series.
        _tracker = Anchor switch
        {
            VwapAnchor.InstrumentSession => SessionResetTracker.Create(
                ctx.CreateSessionIterator(), sessionAware: false, default, default),
            VwapAnchor.CustomSession => SessionResetTracker.Create(
                null, sessionAware: true, SessionStart, SessionEnd),
            _ => null,
        };
        // The session anchors own the cumulator (reset on each session-open bar), so they
        // disable the rolling window entirely. Rolling anchor uses the window when the
        // period is positive; Cumulative (and Rolling with period 0) accumulates forever.
        if (_tracker is null && Anchor == VwapAnchor.Rolling && RollingPeriod > 0)
        {
            _rollingPV  = new RollingSum(RollingPeriod);
            _rollingPV2 = new RollingSum(RollingPeriod);
            _rollingVol = new RollingSum(RollingPeriod);
        }
        else
        {
            _rollingPV = null; _rollingPV2 = null; _rollingVol = null;
        }
        // σ = 0 means "no band" — force those plots hidden so the legend stays clean and
        // nothing is drawn. We only ever force OFF here, NEVER on: when σ > 0 we leave
        // Visible exactly as the user left it (the codec restores per-plot visibility from
        // the workspace/template BEFORE OnInit runs). Forcing it true here would clobber
        // that restore every init, which is why unchecking a band used to "come back" on
        // the next edit/reload.
        if (Band1StdDev <= 0) { Plots[1].Visible = false; Plots[2].Visible = false; }
        if (Band2StdDev <= 0) { Plots[3].Visible = false; Plots[4].Visible = false; }
        if (Band3StdDev <= 0) { Plots[5].Visible = false; Plots[6].Visible = false; }
        if (Band4StdDev <= 0) { Plots[7].Visible = false; Plots[8].Visible = false; }
        // A disabled band emits only NaN — keep it out of the data box too (a blank
        // row for a band the user turned off is noise). Derived purely from the σ
        // multiplier, so it is deterministic across re-inits (unlike Visible, which
        // the codec restores from the saved workspace).
        Plots[1].IncludeInDataWindow = Plots[2].IncludeInDataWindow = Band1StdDev > 0;
        Plots[3].IncludeInDataWindow = Plots[4].IncludeInDataWindow = Band2StdDev > 0;
        Plots[5].IncludeInDataWindow = Plots[6].IncludeInDataWindow = Band3StdDev > 0;
        Plots[7].IncludeInDataWindow = Plots[8].IncludeInDataWindow = Band4StdDev > 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            Consume(bars[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        Consume(bars[0]);
    }

    private void Consume(Bar bar)
    {
        // NT TDUVWAP price = 4-point OHLC midpoint (NOT the classic (H+L+C)/3 typical):
        //   mean1 = (H+L)/2, mean2 = (O+C)/2, price = (mean1+mean2)/2 = (O+H+L+C)/4.
        // The σ accumulator is the 6-point weighted mean-of-squares NT uses
        //   (O²+H²+L²+C²+2·mean2²+2·mean1²)/8 · volume.
        // NT subtracts the session's first-bar open before squaring for numeric stability;
        // that offset is provably shift-invariant for both the mean and this variance, so we
        // accumulate raw OHLC and get bit-equivalent VWAP / σ without the offset bookkeeping.
        double o = bar.Open, h = bar.High, l = bar.Low, c = bar.Close;
        double mean1 = 0.5 * (h + l);
        double mean2 = 0.5 * (o + c);
        double typical = 0.5 * (mean1 + mean2);
        double vol = bar.Volume;
        double pv  = typical * vol;
        double pv2 = vol * (o * o + h * h + l * l + c * c + 2 * mean2 * mean2 + 2 * mean1 * mean1) / 8.0;

        // Session-anchored path: reset the cumulator on every session-open bar; emit NaN
        // for out-of-session bars so the chart shows a clean break (instead of dragging a
        // stale value across the overnight gap).
        if (_tracker is not null)
        {
            bool newSession = _tracker.OnBar(bar.StartUtc, bar.EndUtc);
            if (!_tracker.InSession)
            {
                AppendNaN();                // out-of-session → clean break
                return;
            }
            if (newSession)
            {
                _cumPV = 0; _cumPV2 = 0; _cumVol = 0;
            }
            _cumPV  += pv;
            _cumPV2 += pv2;
            _cumVol += bar.Volume;
            EmitFromSums(_cumPV, _cumPV2, _cumVol);
            return;
        }

        double volSum, pvSum, pv2Sum;
        if (_rollingPV is null)
        {
            _cumPV  += pv;
            _cumPV2 += pv2;
            _cumVol += bar.Volume;
            volSum = _cumVol; pvSum = _cumPV; pv2Sum = _cumPV2;
        }
        else
        {
            _rollingPV.Push(pv);
            _rollingPV2!.Push(pv2);
            _rollingVol!.Push(vol);
            volSum = _rollingVol.Sum; pvSum = _rollingPV.Sum; pv2Sum = _rollingPV2.Sum;
        }
        EmitFromSums(pvSum, pv2Sum, volSum);
    }

    /// <summary>
    /// Push VWAP + bands derived from running (Σ pv, Σ pv², Σ vol). Shared between the
    /// session-anchored and rolling/cumulative paths so the math + edge-cases (zero volume,
    /// negative-variance noise guard) stay in one place.
    /// </summary>
    private void EmitFromSums(double pvSum, double pv2Sum, double volSum)
    {
        double vwap = volSum > 0 ? pvSum / volSum : 0;
        // Slope colour vs the prior emitted VWAP. A NaN prior (first bar of a session,
        // or right after an out-of-session gap) has no slope → default to rising.
        double prev = _output.Count > 0 ? _output[0] : double.NaN;
        _output.Append(vwap);
        _colors.Append(!double.IsNaN(prev) && vwap < prev ? FallingColor : RisingColor);
        double variance = volSum > 0 ? pv2Sum / volSum - vwap * vwap : 0;
        if (variance < 0) variance = 0;
        double sigma = Math.Sqrt(variance);
        EmitBand(_upper1, _lower1, Band1StdDev, vwap, sigma);
        EmitBand(_upper2, _lower2, Band2StdDev, vwap, sigma);
        EmitBand(_upper3, _lower3, Band3StdDev, vwap, sigma);
        EmitBand(_upper4, _lower4, Band4StdDev, vwap, sigma);
    }

    private static void EmitBand(ChunkedSeries<double> upper, ChunkedSeries<double> lower,
        double mult, double vwap, double sigma)
    {
        if (mult > 0) { upper.Append(vwap + mult * sigma); lower.Append(vwap - mult * sigma); }
        else          { upper.Append(double.NaN);          lower.Append(double.NaN); }
    }

    private void AppendNaN()
    {
        _output.Append(double.NaN);
        _colors.Append(RisingColor);   // dot is skipped (NaN) — placeholder keeps lockstep
        _upper1.Append(double.NaN); _lower1.Append(double.NaN);
        _upper2.Append(double.NaN); _lower2.Append(double.NaN);
        _upper3.Append(double.NaN); _lower3.Append(double.NaN);
        _upper4.Append(double.NaN); _lower4.Append(double.NaN);
    }

    // Paint the band fills BEHIND the candles so the σ rings read as a faint background
    // tint (the band edge lines are still drawn opaquely by the standard PlotRenderPass on
    // top). BelowBars = candles + lines paint over the fill, NT/typical-VWAP appearance.
    public CustomRenderLayer Layer => CustomRenderLayer.BelowBars;

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (BandFillOpacity <= 0) return;
        int total = _output.Count;
        if (total == 0) return;

        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        byte alpha = (byte)Math.Clamp((int)Math.Round(BandFillOpacity / 100.0 * 255.0), 0, 255);

        // Fill ANNULAR zones between ADJACENT bands so every shaded region is
        // bounded on BOTH sides by a band line — the fill hugs the lines with no
        // spill outside and no overlap inside. The previous approach shaded each
        // band as a full −kσ..+kσ ribbon and stacked all four, so the outer
        // ribbons bled past the inner band lines ("fill not between the bands,
        // also outside and inside"). Collect the ACTIVE bands (σ > 0 AND at least
        // one edge visible) innermost→outermost, then fill:
        //   • innermost band → its own lower..upper (the ±kσ core), and
        //   • each next band → two rings: prevUpper..curUpper (top) and
        //                      curLower..prevLower (bottom).
        Span<int> active = stackalloc int[4];
        int n = 0;
        if (Band1StdDev > 0 && (Plots[1].Visible || Plots[2].Visible)) active[n++] = 0;
        if (Band2StdDev > 0 && (Plots[3].Visible || Plots[4].Visible)) active[n++] = 1;
        if (Band3StdDev > 0 && (Plots[5].Visible || Plots[6].Visible)) active[n++] = 2;
        if (Band4StdDev > 0 && (Plots[7].Visible || Plots[8].Visible)) active[n++] = 3;
        if (n == 0) return;

        // Innermost core: between its own two lines (spans the VWAP centre).
        int inner = active[0];
        BandFill.FillBetween(renderer, vp, Upper(inner), Lower(inner), total,
            BandFill.WithAlpha(Plots[1 + inner * 2].Color, alpha));

        // Each outer band: the two annular rings out to the previous active band.
        for (int k = 1; k < n; k++)
        {
            int cur = active[k], prev = active[k - 1];
            var color = BandFill.WithAlpha(Plots[1 + cur * 2].Color, alpha);
            BandFill.FillBetween(renderer, vp, Upper(cur), Upper(prev), total, color); // top ring
            BandFill.FillBetween(renderer, vp, Lower(prev), Lower(cur), total, color); // bottom ring
        }
    }

    // Band index (0-based) → its upper / lower σ series.
    private ChunkedSeries<double> Upper(int band) => band switch
    {
        0 => _upper1, 1 => _upper2, 2 => _upper3, _ => _upper4,
    };

    private ChunkedSeries<double> Lower(int band) => band switch
    {
        0 => _lower1, 1 => _lower2, 2 => _lower3, _ => _lower4,
    };
}
