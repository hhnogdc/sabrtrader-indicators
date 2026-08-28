using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ease of Movement.
/// midPoint = Median[0] − Median[1]; Median = (H+L)/2.
/// boxRatio = Volume / VolumeDivisor / (H−L).
/// emv = boxRatio == 0 ? 0 : midPoint / boxRatio.
/// Value = EMA(emv, Smoothing).
/// Defaults Smoothing=14, VolumeDivisor=10000, DodgerBlue, zero line, sub-panel.
/// </summary>
[IndicatorDescription("Ease of Movement (Arms): EMA of midpoint-change divided by a volume/range box ratio. Positive = price rises easily on low volume, negative = falls easily.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class EaseOfMovement : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _emv = new();
    private IndicatorOutputSeries? _emvW, _wrap;
    private Ema _ema = null!;
    private double? _prevMedian;

    public EaseOfMovement(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("EMV", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Smoothing", Description = "EMA period over raw EMV.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smoothing { get; set; } = 14;

    [IndicatorParameter(DisplayName = "VolumeDivisor", Description = "Box-ratio denominator (the original default 10000).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int VolumeDivisor { get; set; } = 10000;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Smoothing < 1)     throw new ArgumentOutOfRangeException(nameof(Smoothing));
        if (VolumeDivisor < 1) throw new ArgumentOutOfRangeException(nameof(VolumeDivisor));
        _emvW = new IndicatorOutputSeries(_emv, Bars);
        // parent=null: parent-filled _emvW; avoid auto-dispatch double-processing.
        _ema = new Ema(parent: null, input: _emvW) { Period = Smoothing };
        _ema.OnInit(ctx);
        _prevMedian = null;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) PushBar(bars[bars.Count - 1 - i]);
        _ema.OnDataLoaded(ctx);
        int n = _ema.Output.Count;
        for (int i = 0; i < n; i++) _output.Append(_ema.Output[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        PushBar(Bars[0]);
        _ema.OnBarUpdate(ctx);
        _output.Append(_ema.Output[0]);
    }

    private void PushBar(Bar bar)
    {
        double median = (bar.High + bar.Low) / 2.0;
        if (_prevMedian is null) { _emv.Append(0); _prevMedian = median; return; }
        double mid = median - _prevMedian.Value;
        double hl = bar.High - bar.Low;
        double boxRatio = hl == 0 ? 0 : (double)bar.Volume / VolumeDivisor / hl;
        _emv.Append(boxRatio == 0 ? 0 : mid / boxRatio);
        _prevMedian = median;
    }
}
