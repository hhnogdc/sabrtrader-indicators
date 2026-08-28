using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume Oscillator.
/// Value = SMA(Volume, Fast) − SMA(Volume, Slow).
/// Defaults Fast=12, Slow=26, DodgerBlue 2px Bar plot, sub-panel.
/// CryptoCurrency volume normalisation is host-specific; the pipeline does not
/// (yet) carry instrument metadata so we skip the crypto-volume conversion.
/// </summary>
[IndicatorDescription("Volume Oscillator. The difference between a fast and slow SMA of volume, highlighting expansion or contraction of participation around the zero line.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class VolumeOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _fastSum = null!, _slowSum = null!;

    public VolumeOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Volume Oscillator", _output, PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Fast", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1) throw new ArgumentOutOfRangeException();
        _fastSum = new RollingSum(Fast);
        _slowSum = new RollingSum(Slow);
    }

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
        double v = bar.Volume;
        _fastSum.Push(v);
        _slowSum.Push(v);
        // SMAs use simple running average regardless of fill level (matches the original @SMA).
        double smaFast = _fastSum.Sum / _fastSum.Count;
        double smaSlow = _slowSum.Sum / _slowSum.Count;
        _output.Append(smaFast - smaSlow);
    }
}
