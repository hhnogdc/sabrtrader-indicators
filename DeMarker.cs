using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// DeMarker (Tom DeMark) — a bounded 0–1 momentum oscillator built
/// from the demand (rising highs) vs supply (falling lows) of recent
/// bars. Less whippy than RSI in quiet markets because it only reacts
/// to NEW extremes.
///
/// Math:
///   <code>
///   DeMax = High &gt; High[1] ? High − High[1] : 0
///   DeMin = Low  &lt; Low[1]  ? Low[1] − Low  : 0
///   DeM   = SMA(DeMax, Period) / (SMA(DeMax, Period) + SMA(DeMin, Period))
///   </code>
/// When both sums are zero (flat market) DeM = 0.5 by convention.
///
/// Defaults Period=14. Single plot (Goldenrod), 0.3 / 0.7 reference
/// lines, range 0–1, sub-panel.
/// </summary>
[IndicatorDescription("DeMarker (Tom DeMark): bounded 0-1 oscillator comparing the SMA of new-high demand (DeMax) to new-low supply (DeMin). Above 0.7 = overbought, below 0.3 = oversold; reacts only to fresh extremes so it is calmer than RSI.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DeMarker : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _maxSum = null!;
    private RollingSum _minSum = null!;
    private double? _prevHigh, _prevLow;

    public DeMarker(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("DeMarker", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0.3 oversold / 0.7 overbought") });
        AddLine("Oversold",   0.3, PlotColors.DarkCyan);
        AddLine("Overbought", 0.7, PlotColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "SMA window over DeMax / DeMin.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _maxSum = new RollingSum(Period);
        _minSum = new RollingSum(Period);
        _prevHigh = _prevLow = null;
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
        double deMax = _prevHigh is not null && bar.High > _prevHigh.Value ? bar.High - _prevHigh.Value : 0;
        double deMin = _prevLow  is not null && bar.Low  < _prevLow.Value  ? _prevLow.Value - bar.Low  : 0;
        _prevHigh = bar.High;
        _prevLow = bar.Low;

        _maxSum.Push(deMax);
        _minSum.Push(deMin);
        double denom = _maxSum.Sum + _minSum.Sum;
        _output.Append(denom == 0 ? 0.5 : _maxSum.Sum / denom);
    }
}
