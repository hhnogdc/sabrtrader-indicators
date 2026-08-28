using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Heikin-Ashi — derived from Skender.Stock.Indicators (Apache-2.0).
/// Bar-smoothing transform:
///   HA Close = (Open + High + Low + Close) / 4
///   HA Open  = (prev HA Open + prev HA Close) / 2     (seeded with bar 0's Open/Close)
///   HA High  = max(High, HA Open, HA Close)
///   HA Low   = min(Low,  HA Open, HA Close)
/// Exposed as 4 plots (HA-Open / HA-High / HA-Low / HA-Close) for chart overlays.
/// (A true alternate bar-series output requires bar-stream framework infra; out of scope.)
/// </summary>
[IndicatorDescription("Heikin-Ashi bar transform: smoothed OHLC where HA-Close = (O+H+L+C)/4 and HA-Open = (prev HA-Open + prev HA-Close)/2. Filters noise and makes trends visually obvious.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class HeikinAshi : IndicatorBase
{
    private readonly ChunkedSeries<double> _open  = new();
    private readonly ChunkedSeries<double> _high  = new();
    private readonly ChunkedSeries<double> _low   = new();
    private readonly ChunkedSeries<double> _close = new();
    private IndicatorOutputSeries? _oW, _hW, _lW, _cW;
    private double _prevOpen, _prevClose;
    private bool _seeded;

    public HeikinAshi(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("HA Open",  _open,  PlotStyle.Line, PlotColors.DarkGray,   1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("HA High",  _high,  PlotStyle.Line, PlotColors.LimeGreen, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("HA Low",   _low,   PlotStyle.Line, PlotColors.Red,       1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("HA Close", _close, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Open  => _oW ??= new IndicatorOutputSeries(_open,  Bars);
    public ISeries<double> High  => _hW ??= new IndicatorOutputSeries(_high,  Bars);
    public ISeries<double> Low   => _lW ??= new IndicatorOutputSeries(_low,   Bars);
    public ISeries<double> Close => _cW ??= new IndicatorOutputSeries(_close, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _seeded = false;
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
        if (!_seeded) { _prevOpen = bar.Open; _prevClose = bar.Close; _seeded = true; }
        double haClose = (bar.Open + bar.High + bar.Low + bar.Close) / 4;
        double haOpen  = (_prevOpen + _prevClose) / 2;
        double haHigh  = Math.Max(bar.High, Math.Max(haOpen, haClose));
        double haLow   = Math.Min(bar.Low,  Math.Min(haOpen, haClose));
        _open.Append(haOpen); _high.Append(haHigh); _low.Append(haLow); _close.Append(haClose);
        _prevOpen = haOpen; _prevClose = haClose;
    }
}
