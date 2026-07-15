using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// McClellan Oscillator — market-breadth momentum gauge.
/// Value = EMA(Adv−Decl, Fast) − EMA(Adv−Decl, Slow), where Adv / Decl are the
/// advancing- and declining-issues counts for a breadth index.
///
/// <para><b>Venue- and consumer-agnostic.</b> The indicator does NOT know any
/// provider's symbols. It declares two breadth series by their
/// <see cref="AdvanceSymbol"/> / <see cref="DeclineSymbol"/> strings and
/// subscribes to them through the universal
/// <see cref="IIndicatorContext.AddDataSeries"/> seam — the SAME bar
/// specification as the chart (period / type) re-pointed at each symbol via
/// <see cref="BarSpecification.WithInstrumentId"/>. Resolving a symbol to its
/// venue + exchange is the data provider's job (the instrument catalog), exactly
/// like the primary series. Suggested values per feed are documentation, not code:
/// e.g. on a Rithmic feed the NASDAQ breadth indices are <c>$adv</c> / <c>$dec</c>
/// (probe-confirmed); other feeds use their own tickers.</para>
///
/// <para><b>Graceful fallback.</b> When the symbols are blank, the host consumer
/// refuses additional series (some declarative strategy hosts do — see
/// <c>AdditionalDataSeriesRejectionReason</c>), or the breadth data simply hasn't
/// arrived for a bar, the oscillator emits <see cref="double.NaN"/> for that bar
/// (an empty panel) rather than a misleading flat 0 line.</para>
///
/// <para>A host MAY instead inject the two series directly via
/// <see cref="AdvanceSeries"/> / <see cref="DeclineSeries"/> (index-aligned with
/// the primary, newest = [0]); when set, those take precedence and no secondary
/// subscription is made. Defaults Fast=19, Slow=39. LimeGreen plot, 70/-70 DarkCyan
/// over-bought / oversold lines, 0 DarkGray zero line.</para>
/// </summary>
[IndicatorDescription("McClellan Oscillator: difference between a fast and slow EMA of advancing minus declining issues. A market-breadth momentum gauge; +/-70 mark overbought/oversold extremes. Needs two breadth series (advancing/declining issues) supplied via the AdvanceSymbol/DeclineSymbol settings — resolved by your data provider like any instrument.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class McClellanOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    // Secondary breadth series, subscribed via AddDataSeries. Spec differs from the
    // primary only by InstrumentId, so the resolved local index is found by spec match.
    private BarSpecification? _advSpec, _decSpec;
    private int _advIdx = -1, _decIdx = -1;

    // Running EMA + alignment state (persist across incremental live appends so a
    // one-bar-at-a-time live build reproduces a one-shot backfill exactly).
    private double _k1, _k2;
    private double _emaFast, _emaSlow;
    private bool _seeded;
    private double _lastAdv = double.NaN, _lastDec = double.NaN;
    private int _advConsumed, _decConsumed;   // oldest-first offsets into the secondary bars

    public McClellanOscillator(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("McClellan", _output, PlotStyle.Line, NTColors.LimeGreen, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Overbought", 70,  NTColors.DarkCyan);
        AddLine("Oversold",  -70,  NTColors.DarkCyan);
        AddLine("Zero line",   0,  NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "FastPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int FastPeriod { get; set; } = 19;

    [IndicatorParameter(DisplayName = "SlowPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SlowPeriod { get; set; } = 39;

    [IndicatorParameter(DisplayName = "AdvanceSymbol",
        Description = "Symbol of the advancing-issues breadth series, as your data provider names it (e.g. Rithmic NASDAQ: $adv). Blank = oscillator stays empty.")]
    public string AdvanceSymbol { get; set; } = "$adv";

    [IndicatorParameter(DisplayName = "DeclineSymbol",
        Description = "Symbol of the declining-issues breadth series (e.g. Rithmic NASDAQ: $dec). Blank = oscillator stays empty.")]
    public string DeclineSymbol { get; set; } = "$dec";

    public ChartColor NegativeColor { get; set; } = NTColors.Red;

    /// <summary>Optional host-injected advancing-issues series (index-aligned with the
    /// primary, newest = [0]). When set, takes precedence over <see cref="AdvanceSymbol"/>.</summary>
    public ISeries<double>? AdvanceSeries { get; set; }

    /// <summary>Optional host-injected declining-issues series. See <see cref="AdvanceSeries"/>.</summary>
    public ISeries<double>? DeclineSeries { get; set; }

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (FastPeriod < 1 || SlowPeriod < 1) throw new ArgumentOutOfRangeException();
        _k1 = 2.0 / (FastPeriod + 1);
        _k2 = 2.0 / (SlowPeriod + 1);
        _advSpec = _decSpec = null;
        _advIdx = _decIdx = -1;

        // Subscribe the breadth series — only when the host didn't inject them directly
        // and both symbols are set. The spec is the chart's own (period/type) re-pointed
        // at each breadth symbol; the provider resolves symbol → venue/exchange. Any
        // failure (consumer forbids extra series, etc.) degrades to the NaN fallback.
        bool injected = AdvanceSeries != null && DeclineSeries != null;
        if (!injected
            && !string.IsNullOrWhiteSpace(AdvanceSymbol)
            && !string.IsNullOrWhiteSpace(DeclineSymbol)
            && Bars?.Spec is { } spec)
        {
            try
            {
                // Different instruments → strip the chart's continuous intent (see Beta:
                // a stale ContinuousRoot would bind the chart's own root instead).
                _advSpec = spec.WithInstrumentId(AdvanceSymbol.Trim())
                    .WithContinuous(null, spec.ContinuousAdjustment);
                _decSpec = spec.WithInstrumentId(DeclineSymbol.Trim())
                    .WithContinuous(null, spec.ContinuousAdjustment);
                _ = ctx.AddDataSeries(_advSpec);
                _ = ctx.AddDataSeries(_decSpec);
            }
            catch (Exception ex)
            {
                ctx.LogWarning(
                    $"McClellan: couldn't subscribe breadth series " +
                    $"'{AdvanceSymbol}' / '{DeclineSymbol}' — {ex.Message}. Oscillator stays empty.");
                _advSpec = _decSpec = null;
            }
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnDataSeriesReady(int seriesIndex, IIndicatorContext ctx) => FullRebuild(ctx);

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        // Only the primary series advances the oscillator; secondary (breadth) live
        // bars are absorbed lazily on the next primary bar via the alignment pointers.
        if (ctx.BarsInProgress != 0) return;
        var primary = Bars;
        if (primary == null || primary.Count == 0) return;
        ResolveIndices(ctx);

        (IBarSeries? advBars, IBarSeries? decBars) = SecondaryBars(ctx);
        Bar nb = primary[0];
        var (adv, dec) = SampleBreadth(in nb, advBars, decBars, newestOverrideIndex: true);
        Emit(adv, dec);
    }

    /// <summary>Recompute every bar from scratch — used at load and when a breadth
    /// series becomes ready. Leaves the running EMA / alignment state at the
    /// post-last-bar position so subsequent incremental live appends continue it.</summary>
    private void FullRebuild(IIndicatorContext ctx)
    {
        ResolveIndices(ctx);
        _output.Clear();
        ResetCompute();

        var primary = Bars;
        if (primary == null || primary.Count == 0) return;
        (IBarSeries? advBars, IBarSeries? decBars) = SecondaryBars(ctx);

        int n = primary.Count;
        for (int k = 0; k < n; k++)
        {
            int barsAgo = n - 1 - k;             // oldest-first
            Bar pb = primary[barsAgo];
            var (adv, dec) = SampleBreadth(in pb, advBars, decBars, newestOverrideIndex: false, overrideBarsAgo: barsAgo);
            Emit(adv, dec);
        }
    }

    /// <summary>Advancing / declining value aligned to <paramref name="primaryBar"/>:
    /// the latest breadth bar at-or-before this bar's close (carried forward across
    /// gaps), or a host-injected series sampled by index, or NaN when neither exists.</summary>
    private (double Adv, double Dec) SampleBreadth(
        in Bar primaryBar, IBarSeries? advBars, IBarSeries? decBars,
        bool newestOverrideIndex, int overrideBarsAgo = 0)
    {
        if (advBars != null && decBars != null)
        {
            DateTime pEnd = primaryBar.EndUtc;
            int advCount = advBars.Count, decCount = decBars.Count;
            while (_advConsumed < advCount && advBars[advCount - 1 - _advConsumed].EndUtc <= pEnd)
            { _lastAdv = advBars[advCount - 1 - _advConsumed].Close; _advConsumed++; }
            while (_decConsumed < decCount && decBars[decCount - 1 - _decConsumed].EndUtc <= pEnd)
            { _lastDec = decBars[decCount - 1 - _decConsumed].Close; _decConsumed++; }
            return (_lastAdv, _lastDec);
        }

        if (AdvanceSeries != null && DeclineSeries != null)
        {
            int ai = newestOverrideIndex ? 0 : overrideBarsAgo;
            int di = ai;
            double a = ai >= 0 && ai < AdvanceSeries.Count ? AdvanceSeries[ai] : double.NaN;
            double d = di >= 0 && di < DeclineSeries.Count ? DeclineSeries[di] : double.NaN;
            return (a, d);
        }

        return (double.NaN, double.NaN);
    }

    /// <summary>Fold one (adv, dec) sample into the running EMAs and append the
    /// oscillator value, or NaN when the breadth pair is unavailable for this bar.</summary>
    private void Emit(double adv, double dec)
    {
        if (double.IsNaN(adv) || double.IsNaN(dec)) { _output.Append(double.NaN); return; }
        double spread = adv - dec;
        if (!_seeded) { _emaFast = spread; _emaSlow = spread; _seeded = true; }
        else { _emaFast += _k1 * (spread - _emaFast); _emaSlow += _k2 * (spread - _emaSlow); }
        _output.Append(_emaFast - _emaSlow);
    }

    private (IBarSeries? Adv, IBarSeries? Dec) SecondaryBars(IIndicatorContext ctx)
    {
        IBarSeries? adv = _advIdx >= 1 && _advIdx < ctx.SeriesCount ? ctx.Bars(_advIdx) : null;
        IBarSeries? dec = _decIdx >= 1 && _decIdx < ctx.SeriesCount ? ctx.Bars(_decIdx) : null;
        return (adv != null && dec != null) ? (adv, dec) : (null, null);
    }

    private void ResolveIndices(IIndicatorContext ctx)
    {
        if (_advSpec == null || _decSpec == null) return;
        if (_advIdx >= 1 && _decIdx >= 1) return;
        for (int i = 1; i < ctx.SeriesCount; i++)
        {
            var s = ctx.Bars(i).Spec;
            if (_advIdx < 1 && s.Equals(_advSpec)) _advIdx = i;
            else if (_decIdx < 1 && s.Equals(_decSpec)) _decIdx = i;
        }
    }

    private void ResetCompute()
    {
        _emaFast = _emaSlow = 0;
        _seeded = false;
        _lastAdv = _lastDec = double.NaN;
        _advConsumed = _decConsumed = 0;
    }
}
