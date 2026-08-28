using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum BetaType { Standard, Up, Down, All }

/// <summary>
/// Beta coefficient — derived from Skender.Stock.Indicators (Apache-2.0).
/// Compares the evaluated asset's per-bar returns against the market benchmark's returns
/// over a rolling window:
///   beta = Covariance(market, eval) / Variance(market)
/// Variants: Standard (all bars), Up (only bars where market return &gt; 0), Down (only &lt; 0),
/// All (computes Standard + Up + Down).
///
/// <para><b>Self-wiring benchmark.</b> Beta needs a market benchmark instrument. It declares
/// that benchmark by its <see cref="BenchmarkSymbol"/> string and subscribes to it through the
/// universal <see cref="IIndicatorContext.AddDataSeries"/> seam — the chart's own bar spec
/// (period/type) re-pointed at the benchmark via
/// <see cref="BarSpecification.WithInstrumentId"/>; the data provider resolves the symbol →
/// venue/exchange exactly like the primary series. The benchmark is time-aligned to the primary
/// (latest benchmark close at-or-before each bar, carried forward across gaps). Same
/// venue/consumer-agnostic pattern as the McClellan oscillator's breadth series.</para>
///
/// <para>When the benchmark symbol is blank / doesn't resolve, the indicator emits
/// <see cref="double.NaN"/> (an empty panel) rather than a misleading line. A host MAY instead
/// inject the market series directly via <see cref="BenchmarkSeries"/> (index-aligned with the
/// primary, newest = [0]); when set it takes precedence and no subscription is made.</para>
///
/// Defaults LookbackPeriods=14, Type=Standard, BenchmarkSymbol="ES".
/// </summary>
[IndicatorDescription("Beta coefficient. Rolling covariance of the instrument's returns vs a benchmark divided by benchmark variance: Beta>1 = more volatile than the market, <1 = less. Up/Down/All variants isolate up-market and down-market beta. Pick the benchmark via the BenchmarkSymbol setting.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Beta : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _beta     = new();
    private readonly ChunkedSeries<double> _betaUp   = new();
    private readonly ChunkedSeries<double> _betaDown = new();
    private IndicatorOutputSeries? _bW, _uW, _dW;

    private double[] _evalReturns = null!;
    private double[] _mrktReturns = null!;
    private double _prevE, _prevM;
    private int _barIdx;

    // Secondary benchmark series, subscribed via AddDataSeries. The spec differs from the
    // primary only by InstrumentId, so the resolved local index is found by spec match.
    private BarSpecification? _benchSpec;
    private int _benchIdx = -1;
    private int _benchConsumed;
    private double _lastBench = double.NaN;

    public Beta(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Beta",     _beta,     PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, null) });
        AddPlot(new Plot("BetaUp",   _betaUp,   PlotStyle.Line, PlotColors.LimeGreen,  1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("BetaDown", _betaDown, PlotStyle.Line, PlotColors.Red,        1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "LookbackPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriods { get; set; } = 14;

    [IndicatorParameter(DisplayName = "BetaType")]
    public BetaType BetaType { get; set; } = BetaType.Standard;

    [IndicatorParameter(DisplayName = "BenchmarkSymbol",
        Description = "The market benchmark instrument beta is measured against (e.g. ES for the S&P 500 e-mini). Resolved by your data provider like any symbol. Blank = beta stays empty.",
        Editor = ParameterEditorKind.SymbolPicker)]
    public string BenchmarkSymbol { get; set; } = "ES";

    /// <summary>Optional host-injected market benchmark series (index-aligned with the primary,
    /// newest = [0]). When set, takes precedence over <see cref="BenchmarkSymbol"/>.</summary>
    public ISeries<double>? BenchmarkSeries { get; set; }

    public ISeries<double> Output   => _bW ??= new IndicatorOutputSeries(_beta,     Bars);
    public ISeries<double> BetaUp   => _uW ??= new IndicatorOutputSeries(_betaUp,   Bars);
    public ISeries<double> BetaDown => _dW ??= new IndicatorOutputSeries(_betaDown, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LookbackPeriods < 1) throw new ArgumentOutOfRangeException(nameof(LookbackPeriods));
        _evalReturns = new double[LookbackPeriods];
        _mrktReturns = new double[LookbackPeriods];
        _prevE = _prevM = 0;
        _barIdx = 0;
        _benchSpec = null;
        _benchIdx = -1;
        _benchConsumed = 0;
        _lastBench = double.NaN;

        if (BenchmarkSeries == null && !string.IsNullOrWhiteSpace(BenchmarkSymbol) && Bars?.Spec is { } spec)
        {
            try
            {
                // The benchmark is a DIFFERENT instrument: the chart spec's continuous
                // intent (ContinuousRoot) must not ride along, or on a continuous chart
                // the "benchmark" series binds the chart's own root instead of the
                // benchmark symbol (the stale-ContinuousRoot bug class).
                _benchSpec = spec.WithInstrumentId(BenchmarkSymbol.Trim())
                    .WithContinuous(null, spec.ContinuousAdjustment);
                _ = ctx.AddDataSeries(_benchSpec);
            }
            catch (Exception ex)
            {
                ctx.LogWarning($"Beta: couldn't subscribe benchmark '{BenchmarkSymbol}' — {ex.Message}. Beta stays empty.");
                _benchSpec = null;
            }
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (ctx.BarsInProgress != 0) return;
        var primary = Bars;
        if (primary == null || primary.Count == 0 || Input == null || Input.Count == 0)
        { _beta.Append(double.NaN); _betaUp.Append(double.NaN); _betaDown.Append(double.NaN); return; }

        ResolveIndex(ctx);
        double mrkt = SampleBenchmark(ctx, primary[0], newest: true, barsAgo: 0);
        Compute(Input[0], mrkt);
    }

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
            double mrkt = SampleBenchmark(ctx, primary[barsAgo], newest: false, barsAgo: barsAgo);
            Compute(Input![barsAgo], mrkt);
        }
    }

    private double SampleBenchmark(IIndicatorContext ctx, in Bar primaryBar, bool newest, int barsAgo)
    {
        if (BenchmarkSeries != null)
        {
            int i = newest ? 0 : barsAgo;
            return i >= 0 && i < BenchmarkSeries.Count ? BenchmarkSeries[i] : double.NaN;
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

    private void Compute(double eval, double mrkt)
    {
        // No benchmark for this bar → empty point (don't advance the return state with NaN).
        if (double.IsNaN(eval) || double.IsNaN(mrkt))
        { _beta.Append(double.NaN); _betaUp.Append(double.NaN); _betaDown.Append(double.NaN); return; }

        double er = _prevE != 0 ? eval / _prevE - 1 : 0;
        double mr = _prevM != 0 ? mrkt / _prevM - 1 : 0;
        _prevE = eval; _prevM = mrkt;

        // Push into ring.
        int slot = _barIdx % LookbackPeriods;
        _evalReturns[slot] = er;
        _mrktReturns[slot] = mr;

        if (_barIdx + 1 < LookbackPeriods)
        {
            _beta.Append(double.NaN); _betaUp.Append(double.NaN); _betaDown.Append(double.NaN);
            _barIdx++;
            return;
        }

        double beta     = ComputeBeta(BetaSelection.Standard);
        double betaUp   = BetaType == BetaType.Up   || BetaType == BetaType.All ? ComputeBeta(BetaSelection.Up)   : double.NaN;
        double betaDown = BetaType == BetaType.Down || BetaType == BetaType.All ? ComputeBeta(BetaSelection.Down) : double.NaN;

        _beta.Append(BetaType == BetaType.Standard || BetaType == BetaType.All ? beta : double.NaN);
        _betaUp.Append(betaUp);
        _betaDown.Append(betaDown);
        _barIdx++;
    }

    private enum BetaSelection { Standard, Up, Down }

    private double ComputeBeta(BetaSelection sel)
    {
        // Walk the ring, optionally filtering by market-return direction.
        double sumM = 0, sumE = 0;
        int count = 0;
        Span<double> mBuf = stackalloc double[LookbackPeriods];
        Span<double> eBuf = stackalloc double[LookbackPeriods];
        for (int k = 0; k < LookbackPeriods; k++)
        {
            double m = _mrktReturns[k], e = _evalReturns[k];
            if (sel == BetaSelection.Up   && m <= 0) continue;
            if (sel == BetaSelection.Down && m >= 0) continue;
            mBuf[count] = m; eBuf[count] = e; sumM += m; sumE += e; count++;
        }
        if (count < 2) return double.NaN;
        double meanM = sumM / count, meanE = sumE / count;
        double cov = 0, varM = 0;
        for (int k = 0; k < count; k++)
        {
            double dm = mBuf[k] - meanM, de = eBuf[k] - meanE;
            cov += dm * de;
            varM += dm * dm;
        }
        return varM == 0 ? double.NaN : cov / varM;
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
        _beta.Clear();
        _betaUp.Clear();
        _betaDown.Clear();
        _prevE = _prevM = 0;
        _barIdx = 0;
        _benchConsumed = 0;
        _lastBench = double.NaN;
    }
}
