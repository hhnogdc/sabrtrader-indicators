using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Balance OI — the net Open-Interest flow attributed by price direction over a
/// rolling window. Each bar contributes its OI change signed by the bar's price
/// move: <c>+ΔOI</c> on up bars, <c>−ΔOI</c> on down bars, <c>0</c> on flat bars;
/// the plot is the sum of those contributions over the last <see cref="Lookback"/>
/// bars (an O(1) rolling sum).
///
/// <para>The single, consistent reading across the four regimes:
/// <list type="bullet">
///   <item>Long build-up (price up, OI up) adds a <b>bullish</b> balance (+).</item>
///   <item>Short build-up (price down, OI up) adds a <b>bearish</b> balance (−).</item>
///   <item>Long unwinding (price down, OI down) reads as bullish exhaustion / capitulation (+).</item>
///   <item>Short covering (price up, OI down) reads as less-genuine demand (−).</item>
/// </list>
/// Positive balance = open interest is accumulating with the up-moves; negative =
/// accumulating with the down-moves.</para>
///
/// <para>Reads the per-bar Open-Interest channel
/// (<see cref="RequiresOpenInterestAttribute"/>). Emits <see cref="double.NaN"/>
/// during warmup (until <see cref="Lookback"/>+1 bars are seen) and on any bar whose
/// OI change can't be computed (no OI this bar or the previous) — so on feeds without
/// Open Interest the plot is empty rather than a flat zero line.</para>
/// </summary>
[IndicatorDescription("Balance OI: rolling net Open-Interest flow signed by price direction (+ΔOI on up bars, -ΔOI on down bars) summed over the lookback. Positive = OI building with up-moves, negative = with down-moves. Requires a feed that supplies Open Interest.")]
[RequiresOpenInterest]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BalanceOi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputView;

    private double[] _ring = Array.Empty<double>();
    private int _ringPos;
    private double _sum;
    private int _barsSeen;
    private double _prevOi;
    private double _prevClose;
    private bool _hasPrev;

    public BalanceOi(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("BalanceOI", _output, PlotStyle.Histogram, NTColors.DodgerBlue, 1.0)
        {
            Semantics = new PlotSemantics(PlotKind.Oscillator, null, null,
                PlotValueUnit.Raw, PlotBullishDirection.Up, "0 centerline")
        });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Lookback",
        Description = "Number of bars in the rolling net-OI-flow window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1, Group = "Calculation")]
    public int Lookback { get; set; } = 14;

    /// <summary>Per-bar Balance OI as a bar-sourced series (chainable as input).</summary>
    public ISeries<double> Output => _outputView ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Lookback < 1) throw new ArgumentOutOfRangeException(nameof(Lookback));
        _ring = new double[Lookback];
        _ringPos = 0;
        _sum = 0;
        _barsSeen = 0;
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
            _output.Append(Consume(bars[barsAgo].Close, ReadOi(oi, barsAgo)));
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        _output.Append(Consume(bars[0].Close, ReadOi(ctx.OpenInterest(0), 0)));
    }

    private double Consume(double close, double oi)
    {
        double dOi = (_hasPrev && !double.IsNaN(oi) && !double.IsNaN(_prevOi)) ? oi - _prevOi : double.NaN;
        double dPrice = _hasPrev ? close - _prevClose : double.NaN;
        bool valid = !double.IsNaN(dOi) && !double.IsNaN(dPrice);

        // Contribution: +ΔOI on up bars, -ΔOI on down bars, 0 on flat / invalid.
        double contribution = valid ? Math.Sign(dPrice) * dOi : 0;

        // O(1) rolling sum over the last Lookback contributions.
        _sum -= _ring[_ringPos];
        _ring[_ringPos] = contribution;
        _sum += contribution;
        _ringPos = (_ringPos + 1) % _ring.Length;
        _barsSeen++;

        _prevOi = oi;       // may be NaN — a gap forces the next bar's ΔOI to NaN
        _prevClose = close;
        _hasPrev = true;

        // NaN during warmup (fewer than Lookback deltas) and on any bar without a
        // computable OI change, so feeds without OI render an empty plot.
        return (_barsSeen > Lookback && valid) ? _sum : double.NaN;
    }

    /// <summary>Defensive read: NaN when the OI channel has no value at this offset.</summary>
    private static double ReadOi(ISeries<double> oi, int barsAgo)
        => oi.TryGet(barsAgo, out var value) ? value : double.NaN;
}
