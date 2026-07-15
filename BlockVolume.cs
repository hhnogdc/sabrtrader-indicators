using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Counting mode for <see cref="BlockVolume"/>. Matches NT's CountType enum.</summary>
public enum BlockVolumeCountType { Trades = 0, Volume = 1 }

/// <summary>
/// Block Volume — port of NT @BlockVolume.
///
/// NT runs at tick granularity: for every tick whose volume ≥ BlockSize,
/// add its volume (Volume mode) or 1 (Trades mode) to the bar's
/// accumulator. Our port runs at bar granularity — when a bar's
/// total volume meets the threshold we count it as one block;
/// proper tick-level accounting needs the host to feed the tick
/// series (Phase-later feature). Matches NT for the case where all
/// bars are themselves blocks (the common use of large BlockSize on
/// minute charts).
///
/// Defaults BlockSize=80, CountType=Volume, DarkRed 2px Histogram,
/// sub-panel.
/// </summary>
[IndicatorDescription("Block Volume. Highlights bars whose volume meets or exceeds the BlockSize threshold by plotting the qualifying volume (Volume mode) or a count of 1 per qualifying bar (Trades mode), surfacing large institutional prints.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BlockVolume : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    public BlockVolume(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Block Volume", _output, PlotStyle.Histogram, NTColors.DarkRed, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "BlockSize", Description = "Per-bar volume threshold.",
        MinValue = 0.00000001, MaxValue = double.MaxValue, Step = 1)]
    public double BlockSize { get; set; } = 80;

    [IndicatorParameter(DisplayName = "CountType", Description = "Trades (1 per qualifying bar) or Volume (cumulative).")]
    public BlockVolumeCountType CountType { get; set; } = BlockVolumeCountType.Volume;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    private void Consume(Bar bar)
    {
        if (bar.Volume < BlockSize) { _output.Append(0); return; }
        _output.Append(CountType == BlockVolumeCountType.Trades ? 1 : (double)bar.Volume);
    }
}
