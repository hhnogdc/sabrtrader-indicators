using System;
using System.Collections.Generic;
using System.Linq;
using SabrTrader.Pipeline.Alerts;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Moving-average type for the <see cref="SslChannel"/> high/low channel — a 
/// match of the original TDU SSL Channel's <c>Moving Average type</c> dropdown
/// (same options, same declaration order). The original default is <see cref="VWMA"/>.
/// </summary>
public enum SslMaType
{
    SMA,
    EMA,
    DEMA,
    HMA,
    /// <summary>MESA Adaptive MA (Ehlers), fed the high / low price — the original calls MAMA(series, 0.5, 0.5).</summary>
    MAMA,
    /// <summary>Tim Tillson T3 — the original calls T3(series, period, 3, 0.7).</summary>
    T3,
    TEMA,
    TMA,
    /// <summary>EMA of bar volume — matches the original @VOLMA verbatim (both lines collapse to the volume EMA).</summary>
    VOLMA,
    /// <summary>Volume-weighted MA — Σ(price·vol)/Σ(vol). The original default.</summary>
    VWMA,
    WMA,
    ZLEMA,
    /// <summary>McGinley Dynamic, the original's custom smoothing (÷ 0.6·period·(p/prev)⁴), fed high / low.</summary>
    McGinley,
}

/// <summary>
/// SSL Channel (Semaphore Signal Level) — a faithful port of the original
/// TDU SSL Channel study: the high/low moving-average channel, trend paint, the
/// reversal / pull-back signal engine, AND the original on-chart trade overlay — per-entry
/// reversal (R) / pull-back (PB) markers, entry &amp; target lines with tick labels, and
/// the win-ratio / profit-factor / expected-value statistics table.
///
/// <para><b>Channel.</b> Two moving averages — one of the highs, one of the lows — of
/// the selected <see cref="MAType"/> (all 13 the original types). A trend flag <c>hlv</c> flips
/// to +1 when Close closes above the high-MA, −1 below the low-MA, else carries. The
/// drawn lines swap on the flag and are spread by an optional ATR offset
/// (<c>ATR(ATRPeriod)·ATRMultiplier</c>, the original default 0).</para>
///
/// <para><b>Signals &amp; trades.</b> On the bar after a trend flip a reversal fires and
/// a tracked trade entry is opened (entry = prior bar extreme ± 1 tick); a pull-back
/// fires when price closes back inside the channel and breaks out again. Each entry is
/// simulated against <see cref="StoplossTicks"/> / <see cref="TargetTicks"/> to drive the
/// markers, the max-favourable target line and the statistics table — a port of
/// the original's <c>HandleEntries</c> / <c>HandleLastReversal</c> / <c>IsEntryActive</c> /
/// <c>RenderEntry</c> / <c>RenderStatistics</c>.</para>
///
/// <para>The original processes <c>OnEachTick</c>; this port runs on the platform's closed-bar
/// cadence (the same model the rest of the built-ins use), so the simulated trade
/// statistics match the original bar-for-bar on history and differ only in intrabar stop/target
/// tie-breaks. The original licence scaffolding is intentionally not ported.</para>
///
/// Price overlay. Defaults MAType=VWMA, Period=20, ATRPeriod=20, ATRMultiplier=0.
/// </summary>
[IndicatorDescription("SSL Channel (TDU): high/low moving-average channel with a selectable MA type (13 options, VWMA default) and optional ATR spread. The up/down lines swap on a close-based trend flag; reversals and pull-backs open tracked trades with on-chart markers, target lines and a win-ratio / profit-factor statistics panel.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed partial class SslChannel : IndicatorBase, IChartCustomRender, IBarColorSource
{
    // ─── Plots (the original Values[0..7]) — channel lines custom-rendered (per-bar trend
    //     colour); trend/signal/reversal/pullback are data-box-only signal series ──
    private readonly ChunkedSeries<double> _up   = new();
    private readonly ChunkedSeries<double> _down = new();
    private readonly ChunkedSeries<double> _trend = new();
    private readonly ChunkedSeries<double> _signal = new();
    private readonly ChunkedSeries<double> _revLong = new();
    private readonly ChunkedSeries<double> _revShort = new();
    private readonly ChunkedSeries<double> _pullLong = new();
    private readonly ChunkedSeries<double> _pullShort = new();
    // Per-bar pill colours so the axis markers match the trend-coloured channel lines
    // (both plots default to a single blue). Appended in lockstep with _up / _down.
    private readonly ChunkedSeries<ChartColor> _upColors = new(), _downColors = new();
    private IndicatorOutputSeries? _upW, _downW, _trendW, _sigW, _rlW, _rsW, _plW, _psW;

    // ─── Per-bar render metadata (absolute index) ───────────────────────────
    private readonly List<double> _upV = new(), _downV = new();
    private readonly List<sbyte> _trendDir = new();   // +1 up / −1 down / 0 warmup
    private readonly List<bool> _computed = new();

    // ─── Internal primary-bar history (the original Opens/Highs/Lows/Closes/Times view) ─
    private readonly List<double> _opens = new(), _highs = new(), _lows = new(), _closes = new();
    private readonly List<DateTime> _times = new();

    // ─── Tracked trade entries (the original _entries) ────────────────────────────────
    private readonly List<SslEntry> _entries = new();

    /// <summary>Guards collections shared between the calc thread and the render
    /// pass (which — exactly like the original's OnRender — also reads the entry list).</summary>
    private readonly object _gate = new();

    // ─── MA sources: either composed child MAs (scalar types) or streaming cores ──
    private CoreKind _core;
    private IndicatorBase? _highMa, _lowMa;
    private ISeries<double>? _highMaOut, _lowMaOut;   // child .Output when composed
    private VwmaPriceCore _highVwma = null!, _lowVwma = null!;
    private McGinleyCore _mgHigh = null!, _mgLow = null!;
    private MamaPriceCore _mamaHigh = null!, _mamaLow = null!;
    private EmaCore _volEma = null!;
    private NtAtr _atr = null!;

    private int _barIdx, _warmup, _prevHlv, _pullbackState;
    private double _prevClose, _price;
    private DateTime _dateNow;
    private double _tickSize = 0.25, _pointValue = 1;

    private enum CoreKind { ChildScalar, Vwma, McGinley, Mama, Volma }

    public SslChannel(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        // Channel lines: custom-rendered per-bar by trend colour. IsCustomRendered →
        // the standard pass skips them (no double-draw); Visible = true so the settings
        // show/hide toggle gates the custom render and the line-style picker works. Kept
        // for the data box. Trend/Signal/Reversal/Pullback: transparent signal series.
        AddPlot(new Plot("Upper line", _up, PlotStyle.Line, PlotColors.DodgerBlue, 3.0)
        { Visible = true, IsCustomRendered = true, PerBarColors = _upColors, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "upper SSL channel line") });
        AddPlot(new Plot("Lower line", _down, PlotStyle.Line, PlotColors.DodgerBlue, 3.0)
        { Visible = true, IsCustomRendered = true, PerBarColors = _downColors, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, "lower SSL channel line") });
        AddPlot(new Plot("Trend", _trend, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, "+1 uptrend / -1 downtrend") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, "±1 reversal, ±2 pull-back") });
        AddPlot(new Plot("Reversal Long", _revLong, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Reversal Short", _revShort, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
        AddPlot(new Plot("Pullback Long", _pullLong, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Pullback Short", _pullShort, PlotStyle.Line, ChartColor.Transparent, 1.0)
        { Visible = false, ShowPriceMarker = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
    }

    // ─── 01. Parameters ─────────────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "Moving Average type", Group = "01. Parameters", Order = 1,
        Description = "Average used for the high/low channel. The original default: VWMA.")]
    public SslMaType MAType { get; set; } = SslMaType.VWMA;

    [IndicatorParameter(DisplayName = "Moving Average Period", Group = "01. Parameters", Order = 2,
        Description = "MA length for the high/low channel.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    [IndicatorParameter(DisplayName = "ATR Period", Group = "01. Parameters", Order = 3,
        Description = "ATR length for the channel spread.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int ATRPeriod { get; set; } = 20;

    [IndicatorParameter(DisplayName = "ATR Multiplier", Group = "01. Parameters", Order = 4,
        Description = "Spreads the channel by ATR·multiplier. 0 (the original default) = lines sit on the raw MAs.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double ATRMultiplier { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Fill Channel", Group = "01. Parameters", Order = 5)]
    public bool FillBands { get; set; } = true;

    [IndicatorParameter(DisplayName = "Paint Bars", Group = "01. Parameters", Order = 6)]
    public bool PaintBars { get; set; } = true;   // the original default

    [IndicatorParameter(DisplayName = "Show Pullbacks", Group = "01. Parameters", Order = 7)]
    public bool ShowPullbacks { get; set; } = true;

    [IndicatorParameter(DisplayName = "Show Reversals", Group = "01. Parameters", Order = 8)]
    public bool ShowReversals { get; set; } = true;

    // ─── 02. Stats ──────────────────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "Show Statistics", Group = "02. Stats", Order = 1)]
    public bool ShowStats { get; set; } = true;

    [IndicatorParameter(DisplayName = "X coordinate", Group = "02. Stats", Order = 2,
        Description = "Statistics panel left edge, pixels.", MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int StatsX { get; set; } = 20;

    [IndicatorParameter(DisplayName = "Y coordinate", Group = "02. Stats", Order = 3,
        Description = "Statistics panel top edge, pixels.", MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int StatsY { get; set; } = 70;

    [IndicatorParameter(DisplayName = "Entry line width (pixels)", Group = "02. Stats", Order = 4,
        MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int EntryLineWidth { get; set; } = 40;

    [IndicatorParameter(DisplayName = "Target (ticks)", Group = "02. Stats", Order = 5,
        MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int TargetTicks { get; set; } = 4;

    [IndicatorParameter(DisplayName = "Stoploss (ticks)", Group = "02. Stats", Order = 6,
        MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int StoplossTicks { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Target line color", Group = "02. Stats", Order = 7,
        Description = "Max-favourable-excursion line drawn from a winning reversal marker.")]
    public ChartColor TargetLineColor { get; set; } = new(0xFF, 0xFF, 0xFF, 0xCC); // White @ 80%

    [IndicatorParameter(DisplayName = "Target line width", Group = "02. Stats", Order = 8,
        MinValue = 0.5, MaxValue = 20, Step = 0.5)]
    public double TargetLineWidth { get; set; } = 4;

    [IndicatorParameter(DisplayName = "Target line style", Group = "02. Stats", Order = 9)]
    public LineStyle TargetLineStyle { get; set; } = LineStyle.Dashed;

    [IndicatorParameter(DisplayName = "Target Winner Color", Group = "02. Stats", Order = 10)]
    public ChartColor TargetWinnerColor { get; set; } = PlotColors.LimeGreen;

    [IndicatorParameter(DisplayName = "Target Loser Color", Group = "02. Stats", Order = 11)]
    public ChartColor TargetLoserColor { get; set; } = PlotColors.Crimson;

    [IndicatorParameter(DisplayName = "Target text size", Group = "02. Stats", Order = 12,
        MinValue = 4, MaxValue = 72, Step = 1)]
    public double TargetFontSize { get; set; } = 14;

    // ─── 03. Colors ─────────────────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "OutLine Uptrend", Group = "03. Colors", Order = 1)]
    public ChartColor UptrendColor { get; set; } = PlotColors.ForestGreen;

    [IndicatorParameter(DisplayName = "OutLine Downtrend", Group = "03. Colors", Order = 2)]
    public ChartColor DowntrendColor { get; set; } = PlotColors.Crimson;

    [IndicatorParameter(DisplayName = "Candle Uptrend", Group = "03. Colors", Order = 3)]
    public ChartColor CandleUptrendColor { get; set; } = PlotColors.ForestGreen;

    [IndicatorParameter(DisplayName = "Candle Downtrend", Group = "03. Colors", Order = 4)]
    public ChartColor CandleDowntrendColor { get; set; } = PlotColors.Crimson;

    [IndicatorParameter(DisplayName = "Fill Up bands", Group = "03. Colors", Order = 5)]
    public ChartColor FillUptrendColor { get; set; } = PlotColors.ForestGreen;

    [IndicatorParameter(DisplayName = "Fill Lower bands", Group = "03. Colors", Order = 6)]
    public ChartColor FillDowntrendColor { get; set; } = PlotColors.Crimson;

    [IndicatorParameter(DisplayName = "Fill opacity %", Group = "03. Colors", Order = 7,
        Description = "Channel fill opacity (the original FillUpTrend/FillDownTrend opacity = 15).",
        MinValue = 0, MaxValue = 100, Step = 1)]
    public int FillOpacity { get; set; } = 15;

    [IndicatorParameter(DisplayName = "Line width", Group = "03. Colors", Order = 8,
        MinValue = 0.5, MaxValue = 20, Step = 0.5)]
    public double LineWidth { get; set; } = 3.0;

    [IndicatorParameter(DisplayName = "Text Color", Group = "03. Colors", Order = 9,
        Description = "Marker label + statistics text.")]
    public ChartColor TextColor { get; set; } = PlotColors.White;

    [IndicatorParameter(DisplayName = "Text size", Group = "03. Colors", Order = 10,
        Description = "Reversal/pull-back marker label size (the original Text Font = 8).", MinValue = 4, MaxValue = 72, Step = 1)]
    public double FontSize { get; set; } = 8;

    [IndicatorParameter(DisplayName = "Statistics size", Group = "03. Colors", Order = 11,
        Description = "Statistics panel text size (the original Statistics Font = 12).", MinValue = 4, MaxValue = 72, Step = 1)]
    public double StatFontSize { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Font family", Group = "03. Colors", Order = 12,
        Description = "Optional font family for all text; blank = chart default.")]
    public string FontFamilyName { get; set; } = "";

    [IndicatorParameter(DisplayName = "Statistics Background", Group = "03. Colors", Order = 13)]
    public ChartColor StatsBGColor { get; set; } = PlotColors.Gray;

    [IndicatorParameter(DisplayName = "Pullback", Group = "03. Colors", Order = 14,
        Description = "Pull-back (PB) marker fill.")]
    public ChartColor PullbackColor { get; set; } = PlotColors.DarkSeaGreen;

    [IndicatorParameter(DisplayName = "Up Trend Reversal", Group = "03. Colors", Order = 15,
        Description = "Long reversal (R) marker fill.")]
    public ChartColor UpTrendReversalColor { get; set; } = PlotColors.DeepSkyBlue;

    [IndicatorParameter(DisplayName = "Down Trend Reversal", Group = "03. Colors", Order = 16,
        Description = "Short reversal (R) marker fill.")]
    public ChartColor DownTrendReversalColor { get; set; } = new(0xF0, 0x80, 0x80); // LightCoral

    [IndicatorParameter(DisplayName = "Long entry", Group = "03. Colors", Order = 17,
        Description = "Long entry-price tick line.")]
    public ChartColor LongEntryColor { get; set; } = new(0x2E, 0x8B, 0x57); // SeaGreen

    [IndicatorParameter(DisplayName = "Short entry", Group = "03. Colors", Order = 18,
        Description = "Short entry-price tick line.")]
    public ChartColor ShortEntryColor { get; set; } = PlotColors.Magenta;

    [IndicatorParameter(DisplayName = "Entry line thickness", Group = "03. Colors", Order = 19,
        MinValue = 0.5, MaxValue = 20, Step = 0.5)]
    public double EntryColorWidth { get; set; } = 2;

    // ─── 04. Alerts ─────────────────────────────────────────────────────────

    [IndicatorParameter(DisplayName = "Trend reversal alert", Group = "04. Alerts", Order = 1)]
    public bool TrendAlertEnabled { get; set; } = true;   // the original default

    [IndicatorParameter(DisplayName = "Pullback alert", Group = "04. Alerts", Order = 2)]
    public bool PullbackAlertEnabled { get; set; } = true;   // the original default

    [IndicatorParameter(DisplayName = "Sound", Group = "04. Alerts", Order = 3,
        Editor = ParameterEditorKind.FilePicker, FileFilter = "WAV Files (*.wav)|*.wav")]
    public string? AlertSound { get; set; } = BuiltinAlertSounds.Default;

    // ─── Chainable outputs (mirror the original Series properties) ────────────────
    public ISeries<double> UpLine        => _upW    ??= new IndicatorOutputSeries(_up, Bars);
    public ISeries<double> DownLine      => _downW  ??= new IndicatorOutputSeries(_down, Bars);
    public ISeries<double> Trend         => _trendW ??= new IndicatorOutputSeries(_trend, Bars);
    public ISeries<double> Signal        => _sigW   ??= new IndicatorOutputSeries(_signal, Bars);
    public ISeries<double> ReversalLong  => _rlW    ??= new IndicatorOutputSeries(_revLong, Bars);
    public ISeries<double> ReversalShort => _rsW    ??= new IndicatorOutputSeries(_revShort, Bars);
    public ISeries<double> PullbackLong  => _plW    ??= new IndicatorOutputSeries(_pullLong, Bars);
    public ISeries<double> PullbackShort => _psW    ??= new IndicatorOutputSeries(_pullShort, Bars);

    // ─── original-style accessors over the internal history ───────────────────────
    private double TickSize => _tickSize;
    private int CurrentBar => _closes.Count - 1;
    private double High(int barsAgo) => _highs[_highs.Count - 1 - barsAgo];
    private double Low(int barsAgo) => _lows[_lows.Count - 1 - barsAgo];

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (ATRPeriod < 1) throw new ArgumentOutOfRangeException(nameof(ATRPeriod));
        if (ATRMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(ATRMultiplier));

        _tickSize = ctx.TickSize ?? _tickSize;
        _pointValue = ctx.PointValue ?? _pointValue;

        _atr = new NtAtr(ATRPeriod);
        _core = MAType switch
        {
            SslMaType.VWMA     => CoreKind.Vwma,
            SslMaType.McGinley => CoreKind.McGinley,
            SslMaType.MAMA     => CoreKind.Mama,
            SslMaType.VOLMA    => CoreKind.Volma,
            _                  => CoreKind.ChildScalar,
        };

        _highMa = _lowMa = null;
        _highMaOut = _lowMaOut = null;
        switch (_core)
        {
            case CoreKind.Vwma:
                _highVwma = new VwmaPriceCore(Period);
                _lowVwma = new VwmaPriceCore(Period);
                break;
            case CoreKind.McGinley:
                _mgHigh = new McGinleyCore(Period);
                _mgLow = new McGinleyCore(Period);
                break;
            case CoreKind.Mama:
                _mamaHigh = new MamaPriceCore(0.5, 0.5);
                _mamaLow = new MamaPriceCore(0.5, 0.5);
                break;
            case CoreKind.Volma:
                _volEma = new EmaCore(Period);
                break;
            default:
                // Reuse the platform's tested MA indicators, fed the high / low price views.
                _highMa = CreateMa(MAType, Bars.High, Period);
                _lowMa = CreateMa(MAType, Bars.Low, Period);
                _highMaOut = MaOutput(_highMa);
                _lowMaOut = MaOutput(_lowMa);
                break;
        }

        _warmup = 2 * Period;
        _barIdx = 0;
        _prevHlv = 1;
        _pullbackState = 0;
        _prevClose = 0;
        ClearState();
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Composed child MAs are fully backfilled by the framework BEFORE this runs,
        // so their Output is read by barsAgo; the cores stream in this loop.
        var bars = Bars;
        int n = bars.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            Consume(bars[barsAgo], barsAgo, ctx, live: false);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        if (_core == CoreKind.ChildScalar) AdvanceChildren(ctx);   // advance child MAs on the live bar
        Consume(Bars[0], 0, ctx, live: ctx.IsLive);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        ClearState();
    }

    private void ClearState()
    {
        lock (_gate)
        {
            _upV.Clear(); _downV.Clear(); _trendDir.Clear(); _computed.Clear();
            _opens.Clear(); _highs.Clear(); _lows.Clear(); _closes.Clear(); _times.Clear();
            _entries.Clear();
            _price = 0; _dateNow = default;
        }
    }

    /// <summary>Process one bar — with the original <c>OnBarUpdate</c> body.</summary>
    private void Consume(in Bar bar, int barsAgo, IIndicatorContext ctx, bool live)
    {
        lock (_gate)
        {
            // Mirror the bar (the original Opens/Highs/Lows/Closes/Times[0]).
            _opens.Add(bar.Open); _highs.Add(bar.High); _lows.Add(bar.Low);
            _closes.Add(bar.Close); _times.Add(bar.Start);
            _price = bar.Close;
            _dateNow = bar.Start;

            // Feed the streaming cores every bar (the original advances every child indicator each bar).
            double atrRaw = _atr.Add(bar.High, bar.Low, bar.Close);
            double smaHi, smaLo;
            switch (_core)
            {
                case CoreKind.Vwma:
                    smaHi = _highVwma.Add(bar.High, bar.Volume);
                    smaLo = _lowVwma.Add(bar.Low, bar.Volume);
                    break;
                case CoreKind.McGinley:
                    smaHi = _mgHigh.Add(bar.High);
                    smaLo = _mgLow.Add(bar.Low);
                    break;
                case CoreKind.Mama:
                    smaHi = _mamaHigh.Add(bar.High);
                    smaLo = _mamaLow.Add(bar.Low);
                    break;
                case CoreKind.Volma:
                    // The original @VOLMA is an EMA of bar volume — both high & low collapse to it (verbatim).
                    smaHi = smaLo = _volEma.Add(bar.Volume);
                    break;
                default:
                    smaHi = _highMaOut![barsAgo];
                    smaLo = _lowMaOut![barsAgo];
                    break;
            }

            if (_barIdx < _warmup)
            {
                AppendBar(bar.Close, bar.Close, 0, computed: false);
                _prevHlv = 1;
                _prevClose = bar.Close;
                _barIdx++;
                return;
            }

            // The original: at the top of every new bar — simulate open trades + track MFE.
            HandleEntries();
            HandleLastReversal();

            double atr = atrRaw * ATRMultiplier;
            int hlv = bar.Close > smaHi ? 1 : bar.Close < smaLo ? -1 : _prevHlv;
            _prevHlv = hlv;

            double down = hlv < 0 ? smaHi : smaLo;
            double up = hlv < 0 ? smaLo : smaHi;
            if (up > down) { up += atr; down -= atr; }
            else { up -= atr; down += atr; }

            double bottom = Math.Min(up, down);
            double top = Math.Max(up, down);
            sbyte trend = up >= down ? (sbyte)1 : (sbyte)-1;

            AppendBar(top, bottom, trend, computed: true);

            // ── Reversal: a trend flip is confirmed one bar later (the original: Trend[1] != Trend[2],
            //    which also fires on the first computed bar after the warm-up zero — faithful). ──
            int cur = _trendDir.Count - 1;
            bool reversal = cur >= 2 && _trendDir[cur - 1] != _trendDir[cur - 2];
            if (reversal)
            {
                // The original sets the Reversal signal plots unconditionally (Trend[1] != Trend[2]);
                // ShowReversals only gates the on-chart marker in RenderEntry.
                if (trend > 0) _revLong.UpdateLast(1);
                else if (trend < 0) _revShort.UpdateLast(-1);
                _pullbackState = 2;
                _signal.UpdateLast(trend);

                double entryPrice = trend > 0 ? High(1) + TickSize : Low(1) - TickSize;
                // The original: IsEntryActive closes any open reversal trade (computing its result), then
                // always returns false, so a new reversal trade is opened.
                if (!IsEntryActive(entryPrice))
                {
                    _entries.Add(new SslEntry
                    {
                        PointType = trend < 0 ? SslPointType.ReversalDown : SslPointType.ReversalUp,
                        EntryTime = bar.Start,
                        Price = trend > 0 ? bottom : top,
                        EntryBar = CurrentBar,
                        IsLong = trend > 0,
                        EntryPrice = entryPrice,
                        IsActive = true,
                        MaxBar = CurrentBar,
                        MaxTime = bar.Start,
                        MaxPrice = entryPrice,
                    });
                }

                if (TrendAlertEnabled && live)
                    ctx.Alert("ssl-reversal", trend > 0 ? "SSL trend reversal up" : "SSL trend reversal down",
                        AlertSound, AlertSeverity.Warning);
            }

            // ── Pull-back state machine on the PRIOR bar's channel + close (the original DownLine[1]/UpLine[1]). ──
            if (cur >= 1)
            {
                double prevBottom = _downV[cur - 1];
                double prevTop = _upV[cur - 1];
                double prevClose = _prevClose;
                bool insidePrev = prevClose > prevBottom && prevClose < prevTop;
                bool outsidePrev = prevClose < prevBottom || prevClose > prevTop;
                switch (_pullbackState)
                {
                    case 0:
                        if (insidePrev) _pullbackState = 1;
                        break;
                    case 1:
                        if (outsidePrev)
                        {
                            _pullbackState = 0;
                            // The original sets the Pullback signal plots unconditionally; ShowPullbacks
                            // only gates the on-chart marker in RenderEntry.
                            _signal.UpdateLast(trend > 0 ? 2 : -2);
                            if (trend > 0) _pullLong.UpdateLast(1);
                            else _pullShort.UpdateLast(-1);
                            _entries.Add(new SslEntry
                            {
                                PointType = SslPointType.Pullback,
                                EntryTime = bar.Start,
                                Price = trend > 0 ? prevBottom : prevTop,
                                EntryBar = CurrentBar,
                                IsLong = trend > 0,
                                EntryPrice = trend > 0 ? High(1) + TickSize : Low(1) - TickSize,
                                IsActive = true,
                                MaxBar = CurrentBar,
                                MaxTime = bar.Start,
                                MaxPrice = trend > 0 ? High(1) + TickSize : Low(1) - TickSize,
                            });
                            if (PullbackAlertEnabled && live)
                                ctx.Alert("ssl-pullback", trend > 0 ? "SSL pullback long" : "SSL pullback short",
                                    AlertSound, AlertSeverity.Warning);
                        }
                        break;
                    case 2:
                        if (outsidePrev) _pullbackState = 3;
                        break;
                    case 3:
                        _pullbackState = outsidePrev ? 0 : 2;
                        break;
                }
            }

            _prevClose = bar.Close;
            _barIdx++;
        }
    }

    private void AppendBar(double up, double down, sbyte trend, bool computed)
    {
        _up.Append(up);
        _down.Append(down);
        // Same trend colour the channel lines are drawn with (SslChannelRendering).
        ChartColor lineColor = trend > 0 ? UptrendColor : DowntrendColor;
        _upColors.Append(lineColor);
        _downColors.Append(lineColor);
        _trend.Append(computed ? trend : 0);
        _signal.Append(0);
        _revLong.Append(0); _revShort.Append(0);
        _pullLong.Append(0); _pullShort.Append(0);

        _upV.Add(up); _downV.Add(down); _trendDir.Add(trend); _computed.Add(computed);
    }

    // ─── Entry simulation (the original HandleEntries / HandleLastReversal / IsEntryActive) ──

    /// <summary>The original <c>IsEntryActive</c>: closes every open reversal trade against
    /// <paramref name="price"/> (computing winner / profit), then returns false so the
    /// caller always opens a fresh reversal trade.</summary>
    private bool IsEntryActive(double price)
    {
        double tickValue = _pointValue * _tickSize;
        foreach (var entry in _entries)
        {
            if (!entry.IsActive) continue;
            if (entry.PointType != SslPointType.ReversalDown && entry.PointType != SslPointType.ReversalUp) continue;
            entry.IsActive = false;
            entry.ExitPrice = price;
            entry.Winner = entry.IsLong
                ? entry.ExitPrice > entry.EntryPrice + TargetTicks * _tickSize
                : entry.ExitPrice < entry.EntryPrice - TargetTicks * _tickSize;
            entry.CalculateProfit(_tickSize, tickValue);
        }
        return false;
    }

    /// <summary>The original <c>HandleLastReversal</c>: extends the last winning reversal's
    /// max-favourable-excursion with the current bar and propagates it to the pull-back
    /// trades that followed it.</summary>
    private void HandleLastReversal()
    {
        SslEntry? reversal = null;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.PointType == SslPointType.ReversalDown || e.PointType == SslPointType.ReversalUp) { reversal = e; break; }
        }
        if (reversal is null || !reversal.Winner) return;

        int index = _entries.IndexOf(reversal);
        double high = High(0), low = Low(0);
        if (reversal.IsLong)
        {
            if (high > reversal.MaxPrice)
            {
                reversal.MaxPrice = high; reversal.MaxBar = CurrentBar; reversal.MaxTime = _dateNow;
                for (int i = index + 1; i < _entries.Count; ++i)
                    if (_entries[i].PointType == SslPointType.Pullback)
                    { _entries[i].MaxPrice = high; _entries[i].MaxBar = CurrentBar; _entries[i].MaxTime = _dateNow; }
            }
        }
        else
        {
            if (low < reversal.MaxPrice)
            {
                reversal.MaxPrice = low; reversal.MaxBar = CurrentBar; reversal.MaxTime = _dateNow;
                for (int i = index + 1; i < _entries.Count; ++i)
                    if (_entries[i].PointType == SslPointType.Pullback)
                    { _entries[i].MaxPrice = low; _entries[i].MaxBar = CurrentBar; _entries[i].MaxTime = _dateNow; }
            }
        }
    }

    /// <summary>The original <c>HandleEntries</c>: resolve every open trade against the current
    /// bar's stop / target (verbatim, incl. the engulfing-bar tie-break).</summary>
    private void HandleEntries()
    {
        double tickValue = _pointValue * _tickSize;
        double low = Low(0), high = High(0);
        double open = _opens[_opens.Count - 1], close = _closes[_closes.Count - 1];
        bool isRedBar = close < open;
        bool isGreenBar = close > open;
        foreach (var entry in _entries)
        {
            if (!entry.IsActive) continue;
            if (entry.IsLong)
            {
                double stopLoss = entry.EntryPrice - StoplossTicks * _tickSize;
                double tp = entry.EntryPrice + TargetTicks * _tickSize;
                if (low <= stopLoss)
                {
                    entry.IsActive = false;
                    if (high > tp && isRedBar)
                    { entry.Winner = true; entry.Profit = TargetTicks * tickValue; entry.ExitPrice = tp; }
                    else
                    { entry.Winner = false; entry.Profit = -StoplossTicks * tickValue; entry.ExitPrice = stopLoss; }
                }
                else if (high > tp)
                { entry.Profit = TargetTicks * tickValue; entry.Winner = true; entry.IsActive = false; entry.ExitPrice = tp; }
            }
            else
            {
                double stopLoss = entry.EntryPrice + StoplossTicks * _tickSize;
                double tp = entry.EntryPrice - TargetTicks * _tickSize;
                if (high >= stopLoss)
                {
                    entry.IsActive = false;
                    if (low < tp && isGreenBar)
                    { entry.Winner = true; entry.Profit = TargetTicks * tickValue; entry.ExitPrice = tp; }
                    else
                    { entry.Profit = -StoplossTicks * tickValue; entry.Winner = false; entry.ExitPrice = stopLoss; }
                }
                else if (low < tp)
                { entry.Winner = true; entry.IsActive = false; entry.Profit = TargetTicks * tickValue; entry.ExitPrice = tp; }
            }
        }
    }

    // ─── IBarColorSource (paint-bars by trend) ───────────────────────────────

    public bool PaintBarsEnabled => PaintBars;

    public bool TryGetBarColor(int barIndex, in Bar bar, out ChartColor color)
    {
        if ((uint)barIndex < (uint)_trendDir.Count && _computed[barIndex] && _trendDir[barIndex] != 0)
        {
            color = _trendDir[barIndex] > 0 ? CandleUptrendColor : CandleDowntrendColor;
            return true;
        }
        color = default;
        return false;
    }

    // ─── MA factory + cores ──────────────────────────────────────────────────

    /// <summary>Create a child MA of the selected scalar type over <paramref name="input"/>.</summary>
    private IndicatorBase CreateMa(SslMaType type, ISeries<double> input, int period) => type switch
    {
        SslMaType.SMA   => new Sma(this, input) { Period = period },
        SslMaType.EMA   => new Ema(this, input) { Period = period },
        SslMaType.WMA   => new Wma(this, input) { Period = period },
        SslMaType.HMA   => new Hma(this, input) { Period = period },
        SslMaType.DEMA  => new Dema(this, input) { Period = period },
        SslMaType.TEMA  => new Tema(this, input) { Period = period },
        SslMaType.TMA   => new Tma(this, input) { Period = period },
        SslMaType.ZLEMA => new Zlema(this, input) { Period = period },
        SslMaType.T3    => new T3(this, input) { Period = period, TCount = 3, VFactor = 0.7 }, // the original T3(series, period, 3, 0.7)
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Non-scalar type uses a dedicated core."),
    };

    private static ISeries<double> MaOutput(IndicatorBase ma) => ma switch
    {
        Sma s => s.Output,
        Ema e => e.Output,
        Wma w => w.Output,
        Hma h => h.Output,
        Dema d => d.Output,
        Tema t => t.Output,
        Tma t => t.Output,
        Zlema z => z.Output,
        T3 t => t.Output,
        _ => throw new ArgumentOutOfRangeException(nameof(ma)),
    };

    /// <summary>Volume-weighted MA over an arbitrary price (High/Low): Σ(price·vol)/Σ(vol)
    /// over the trailing window, falling back to the price sum when Σ(vol) ≈ 0 — the same
    /// rule as the platform's <see cref="Vwma"/>, applied to High/Low instead of Close.</summary>
    private sealed class VwmaPriceCore
    {
        private readonly double[] _p, _v;
        private readonly int _period;
        private int _count, _writeIdx;
        public VwmaPriceCore(int period) { _period = period; _p = new double[period]; _v = new double[period]; }
        public double Add(double price, double vol)
        {
            _p[_writeIdx] = price; _v[_writeIdx] = vol;
            _writeIdx = (_writeIdx + 1) % _period;
            if (_count < _period) _count++;
            double pv = 0, vs = 0;
            for (int i = 0; i < _count; i++) { pv += _p[i] * _v[i]; vs += _v[i]; }
            return vs <= double.Epsilon ? pv : pv / vs;
        }
    }

    /// <summary>The original TDU's custom McGinley line, fed High/Low: seeded with price, then
    /// <c>v = prev + (price − prev) / (0.6·period·(price/prev)⁴)</c> per bar. NOTE the
    /// 0.6 smoothing constant — this differs from the platform's standard <see cref="McGinleyDynamic"/>.</summary>
    private sealed class McGinleyCore
    {
        private const double Smooth = 0.6;
        private readonly int _period;
        private double _value;
        private bool _seeded;
        public McGinleyCore(int period) => _period = Math.Max(period, 1);
        public double Add(double price)
        {
            if (!_seeded) { _value = price; _seeded = true; return _value; }
            double prev = _value;
            double ratio = price / prev;
            return _value = prev + (price - prev) / (Smooth * _period * (ratio * ratio * ratio * ratio));
        }
    }

    /// <summary>EMA core over a scalar stream (the original @VOLMA fed bar volume).</summary>
    private sealed class EmaCore
    {
        private readonly double _alpha, _oneMinusAlpha;
        private double _value;
        private bool _seeded;
        public EmaCore(int period) { _alpha = 2.0 / (period + 1); _oneMinusAlpha = 1 - _alpha; }
        public double Add(double v)
        {
            if (!_seeded) { _value = v; _seeded = true; return v; }
            return _value = _alpha * v + _oneMinusAlpha * _value;
        }
    }

    /// <summary>The original @ATR (Wilder): bar 0 = High−Low, then running-mean → Wilder smoothing.</summary>
    private sealed class NtAtr
    {
        private readonly int _period;
        private double _value;
        private double? _prevClose;
        private int _n;
        public NtAtr(int period) => _period = period;
        public double Add(double high, double low, double close)
        {
            if (_prevClose is null) { _value = high - low; _prevClose = close; _n = 1; return _value; }
            double prev = _prevClose.Value;
            double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prev), Math.Abs(low - prev)));
            int n = Math.Min(_n + 1, _period);
            _value = ((n - 1) * _value + tr) / n;
            _prevClose = close;
            _n++;
            return _value;
        }
    }

    /// <summary>Ehlers MESA Adaptive MA fed a scalar price (the original MAMA(High/Low, 0.5, 0.5)) —
    /// the same Hilbert-transform discriminator as the platform <see cref="Mama"/>, but with
    /// the input series as price (not the bar median) so the high/low channel separates.</summary>
    private sealed class MamaPriceCore
    {
        private readonly double _fast, _slow;
        private readonly List<double> _smooth = new(), _detrender = new(), _i1 = new(), _q1 = new(),
            _i2 = new(), _q2 = new(), _re = new(), _im = new(), _period = new(), _phase = new(), _mama = new();
        private double _famaPrev;
        private int _barIdx;
        public MamaPriceCore(double fastLimit, double slowLimit) { _fast = fastLimit; _slow = slowLimit; }

        private static double Ago(List<double> s, int k) { int i = s.Count - 1 - k; return i >= 0 ? s[i] : 0; }

        public double Add(double price)
        {
            _smooth.Add(0); _detrender.Add(0); _i1.Add(0); _q1.Add(0); _i2.Add(0); _q2.Add(0);
            _re.Add(0); _im.Add(0); _period.Add(0); _phase.Add(0); _mama.Add(0);

            if (_barIdx < 6) { _barIdx++; _famaPrev = price; return price; }
            if (_barIdx == 6) { _mama[_mama.Count - 1] = price; _famaPrev = price; _barIdx++; return price; }

            double period1 = Ago(_period, 1);
            double smooth0 = (4 * price + 3 * Ago(_smooth, 1) + 2 * Ago(_smooth, 2) + Ago(_smooth, 3)) / 10;
            _smooth[_smooth.Count - 1] = smooth0;

            double k = 0.075 * period1 + 0.54;
            double detrender0 = (0.0962 * smooth0 + 0.5769 * Ago(_smooth, 2) - 0.5769 * Ago(_smooth, 4) - 0.0962 * Ago(_smooth, 6)) * k;
            _detrender[_detrender.Count - 1] = detrender0;

            double q10 = (0.0962 * detrender0 + 0.5769 * Ago(_detrender, 2) - 0.5769 * Ago(_detrender, 4) - 0.0962 * Ago(_detrender, 6)) * k;
            _q1[_q1.Count - 1] = q10;
            double i10 = Ago(_detrender, 3);
            _i1[_i1.Count - 1] = i10;

            double jI = (0.0962 * i10 + 0.5769 * Ago(_i1, 2) - 0.5769 * Ago(_i1, 4) - 0.0962 * Ago(_i1, 6)) * k;
            double jQ = (0.0962 * q10 + 0.5769 * Ago(_q1, 2) - 0.5769 * Ago(_q1, 4) - 0.0962 * Ago(_q1, 6)) * k;

            double i20 = 0.2 * (i10 - jQ) + 0.8 * Ago(_i2, 1);
            double q20 = 0.2 * (q10 + jI) + 0.8 * Ago(_q2, 1);
            _i2[_i2.Count - 1] = i20;
            _q2[_q2.Count - 1] = q20;

            double i21 = Ago(_i2, 1), q21 = Ago(_q2, 1);
            double re0 = 0.2 * (i20 * i21 + q20 * q21) + 0.8 * Ago(_re, 1);
            double im0 = 0.2 * (i20 * q21 - q20 * i21) + 0.8 * Ago(_im, 1);
            _re[_re.Count - 1] = re0;
            _im[_im.Count - 1] = im0;

            double period0 = Ago(_period, 0);
            if (im0 != 0 && re0 != 0) period0 = 360 / (180 / Math.PI * Math.Atan(im0 / re0));
            if (period0 > 1.5 * period1) period0 = 1.5 * period1;
            if (period0 < 0.67 * period1) period0 = 0.67 * period1;
            if (period0 < 6) period0 = 6;
            if (period0 > 50) period0 = 50;
            period0 = 0.2 * period0 + 0.8 * period1;
            _period[_period.Count - 1] = period0;

            double phase0 = Ago(_phase, 0);
            if (i10 != 0) phase0 = 180 / Math.PI * Math.Atan(q10 / i10);
            _phase[_phase.Count - 1] = phase0;

            double deltaPhase = Ago(_phase, 1) - phase0;
            if (deltaPhase < 1) deltaPhase = 1;
            double alpha = _fast / deltaPhase;
            if (alpha < _slow) alpha = _slow;

            double mama0 = alpha * price + (1 - alpha) * Ago(_mama, 1);
            double fama0 = 0.5 * alpha * mama0 + (1 - 0.5 * alpha) * _famaPrev;
            _mama[_mama.Count - 1] = mama0;
            _famaPrev = fama0;
            _barIdx++;
            return mama0;
        }
    }

    // ─── SSL trade entry (the original TDUSSLEntry) ────────────────────────────────────

    private enum SslPointType { ReversalUp, ReversalDown, Pullback }

    private sealed class SslEntry
    {
        public SslPointType PointType;
        public int EntryBar;
        public DateTime EntryTime;
        public double Price;
        public bool Winner;
        public double EntryPrice;
        public double ExitPrice;
        public bool IsLong;
        public bool IsActive;
        public double Profit;
        public DateTime MaxTime;
        public double MaxPrice;
        public int MaxBar;

        public void CalculateProfit(double tickSize, double tickValue)
        {
            double ticks = (IsLong ? ExitPrice - EntryPrice : EntryPrice - ExitPrice) / tickSize;
            Profit = ticks * tickValue;
        }
    }
}
