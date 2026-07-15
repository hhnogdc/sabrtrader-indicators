using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Open Interest — the venue-reported number of outstanding contracts, plotted raw
/// as one value per bar. Open Interest is a futures/options market-structure figure
/// (rising OI = new positions opening, falling OI = positions closing) that is
/// reported alongside OHLCV, not derived from it.
///
/// <para>OI rides the pipeline's per-bar Open-Interest channel
/// (<see cref="IIndicatorContext.OpenInterest"/>); this indicator opts in with
/// <see cref="RequiresOpenInterestAttribute"/>. Bars for which the feed reported no
/// OI emit <see cref="double.NaN"/> (a gap in the line). On data feeds that don't
/// supply Open Interest the whole plot is empty — the channel is all-NaN — so the
/// indicator surfaces "no OI available" rather than a misleading zero line.</para>
///
/// <para>No parameters, single sub-panel line.</para>
/// </summary>
[IndicatorDescription("Open Interest: the venue-reported count of outstanding futures/options contracts, plotted raw per bar. Rising OI = new positions opening, falling = closing. Requires a data feed that supplies Open Interest; bars without it show as gaps.")]
[RequiresOpenInterest]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class OpenInterest : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputView;

    /// <summary>
    /// OI needs the bar series (to align the channel) plus the per-bar OI channel.
    /// Pass a non-default <paramref name="bars"/> for an MTF Open-Interest plot.
    /// </summary>
    public OpenInterest(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("OpenInterest", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0)
        {
            Semantics = new PlotSemantics(PlotKind.Oscillator, null, null,
                PlotValueUnit.Raw, PlotBullishDirection.None, null)
        });
    }

    /// <summary>Per-bar Open Interest as a bar-sourced series (chainable as input).</summary>
    public ISeries<double> Output => _outputView ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        var oi = ctx.OpenInterest(0);
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            _output.Append(ReadOi(oi, barsAgo));
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        _output.Append(ReadOi(ctx.OpenInterest(0), 0));
    }

    /// <summary>Defensive read: NaN when the OI channel has no value at this offset.</summary>
    private static double ReadOi(ISeries<double> oi, int barsAgo)
        => oi.TryGet(barsAgo, out var value) ? value : double.NaN;
}
