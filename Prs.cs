using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Price Relative Strength (PRS) — port of Skender Prs.
///   Prs        = eval / base                 (ratio, always emitted)
///   PrsPercent = pctEval(LookbackPeriods) − pctBase(LookbackPeriods)
///                (only emitted when LookbackPeriods &gt; 0)
///
/// <para><b>Self-wiring benchmark.</b> PRS compares the chart's instrument against a
/// benchmark/base instrument. It declares that benchmark by its
/// <see cref="BenchmarkSymbol"/> string and subscribes to it through the universal
/// <see cref="IIndicatorContext.AddDataSeries"/> seam — the chart's own bar spec
/// (period/type) re-pointed at the benchmark via
/// <see cref="BarSpecification.WithInstrumentId"/>; the data provider resolves the
/// symbol → venue/exchange exactly like the primary series. The benchmark series is
/// time-aligned to the primary (latest benchmark close at-or-before each bar, carried
/// forward across gaps). Same venue/consumer-agnostic pattern as the McClellan
/// oscillator's breadth series.</para>
///
/// <para>When the benchmark symbol is blank / doesn't resolve, the indicator emits
/// <see cref="double.NaN"/> (an empty panel) rather than a misleading flat line. A host
/// MAY instead inject the base series directly via <see cref="BaseSeries"/> (index-aligned
/// with the primary, newest = [0]); when set it takes precedence and no subscription is made.</para>
///
/// Defaults LookbackPeriods=0 (PrsPercent disabled), BenchmarkSymbol="ES". DodgerBlue Prs, Goldenrod PrsPercent.
/// </summary>
[IndicatorDescription("Price Relative Strength: ratio of the input series to a benchmark instrument, with an optional Lookback that emits the percent-change difference (eval% - base%) over that window. Pick the benchmark via the BenchmarkSymbol setting — resolved by your data provider like any instrument.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Prs : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _prs        = new();
    private readonly ChunkedSeries<double> _prsPercent = new();
    private IndicatorOutputSeries? _pW, _pctW;

    private double[] _evalRing = null!;
    private double[] _baseRing = null!;
    private int _ringHead, _ringCount;
    private int _barIdx;

    // Secondary benchmark series, subscribed via AddDataSeries. The spec differs from the
    // primary only by InstrumentId, so the resolved local index is found by spec match.
    private BarSpecification? _benchSpec;
    private int _benchIdx = -1;
    private int _benchConsumed;            // oldest-first offset into the benchmark bars
    private double _lastBench = double.NaN; // latest benchmark close at-or-before the current bar

    public Prs(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("PRS",        _prs,        PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, null) });
        AddPlot(new Plot("PRSPercent", _prsPercent, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Percent, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "LookbackPeriods",
        Description = "Periods for the PRS% lookback. 0 disables PRS%.",
        MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriods { get; set; } = 0;

    [IndicatorParameter(DisplayName = "BenchmarkSymbol",
        Description = "The benchmark/comparison instrument the input is measured against (e.g. ES for the S&P 500 e-mini). Resolved by your data provider like any symbol. Blank = PRS stays empty.",
        Editor = ParameterEditorKind.SymbolPicker)]
    public string BenchmarkSymbol { get; set; } = "ES";

    /// <summary>Optional host-injected base/benchmark series (index-aligned with the
    /// primary, newest = [0]). When set, takes precedence over <see cref="BenchmarkSymbol"/>.</summary>
    public ISeries<double>? BaseSeries { get; set; }

    public ISeries<double> Output     => _pW   ??= new IndicatorOutputSeries(_prs,        Bars);
    public ISeries<double> PrsPercent => _pctW ??= new IndicatorOutputSeries(_prsPercent, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        int ringSize = Math.Max(1, LookbackPeriods + 1);
        _evalRing = new double[ringSize];
        _baseRing = new double[ringSize];
        _ringHead = _ringCount = 0;
        _barIdx = 0;
        _benchSpec = null;
        _benchIdx = -1;
        _benchConsumed = 0;
        _lastBench = double.NaN;

        // Subscribe the benchmark series — only when the host didn't inject one directly and
        // the symbol is set. The spec is the chart's own (period/type) re-pointed at the
        // benchmark symbol; the provider resolves symbol → venue/exchange. Any failure
        // (consumer forbids extra series, etc.) degrades to the NaN fallback.
        if (BaseSeries == null && !string.IsNullOrWhiteSpace(BenchmarkSymbol) && Bars?.Spec is { } spec)
        {
            try
            {
                // Different instrument → strip the chart's continuous intent (see Beta:
                // a stale ContinuousRoot would bind the chart's own root instead).
                _benchSpec = spec.WithInstrumentId(BenchmarkSymbol.Trim())
                    .WithContinuous(null, spec.ContinuousAdjustment);
                _ = ctx.AddDataSeries(_benchSpec);
            }
            catch (Exception ex)
            {
                ctx.LogWarning($"PRS: couldn't subscribe benchmark '{BenchmarkSymbol}' — {ex.Message}. PRS stays empty.");
                _benchSpec = null;
            }
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        // Only the primary series advances PRS; secondary (benchmark) live bars are absorbed
        // lazily on the next primary bar via the alignment pointer.
        if (ctx.BarsInProgress != 0) return;
        var primary = Bars;
        if (primary == null || primary.Count == 0 || Input == null || Input.Count == 0)
        { _prs.Append(double.NaN); _prsPercent.Append(double.NaN); return; }

        ResolveIndex(ctx);
        double baseV = SampleBase(ctx, primary[0], newest: true, barsAgo: 0);
        Compute(Input[0], baseV);
        LogLiveDiag(ctx, Input[0], baseV);
    }

    // ── TEMPORARY DIAGNOSTIC (remove once verified) ─────────────────────────
    // Logs the benchmark alignment on the first few LIVE bars so a live session can
    // confirm the benchmark series carries the BENCHMARK's price (ratio ≈ real), not the
    // chart instrument's (ratio == 1, the cross-instrument tick-routing bug). Gated to a
    // handful of bars so it never spams the activity log.
    private int _diagLiveBars;
    private void LogLiveDiag(IIndicatorContext ctx, double eval, double baseV)
    {
        if (_diagLiveBars >= 6) return;
        _diagLiveBars++;
        var bench = BenchmarkBars(ctx);
        double benchClose = bench != null && bench.Count > 0 ? bench[0].Close : double.NaN;
        double ratio = baseV == 0 ? double.NaN : eval / baseV;
        ctx.Print(
            $"[PRS-DIAG] live #{_diagLiveBars}: seriesCount={ctx.SeriesCount} benchIdx={_benchIdx} " +
            $"benchInstr={_benchSpec?.InstrumentId ?? "<none>"} benchCount={(bench?.Count ?? -1)} " +
            $"benchClose={benchClose:R} input={eval:R} baseV={baseV:R} ratio={ratio:R}");
    }

    /// <summary>Recompute every bar from scratch — used at load and when the benchmark series
    /// becomes ready. Leaves the ring / alignment state at the post-last-bar position so
    /// subsequent incremental live appends continue it (live == backfill).</summary>
    private void FullRebuild(IIndicatorContext ctx)
    {
        ResolveIndex(ctx);
        ResetCompute();

        var primary = Bars;
        if (primary == null) return;
        int n = Math.Min(primary.Count, Input?.Count ?? 0);
        for (int k = 0; k < n; k++)
        {
            int barsAgo = n - 1 - k;             // oldest-first
            double baseV = SampleBase(ctx, primary[barsAgo], newest: false, barsAgo: barsAgo);
            Compute(Input![barsAgo], baseV);
        }
    }

    /// <summary>Base value aligned to <paramref name="primaryBar"/>: a host-injected series
    /// sampled by index, or the latest benchmark close at-or-before this bar's close (carried
    /// forward across gaps), or NaN when neither exists.</summary>
    private double SampleBase(IIndicatorContext ctx, in Bar primaryBar, bool newest, int barsAgo)
    {
        if (BaseSeries != null)
        {
            int i = newest ? 0 : barsAgo;
            return i >= 0 && i < BaseSeries.Count ? BaseSeries[i] : double.NaN;
        }

        var bench = BenchmarkBars(ctx);
        if (bench != null)
        {
            DateTime pEnd = primaryBar.EndUtc;
            int count = bench.Count;
            while (_benchConsumed < count && bench[count - 1 - _benchConsumed].EndUtc <= pEnd)
            { _lastBench = bench[count - 1 - _benchConsumed].Close; _benchConsumed++; }
            return _lastBench;
        }

        return double.NaN;
    }

    private void Compute(double eval, double baseV)
    {
        // No benchmark for this bar → empty point (don't poison the ring / lookback math).
        if (double.IsNaN(eval) || double.IsNaN(baseV))
        { _prs.Append(double.NaN); _prsPercent.Append(double.NaN); return; }

        _evalRing[_ringHead] = eval;
        _baseRing[_ringHead] = baseV;
        _ringHead = (_ringHead + 1) % _evalRing.Length;
        if (_ringCount < _evalRing.Length) _ringCount++;

        double prs = baseV == 0 ? double.NaN : eval / baseV;
        _prs.Append(prs);

        double prsPct = double.NaN;
        if (LookbackPeriods > 0 && _barIdx >= LookbackPeriods)
        {
            // Lookback values = LookbackPeriods bars ago.
            int oldestIdx = (_ringHead - 1 - LookbackPeriods + _evalRing.Length) % _evalRing.Length;
            double eo = _evalRing[oldestIdx];
            double bo = _baseRing[oldestIdx];
            if (eo != 0 && bo != 0)
            {
                double pctE = (eval  - eo) / eo;
                double pctB = (baseV - bo) / bo;
                prsPct = pctE - pctB;
            }
        }
        _prsPercent.Append(prsPct);
        _barIdx++;
    }

    private IBarSeries? BenchmarkBars(IIndicatorContext ctx)
        => _benchIdx >= 1 && _benchIdx < ctx.SeriesCount ? ctx.Bars(_benchIdx) : null;

    private void ResolveIndex(IIndicatorContext ctx)
    {
        if (_benchSpec == null || _benchIdx >= 1) return;
        for (int i = 1; i < ctx.SeriesCount; i++)
            if (ctx.Bars(i).Spec.Equals(_benchSpec)) { _benchIdx = i; return; }
    }

    private void ResetCompute()
    {
        _prs.Clear();
        _prsPercent.Clear();
        _ringHead = _ringCount = 0;
        _barIdx = 0;
        _benchConsumed = 0;
        _lastBench = double.NaN;
    }
}
