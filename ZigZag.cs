using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;
using SabrTrader.Pipeline.Indicators;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum DeviationType { Percent = 0, Points = 1 }

/// <summary>
/// ZigZag.
/// Pivot-based filtered swing detector. Detects a swing pivot when:
///   • the 1-bar-ago bar is a local extreme over the 3-bar window
///     (close to the original's `highSeries[1] &gt;= highSeries[0] && highSeries[1] &gt;= highSeries[2]`); AND
///   • the move from the last confirmed pivot exceeds the deviation threshold
///     (Percent / Points). Trend can flip on a new swing in the opposite
///     direction, or extend the previous swing (update).
/// Emits a single output (Default) carrying the pivot price at pivot bars, NaN otherwise.
/// Two sub-series: ZigZagHigh / ZigZagLow (pivot values per kind, NaN otherwise).
/// Defaults DeviationType=Points, DeviationValue=0.5, UseHighLow=false. DodgerBlue 1px Line.
/// Renderer (ChartControl) is the host's responsibility — math only here.
/// </summary>
[IndicatorDescription("ZigZag swing filter. Connects significant pivot highs and lows, ignoring moves smaller than a configurable percent or point deviation. Useful for visualising market structure.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class ZigZag : IndicatorBase, IChartCustomRender
{

    private readonly ChunkedSeries<double> _value = new();    // pivot value or NaN
    private readonly ChunkedSeries<double> _zigZagHigh = new();
    private readonly ChunkedSeries<double> _zigZagLow  = new();
    private readonly ChunkedSeries<double> _zigZagHighSeries = new();  // last high level held forward
    private readonly ChunkedSeries<double> _zigZagLowSeries  = new();  // last low level held forward
    private IndicatorOutputSeries? _valueW, _zhW, _zlW, _zhsW, _zlsW;

    private double _currentZigZagHigh;
    private double _currentZigZagLow;
    private int _lastSwingIdx;
    private double _lastSwingPrice;
    private int _trendDir; // 1 up, -1 down, 0 init
    private int _barIdx;

    public ZigZag(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("ZigZag", _value, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "DeviationType",
        Description = "Percent or Points threshold for swing confirmation.")]
    public DeviationType DeviationType { get; set; } = DeviationType.Points;

    [IndicatorParameter(DisplayName = "DeviationValue",
        Description = "Minimum move from the previous pivot before a new pivot is registered.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.1)]
    public double DeviationValue { get; set; } = 0.5;

    [IndicatorParameter(DisplayName = "UseHighLow",
        Description = "When true, use bar High/Low; else use Input series.")]
    public bool UseHighLow { get; set; } = false;

    public ISeries<double> Default       => _valueW ??= new IndicatorOutputSeries(_value, Bars);
    public ISeries<double> ZigZagHighRaw => _zhW    ??= new IndicatorOutputSeries(_zigZagHigh, Bars);
    public ISeries<double> ZigZagLowRaw  => _zlW    ??= new IndicatorOutputSeries(_zigZagLow,  Bars);
    public ISeries<double> ZigZagHigh    => _zhsW   ??= new IndicatorOutputSeries(_zigZagHighSeries, Bars);
    public ISeries<double> ZigZagLow     => _zlsW   ??= new IndicatorOutputSeries(_zigZagLowSeries,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (DeviationValue < 0) throw new ArgumentOutOfRangeException(nameof(DeviationValue));
        _currentZigZagHigh = 0;
        _currentZigZagLow  = 0;
        _lastSwingIdx = -1;
        _lastSwingPrice = 0.0;
        _trendDir = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars, 0);
    }

    private double GetHigh(IBarSeries bars, int barsAgo) => UseHighLow ? bars[barsAgo].High  : (Input?[barsAgo] ?? bars[barsAgo].Close);
    private double GetLow (IBarSeries bars, int barsAgo) => UseHighLow ? bars[barsAgo].Low   : (Input?[barsAgo] ?? bars[barsAgo].Close);

    private void Consume(IBarSeries bars, int barsAgoOfCurrent)
    {
        // Default-append all output series so we can index them.
        _value.Append(double.NaN);
        _zigZagHigh.Append(double.NaN);
        _zigZagLow.Append(double.NaN);
        _zigZagHighSeries.Append(_currentZigZagHigh);
        _zigZagLowSeries.Append(_currentZigZagLow);

        if (_barIdx < 2) { _barIdx++; return; }

        if (_lastSwingPrice == 0.0)
            _lastSwingPrice = UseHighLow ? bars[barsAgoOfCurrent].Close : (Input?[barsAgoOfCurrent] ?? bars[barsAgoOfCurrent].Close);

        // Bars 0/1/2 relative to current (current = barsAgoOfCurrent).
        double h0 = GetHigh(bars, barsAgoOfCurrent);
        double h1 = GetHigh(bars, barsAgoOfCurrent + 1);
        double h2 = GetHigh(bars, barsAgoOfCurrent + 2);
        double l0 = GetLow(bars, barsAgoOfCurrent);
        double l1 = GetLow(bars, barsAgoOfCurrent + 1);
        double l2 = GetLow(bars, barsAgoOfCurrent + 2);

        bool isSwingHigh = h1 >= h0 && h1 >= h2;
        bool isSwingLow  = l1 <= l0 && l1 <= l2;
        bool overHi = DeviationType == DeviationType.Percent
            ? h1 > _lastSwingPrice * (1.0 + DeviationValue / 100.0)
            : h1 > _lastSwingPrice + DeviationValue;
        bool overLo = DeviationType == DeviationType.Percent
            ? _lastSwingPrice * (1.0 - DeviationValue / 100.0) > l1
            : _lastSwingPrice - DeviationValue > l1;

        if (!isSwingHigh && !isSwingLow) { _barIdx++; return; }

        double saveValue = 0; bool addHigh = false, addLow = false, updateHigh = false, updateLow = false;
        if (_trendDir <= 0 && isSwingHigh && overHi)        { saveValue = h1; addHigh = true; _trendDir = 1; }
        else if (_trendDir >= 0 && isSwingLow && overLo)    { saveValue = l1; addLow  = true; _trendDir = -1; }
        else if (_trendDir == 1 && isSwingHigh && h1 > _lastSwingPrice) { saveValue = h1; updateHigh = true; }
        else if (_trendDir == -1 && isSwingLow && _lastSwingPrice > l1) { saveValue = l1; updateLow  = true; }

        if (addHigh || addLow || updateHigh || updateLow)
        {
            // Update: erase the previous pivot's value (write NaN) so the line skips it.
            if (updateHigh && _lastSwingIdx >= 0)
            {
                int ago = _barIdx - _lastSwingIdx;
                if (ago >= 0 && ago < _zigZagHigh.Count) _zigZagHigh.Set(ago, double.NaN);
                if (ago >= 0 && ago < _value.Count)      _value.Set(ago, double.NaN);
            }
            else if (updateLow && _lastSwingIdx >= 0)
            {
                int ago = _barIdx - _lastSwingIdx;
                if (ago >= 0 && ago < _zigZagLow.Count) _zigZagLow.Set(ago, double.NaN);
                if (ago >= 0 && ago < _value.Count)     _value.Set(ago, double.NaN);
            }

            // Write the pivot at barsAgo=1 (the candidate bar, one bar back from current).
            if (addHigh || updateHigh)
            {
                _zigZagHigh.Set(1, saveValue);
                _currentZigZagHigh = saveValue;
                _zigZagHighSeries.Set(1, _currentZigZagHigh);
                _value.Set(1, _currentZigZagHigh);
            }
            else
            {
                _zigZagLow.Set(1, saveValue);
                _currentZigZagLow = saveValue;
                _zigZagLowSeries.Set(1, _currentZigZagLow);
                _value.Set(1, _currentZigZagLow);
            }

            _lastSwingIdx   = _barIdx - 1;
            _lastSwingPrice = saveValue;
        }

        // Hold-forward levels at current bar.
        _zigZagHighSeries.Set(0, _currentZigZagHigh);
        _zigZagLowSeries.Set(0, _currentZigZagLow);
        _barIdx++;
    }

    /// <summary>
    /// Custom rendering: draw connecting lines between consecutive pivots,
    /// matching the original's PathGeometry render. Walks the pivot series in visible
    /// range and produces line segments between successive non-NaN points.
    /// </summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        var color = Plots[0].Color;
        var width = Plots[0].LineWidthPx;

        // Walk the visible range and collect pivot points (high or low).
        int first = Math.Max(0, vp.FirstVisibleBarIdx);
        int last = Math.Min(_value.Count - 1, vp.LastVisibleBarIdx);
        bool havePrev = false;
        double prevX = 0, prevY = 0;
        int total = _value.Count;
        for (int absIdx = first; absIdx <= last; absIdx++)
        {
            int barsAgo = total - 1 - absIdx;
            if (barsAgo < 0 || barsAgo >= _value.Count) continue;
            double v = _value[barsAgo];
            if (double.IsNaN(v)) continue;
            double x = vp.BarIndexToPixelX(absIdx);
            double y = vp.PriceToPixelY(v);
            if (havePrev) renderer.DrawLine(prevX, prevY, x, y, color, width);
            prevX = x; prevY = y; havePrev = true;
        }
    }
}
