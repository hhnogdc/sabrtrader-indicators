using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Force Index — 1-on-1 port of NT @ForceIndex.
/// raw = (Close[0] − Close[1]) · Volume. Bar 0 → 0.
/// Value = EMA(raw, Period); split across Positive (DarkCyan) and
/// Negative (Crimson) histogram plots. Zero line. Sub-panel.
/// Default Period=14.
/// </summary>
[IndicatorDescription("Elder's Force Index: EMA of (ΔClose · Volume). Positive bars show net buying pressure, negative bars net selling pressure; rendered as split positive/negative histograms.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ForceIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _positive = new();
    private readonly ChunkedSeries<double> _negative = new();
    private readonly ChunkedSeries<double> _raw = new();
    private IndicatorOutputSeries? _posW, _negW, _rawW;
    private Ema _ema = null!;
    private double? _prevClose;

    public ForceIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Positive", _positive, PlotStyle.Histogram, NTColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Negative", _negative, PlotStyle.Histogram, NTColors.Crimson, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Down, null) });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period over raw force.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Positive => _posW ??= new IndicatorOutputSeries(_positive, Bars);
    public ISeries<double> Negative => _negW ??= new IndicatorOutputSeries(_negative, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _rawW = new IndicatorOutputSeries(_raw, Bars);
        // parent=null: parent-filled _rawW; avoid auto-dispatch double-processing.
        _ema = new Ema(parent: null, input: _rawW) { Period = Period };
        _ema.OnInit(ctx);
        _prevClose = null;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) PushBar(bars[bars.Count - 1 - i]);
        _ema.OnDataLoaded(ctx);
        int n = _ema.Output.Count;
        for (int i = 0; i < n; i++)
        {
            double v = _ema.Output[n - 1 - i];
            if (v >= 0) { _positive.Append(v); _negative.Append(0); }
            else        { _positive.Append(0); _negative.Append(v); }
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        PushBar(Bars[0]);
        _ema.OnBarUpdate(ctx);
        double v = _ema.Output[0];
        if (v >= 0) { _positive.Append(v); _negative.Append(0); }
        else        { _positive.Append(0); _negative.Append(v); }
    }

    private void PushBar(Bar bar)
    {
        if (_prevClose is null) { _raw.Append(0); _prevClose = bar.Close; return; }
        _raw.Append((bar.Close - _prevClose.Value) * bar.Volume);
        _prevClose = bar.Close;
    }
}
