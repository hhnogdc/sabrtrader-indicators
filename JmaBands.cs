using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// JMA Bands — a smooth, <b>non-repainting</b> volatility envelope built on the
/// Jurik Moving Average (see <see cref="Jma"/>).
///
/// This is the answer to "how do competitors get TMA-band smoothness without
/// the repaint": a centered Triangular MA looks ahead (the last half-window
/// redraws as bars print), whereas JMA is a fully causal low-lag filter — nearly
/// as smooth, but it never repaints.
///   Center = JMA(Close)
///   Width  = Multiplier · JMA(TrueRange)        ← band width is JMA-smoothed too,
///   Upper  = Center + Width                        so the envelope is as glassy
///   Lower  = Center − Width                        as the centerline.
///
/// Both the centerline and the band width share the same JMA (Length, Phase,
/// Power), so inner/outer bands ride as smoothly as the middle line.
/// Defaults Length=14, Phase=0, Power=2, Multiplier=2.
/// Plots: Center (DarkGray), Upper/Lower (DodgerBlue), overlay.
/// </summary>
[IndicatorDescription("JMA Bands: a Jurik Moving Average centerline with upper/lower bands offset by Multiplier times a JMA-smoothed True Range. A smooth, low-lag, non-repainting alternative to centered TMA bands.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class JmaBands : IndicatorBase
{
    private readonly ChunkedSeries<double> _center = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private readonly ChunkedSeries<double> _src     = new(); // Close, feeds the centerline JMA
    private readonly ChunkedSeries<double> _range   = new(); // True Range, feeds the width JMA
    private IndicatorOutputSeries? _cW, _uW, _lW, _srcW, _rangeW;

    private Jma _jmaCenter = null!;
    private Jma _jmaRange  = null!;
    private double? _prevClose;

    public JmaBands(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Center", _center, PlotStyle.Line, PlotColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Length", Description = "JMA smoothing window for both centerline and band width.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Phase", Description = "JMA lag↔overshoot trade in [−100, 100].",
        MinValue = -100, MaxValue = 100, Step = 1)]
    public double Phase { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Power", Description = "JMA adaptation power (≥ 1). Higher = smoother.",
        MinValue = 1, MaxValue = double.MaxValue, Step = 1)]
    public double Power { get; set; } = 2;

    [IndicatorParameter(DisplayName = "Multiplier", Description = "Band width multiplier on JMA(True Range).",
        MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.1)]
    public double Multiplier { get; set; } = 2;

    public ISeries<double> Center => _cW ??= new IndicatorOutputSeries(_center, Bars);
    public ISeries<double> Upper  => _uW ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _lW ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 1) throw new ArgumentOutOfRangeException(nameof(Length));
        if (Power < 1) throw new ArgumentOutOfRangeException(nameof(Power));
        if (Multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));

        _srcW   = new IndicatorOutputSeries(_src,   Bars);
        _rangeW = new IndicatorOutputSeries(_range, Bars);
        // parent=null: parent-filled inputs; avoid auto-dispatch double-processing.
        _jmaCenter = new Jma(parent: null, input: _srcW)   { Length = Length, Phase = Phase, Power = Power };
        _jmaRange  = new Jma(parent: null, input: _rangeW) { Length = Length, Phase = Phase, Power = Power };
        _jmaCenter.OnInit(ctx);
        _jmaRange.OnInit(ctx);
        _prevClose = null;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Push(bars[bars.Count - 1 - i]);
        _jmaCenter.OnDataLoaded(ctx);
        _jmaRange.OnDataLoaded(ctx);

        int n = _jmaCenter.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            Emit(_jmaCenter.Output[b], _jmaRange.Output[b]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Push(Bars[0]);
        _jmaCenter.OnBarUpdate(ctx);
        _jmaRange.OnBarUpdate(ctx);
        Emit(_jmaCenter.Output[0], _jmaRange.Output[0]);
    }

    private void Push(Bar bar)
    {
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        _prevClose = bar.Close;
        _src.Append(bar.Close);
        _range.Append(tr);
    }

    private void Emit(double center, double range)
    {
        double off = Multiplier * range;
        _center.Append(center);
        _upper.Append(center + off);
        _lower.Append(center - off);
    }
}
