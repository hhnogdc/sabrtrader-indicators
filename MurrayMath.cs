using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Indicators.TradingHours;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Murray Math Lines. Builds the Murrey octave
/// grid from the highest/lowest price over a lookback window of
/// <c>FrameSize × FrameMultiplier</c> bars (default 64 × 1.5 = 96), then emits the
/// 15 octave levels (eighths −3/8 … +11/8) as horizontal overlay lines.
///
/// <para>The octave math (log-10 / log-8 / log-2 scale derivation → grid step → centred
/// position → equilibrium shift) is reproduced verbatim from the original so the level VALUES
/// match bar-for-bar; see <see cref="ComputeLevels"/>. <see cref="Days"/> controls how
/// many recent sessions of history the lines are drawn over (older bars emit NaN, exactly
/// like the original's <c>_targetBar</c> draw-start gating); the level values themselves are
/// independent of <see cref="Days"/> — they only depend on the rolling high/low window.</para>
///
/// <para>Colours follow the original: the major lines (0/8, 4/8, 8/8) blue, the ±2/8 lines red,
/// the rest green.</para>
/// </summary>
[IndicatorDescription("Murray Math Lines: the Murrey octave price grid (eighths -3/8 to +11/8) derived from the highest/lowest price over a lookback of FrameSize x FrameMultiplier bars. Major lines (0/8, 4/8, 8/8) act as primary support/resistance.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MurrayMath : IndicatorBase, IChartCustomRender
{
    /// <summary>Number of octave lines (the original loop <c>for (i=-3; i&lt;=8+3; i++)</c> → eighths −3/8 … +11/8).</summary>
    public const int LineCount = 15;

    // Natural-log denominators used as change-of-base divisors (the original _log10 / _log8 / _log2).
    private static readonly double Log10 = Math.Log(10);
    private static readonly double Log8  = Math.Log(8);
    private static readonly double Log2  = Math.Log(2);

    private readonly ChunkedSeries<double>[] _level = new ChunkedSeries<double>[LineCount];
    private readonly IndicatorOutputSeries?[] _wrap = new IndicatorOutputSeries?[LineCount];

    private RollingMinMax _high = null!;
    private RollingMinMax _low  = null!;
    private SessionResetTracker? _counter;   // used only to count sessions for the Days gate
    private int _lookBack;

    // Render state: the most recent level set + its labels.
    private readonly double[] _lastLevels = new double[LineCount];
    private bool _hasLevels;

    public MurrayMath(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        for (int k = 0; k < LineCount; k++)
        {
            int eighth = k - 3;                       // DataSeries[0] = −3/8 … DataSeries[14] = +11/8
            _level[k] = new ChunkedSeries<double>();
            AddPlot(new Plot(LabelFor(eighth), _level[k], PlotStyle.Step, ColorFor(eighth), MajorFor(eighth) ? 1.5 : 1.0)
            {
                Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None,
                    "Murrey octave level; 0/8, 4/8 and 8/8 are the major support/resistance lines")
            });
        }
    }

    [IndicatorParameter(DisplayName = "Days", Description = "Number of recent sessions the lines are drawn over (cosmetic; does not change the level values).",
        Group = "01. Parameters", Order = 1, MinValue = 1, MaxValue = 1000, Step = 1)]
    public int Days { get; set; } = 20;

    [IndicatorParameter(DisplayName = "Frame Size", Description = "Base lookback (the original FrameSize; default 64). Lookback window = Frame Size x Frame Multiplier bars.",
        Group = "01. Parameters", Order = 2, MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int FrameSize { get; set; } = 64;

    [IndicatorParameter(DisplayName = "Frame Multiplier", Description = "Lookback multiplier applied to Frame Size (the original default 1.5).",
        Group = "01. Parameters", Order = 3, MinValue = 0.1, MaxValue = 100, Step = 0.1)]
    public double FrameMultiplier { get; set; } = 1.5;

    [IndicatorParameter(DisplayName = "Ignore Wicks", Description = "When on (default), the high/low window uses candle bodies (max/min of open & close) instead of wicks.",
        Group = "01. Parameters", Order = 4)]
    public bool IgnoreWicks { get; set; } = true;

    /// <summary>The octave level at <paramref name="eighth"/> (−3 … +11); newest value at index 0.</summary>
    public ISeries<double> Level(int eighth)
    {
        int k = eighth + 3;
        if ((uint)k >= LineCount) throw new ArgumentOutOfRangeException(nameof(eighth), "Eighth must be in [-3, +11].");
        return _wrap[k] ??= new IndicatorOutputSeries(_level[k], Bars);
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Days < 1) throw new ArgumentOutOfRangeException(nameof(Days));
        if (FrameSize < 1) throw new ArgumentOutOfRangeException(nameof(FrameSize));
        if (FrameMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(FrameMultiplier));

        _lookBack = Math.Max(1, (int)(FrameSize * FrameMultiplier));
        _high = new RollingMinMax(_lookBack, trackMax: true);
        _low  = new RollingMinMax(_lookBack, trackMax: false);
        _counter = SessionResetTracker.Create(ctx.CreateSessionIterator(), sessionAware: true,
            start: new TimeOnly(0, 0), end: new TimeOnly(0, 0));
        _hasLevels = false;
        Array.Clear(_lastLevels);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        int n = bars.Count;
        if (n == 0) return;

        // Pass 1 — count sessions to find the draw-start bar (the original _targetBar): the bar that
        // opens the session `Days` sessions before the most recent one. Levels for earlier
        // bars are emitted as NaN (drawn blank), matching the original's SetPointOfEndLine gate.
        var sessionStarts = new List<int>(Math.Max(4, n / 64));
        for (int i = 0; i < n; i++)
        {
            var b = bars[n - 1 - i];
            if (_counter!.OnBar(b.Start, b.End)) sessionStarts.Add(i);
        }
        int targetBar = sessionStarts.Count > Days ? sessionStarts[sessionStarts.Count - Days] : 0;

        // Pass 2 — warm the rolling window from the oldest bar and emit levels from targetBar on.
        for (int i = 0; i < n; i++)
        {
            var b = bars[n - 1 - i];
            ConsumeAndEmit(b, emit: i >= targetBar);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        // Live bars are always within the recent draw window → always emit.
        ConsumeAndEmit(Bars[0], emit: true);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _hasLevels = false;
        Array.Clear(_lastLevels);
    }

    private void ConsumeAndEmit(Bar bar, bool emit)
    {
        double hValue = IgnoreWicks ? Math.Max(bar.Open, bar.Close) : bar.High;
        double lValue = IgnoreWicks ? Math.Min(bar.Open, bar.Close) : bar.Low;
        _high.Push(hValue);
        _low.Push(lValue);

        if (!emit)
        {
            for (int k = 0; k < LineCount; k++) _level[k].Append(double.NaN);
            return;
        }

        ComputeLevels(_high.Value, _low.Value, _lastLevels);
        for (int k = 0; k < LineCount; k++) _level[k].Append(_lastLevels[k]);
        _hasLevels = true;
    }

    /// <summary>
    /// Verbatim the original Murrey octave computation. Fills <paramref name="outLevels"/> (length
    /// <see cref="LineCount"/>) with the 15 eighth levels, index 0 = −3/8 … index 14 = +11/8.
    /// </summary>
    internal static void ComputeLevels(double highPeriod, double lowPeriod, double[] outLevels)
    {
        double difference = highPeriod - lowPeriod;

        double tmpHigh = lowPeriod < 0 ? 0 - lowPeriod : highPeriod;
        double tmpLow  = lowPeriod < 0 ? 0 - lowPeriod - difference : lowPeriod;
        bool shift = lowPeriod < 0;

        double sfVar = Math.Log(0.4 * tmpHigh) / Log10
                     - Math.Floor(Math.Log(0.4 * tmpHigh) / Log10);

        double sr;
        if (tmpHigh > 25)
        {
            sr = sfVar > 0
                ? Math.Exp(Log10 * Math.Floor(Math.Log(0.4 * tmpHigh) / Log10) + 1.0)
                : Math.Exp(Log10 * Math.Floor(Math.Log(0.4 * tmpHigh) / Log10));
        }
        else
        {
            sr = 100.0 * Math.Exp(Log8 * Math.Floor(Math.Log(0.005 * tmpHigh) / Log8));
        }

        double highDiff = tmpHigh - tmpLow;
        double nVar1 = Math.Log(sr / (highDiff == 0 ? 1 : highDiff)) / Log8;
        double nVar2 = nVar1 - Math.Floor(nVar1);
        double n = nVar1 <= 0 ? 0 : nVar2 == 0 ? Math.Floor(nVar1) : Math.Floor(nVar1) + 1;

        double si = sr * Math.Exp(-n * Log8);
        double m = Math.Floor(1.0 / Log2 * Math.Log((tmpHigh - tmpLow) / si + 0.0000001));

        double iVar = Math.Round((tmpHigh + tmpLow) * 0.5 / (si * Math.Exp((m - 1.0) * Log2)));
        double bot = (iVar - 1.0) * si * Math.Exp((m - 1.0) * Log2);
        double top = (iVar + 1.0) * si * Math.Exp((m - 1.0) * Log2);

        bool doShift = tmpHigh - top > 0.25 * (top - bot)
                    || bot - tmpLow > 0.25 * (top - bot);
        int er = doShift ? 1 : 0;

        double mm = er == 0 ? m : er == 1 && m < 2 ? m + 1.0 : 0;
        double nn = er == 0 ? n : er == 1 && m < 2 ? n : n - 1.0;

        double finalSi  = er == 1 ? sr * Math.Exp(-nn * Log8) : si;
        double finalI   = er == 1 ? Math.Round((tmpHigh + tmpLow) * 0.5 / (finalSi * Math.Exp((mm - 1.0) * Log2))) : iVar;
        double finalBot = er == 1 ? (finalI - 1.0) * finalSi * Math.Exp((mm - 1.0) * Log2) : bot;
        double finalTop = er == 1 ? (finalI + 1.0) * finalSi * Math.Exp((mm - 1.0) * Log2) : top;

        double increment = (finalTop - finalBot) / 8.0;
        double absTop = shift ? -(finalBot - 3.0 * increment) : finalTop + 3.0 * increment;

        // The original fills DataSeries[Count-1] = absTop downward by `increment`, so index 0 = the
        // lowest line (−3/8) and index LineCount-1 = the highest (+11/8).
        int lineCount = 0;
        for (int k = LineCount - 1; k >= 0; k--)
        {
            outLevels[k] = absTop - lineCount * increment;
            lineCount++;
        }
    }

    /// <summary>Custom render: draws the eighth label (e.g. "8/8", "+1/8") next to each line.</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (!_hasLevels) return;
        IChartRenderer r = ctx.Renderer;
        ChartViewport vp = ctx.Viewport;
        // The octave LINES are standard Step plots (PlotRenderPass honours their Visible /
        // Style / Color); add only the right-edge label, gated on each plot's Visible and
        // painted in its Colour (Plots[k] is the level-k line).
        for (int k = 0; k < LineCount; k++)
        {
            var plot = Plots[k];
            if (!plot.Visible) continue;
            double v = _lastLevels[k];
            if (double.IsNaN(v)) continue;
            LevelLabel.Draw(r, vp, ctx.PlotArea, plot.Name, v, plot.Color, 11);
        }
    }

    private static bool MajorFor(int eighth) => eighth % 4 == 0;   // 0/8, 4/8, 8/8

    private static ChartColor ColorFor(int eighth)
    {
        int mod = eighth % 4;                 // C# keeps the sign of the dividend
        if (mod == 0) return PlotColors.DodgerBlue;
        if (mod == 2 || mod == -2) return PlotColors.Crimson;
        return PlotColors.MediumSeaGreen;
    }

    private static string LabelFor(int eighth)
        => eighth <= 8 ? $"{eighth}/8" : $"+{eighth % 4}/8";   // 9→+1/8, 10→+2/8, 11→+3/8
}
