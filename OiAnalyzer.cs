using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Open-Interest Analyzer — classifies each bar's open-interest change against its
/// price change into the four classic futures regimes, the standard "build-up /
/// unwinding" read:
/// <list type="bullet">
///   <item><b>Long Build-Up</b> — price up + OI up: new longs entering (bullish). State <c>+2</c>.</item>
///   <item><b>Short Covering</b> — price up + OI down: shorts buying back (bullish exit). State <c>+1</c>.</item>
///   <item><b>Short Build-Up</b> — price down + OI up: new shorts entering (bearish). State <c>-1</c>.</item>
///   <item><b>Long Unwinding</b> — price down + OI down: longs liquidating (bearish exit). State <c>-2</c>.</item>
/// </list>
/// Flat price or flat OI (and any bar lacking OI) is <b>Neutral</b>, State <c>0</c>.
///
/// <para>Two sub-panel plots: <c>DeltaOI</c> (the raw OI change ΔOI as a histogram)
/// and <c>OIState</c> (the regime code above as dots, ordered most-bullish <c>+2</c>
/// to most-bearish <c>-2</c>). The numeric state is the machine-readable
/// classification — there is no per-bar plot colour channel in the engine, so the
/// regime is surfaced as the coded value rather than as histogram colouring.</para>
///
/// <para>Reads the per-bar Open-Interest channel
/// (<see cref="RequiresOpenInterestAttribute"/>). Warmup and bars without two
/// consecutive OI values emit <see cref="double.NaN"/> on both plots; on feeds with
/// no Open Interest both plots are empty.</para>
/// </summary>
[IndicatorDescription("Open-Interest Analyzer: classifies each bar by OI change vs price change into Long Build-Up (+2), Short Covering (+1), Short Build-Up (-1) or Long Unwinding (-2). Plots ΔOI (histogram) and the regime code (dots). Requires a feed that supplies Open Interest.")]
[RequiresOpenInterest]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class OiAnalyzer : IndicatorBase, IIndicatorPanelHint
{
    public const double LongBuildUp = 2;
    public const double ShortCovering = 1;
    public const double Neutral = 0;
    public const double ShortBuildUp = -1;
    public const double LongUnwinding = -2;

    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _delta = new();
    private readonly ChunkedSeries<double> _state = new();
    private IndicatorOutputSeries? _deltaView, _stateView;

    private double _prevOi;
    private double _prevClose;
    private bool _hasPrev;

    public OiAnalyzer(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("DeltaOI", _delta, PlotStyle.Histogram, NTColors.DodgerBlue, 1.0)
        {
            Semantics = new PlotSemantics(PlotKind.Histogram, null, null,
                PlotValueUnit.Raw, PlotBullishDirection.None, "0 centerline")
        });
        AddPlot(new Plot("OIState", _state, PlotStyle.Dots, NTColors.Goldenrod, 2.0)
        {
            Semantics = new PlotSemantics(PlotKind.Oscillator, -2, 2,
                PlotValueUnit.Raw, PlotBullishDirection.Up,
                "+2 long build-up / +1 short covering / -1 short build-up / -2 long unwinding")
        });
    }

    /// <summary>Per-bar OI change (ΔOI). NaN on warmup / missing OI.</summary>
    public ISeries<double> DeltaOi => _deltaView ??= new IndicatorOutputSeries(_delta, Bars);

    /// <summary>Per-bar regime code (see the class constants). NaN on warmup / missing OI.</summary>
    public ISeries<double> State => _stateView ??= new IndicatorOutputSeries(_state, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _prevOi = double.NaN;
        _prevClose = double.NaN;
        _hasPrev = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        var oi = ctx.OpenInterest(0);
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            Consume(bars[barsAgo].Close, ReadOi(oi, barsAgo));
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        Consume(bars[0].Close, ReadOi(ctx.OpenInterest(0), 0));
    }

    private void Consume(double close, double oi)
    {
        double dOi = (_hasPrev && !double.IsNaN(oi) && !double.IsNaN(_prevOi)) ? oi - _prevOi : double.NaN;
        double dPrice = _hasPrev ? close - _prevClose : double.NaN;

        double state, delta;
        if (double.IsNaN(dOi) || double.IsNaN(dPrice))
        {
            delta = double.NaN;
            state = double.NaN;
        }
        else
        {
            delta = dOi;
            state = Classify(Math.Sign(dOi), Math.Sign(dPrice));
        }

        _delta.Append(delta);
        _state.Append(state);

        _prevOi = oi;       // may be NaN — a gap forces the next bar's ΔOI to NaN
        _prevClose = close;
        _hasPrev = true;
    }

    private static double Classify(int signOi, int signPrice)
    {
        if (signOi > 0) return signPrice > 0 ? LongBuildUp : signPrice < 0 ? ShortBuildUp : Neutral;
        if (signOi < 0) return signPrice > 0 ? ShortCovering : signPrice < 0 ? LongUnwinding : Neutral;
        return Neutral;   // flat OI
    }

    /// <summary>Defensive read: NaN when the OI channel has no value at this offset.</summary>
    private static double ReadOi(ISeries<double> oi, int barsAgo)
        => oi.TryGet(barsAgo, out var value) ? value : double.NaN;
}
