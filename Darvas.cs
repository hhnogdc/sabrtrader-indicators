using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;
using SabrTrader.Pipeline.Indicators;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Darvas Boxes — 1-on-1 port of NT @Darvas.
/// 6-state machine on bar highs/lows:
///   0 → 1: arm (boxTop = high, boxBottom = +Inf)
///   1 → 2 if new high &lt; boxTop, else stay
///   2 → 3 (boxBottom=low) if new high &lt; boxTop, else reset
///   3 → 4 if boxBottom &lt; new low, else lower boxBottom or reset
///   4 → 5 if boxBottom &lt; new low (box confirmed), else 3 or reset
///   5 stable; break above boxTop → BuySignal; break below boxBottom → SellSignal; box resets
/// Plots: Lower (Crimson, Square 2px), Upper (DarkCyan, Square 2px). Overlay.
/// On every bar back-fills Lower/Upper from current bar to box start (Set per barsAgo offset).
/// </summary>
[IndicatorDescription("Darvas Boxes — a 6-state machine that builds consolidation boxes from successive bar highs and lows; signals fire when price breaks above the top (buy) or below the bottom (sell).")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Darvas : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _lower = new();
    private readonly ChunkedSeries<double> _upper = new();
    private IndicatorOutputSeries? _loW, _upW;

    /// <summary>Snapshot of each confirmed box (state==5) — anchor bar (start) + end bar + top + bottom prices.</summary>
    private readonly List<BoxSnapshot> _boxes = new();
    private BoxSnapshot? _activeBox;
    private struct BoxSnapshot { public int StartBar; public int EndBar; public double Top; public double Bottom; }

    private double _boxBottom = double.MaxValue;
    private double _boxTop = double.MinValue;
    private double _currentBarHigh = double.MinValue;
    private double _currentBarLow = double.MaxValue;
    private int _startBarActBox;
    private int _state;
    private int _barIdx;

    public Darvas(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var darvasEdge = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null);
        AddPlot(new Plot("Lower", _lower, PlotStyle.Dots, NTColors.Crimson,  2.0) { Semantics = darvasEdge });
        AddPlot(new Plot("Upper", _upper, PlotStyle.Dots, NTColors.DarkCyan, 2.0) { Semantics = darvasEdge });
    }

    public bool BuySignal  { get; private set; }
    public bool SellSignal { get; private set; }

    public ISeries<double> Lower => _loW ??= new IndicatorOutputSeries(_lower, Bars);
    public ISeries<double> Upper => _upW ??= new IndicatorOutputSeries(_upper, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _boxBottom = double.MaxValue;
        _boxTop = double.MinValue;
        _currentBarHigh = double.MinValue;
        _currentBarLow = double.MaxValue;
        _startBarActBox = 0;
        _state = 0;
        _barIdx = 0;
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
        BuySignal = false; SellSignal = false;
        _lower.Append(double.NaN);
        _upper.Append(double.NaN);

        if (_barIdx == 0)
        {
            _currentBarHigh = bar.High;
            _currentBarLow  = bar.Low;
            _state = GetNextState();
            _barIdx++;
            return;
        }

        _currentBarHigh = bar.High;
        _currentBarLow  = bar.Low;

        if (_state == 5 && (_currentBarHigh > _boxTop || _currentBarLow < _boxBottom))
        {
            if (_currentBarHigh > _boxTop) BuySignal = true; else SellSignal = true;
            _state = 0;
            _startBarActBox = _barIdx;
        }
        _state = GetNextState();

        int span = _barIdx - _startBarActBox;
        if (_boxBottom >= double.MaxValue)
        {
            for (int i = span; i >= 0; i--) if (i < _upper.Count) _upper.Set(i, _boxTop);
        }
        else
        {
            for (int i = span; i >= 0; i--)
            {
                if (i < _upper.Count) _upper.Set(i, _boxTop);
                if (i < _lower.Count) _lower.Set(i, _boxBottom);
            }
        }

        // Track confirmed boxes (state==5) for the custom renderer.
        if (_state == 5 && _boxBottom < double.MaxValue)
        {
            _activeBox = new BoxSnapshot { StartBar = _startBarActBox, EndBar = _barIdx, Top = _boxTop, Bottom = _boxBottom };
        }
        else if (_activeBox.HasValue && (BuySignal || SellSignal))
        {
            // Box just broke — freeze it into the history list.
            _boxes.Add(_activeBox.Value);
            _activeBox = null;
        }

        _barIdx++;
    }

    /// <summary>Render Darvas boxes as outlined rectangles (top/bottom solid lines, vertical sides).</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        var topColor = Plots[1].Color;
        var bottomColor = Plots[0].Color;
        double width = Math.Max(1.0, Plots[0].LineWidthPx);

        foreach (var box in _boxes) DrawBox(renderer, vp, box, topColor, bottomColor, width);
        if (_activeBox.HasValue) DrawBox(renderer, vp, _activeBox.Value, topColor, bottomColor, width);
    }

    private static void DrawBox(SabrTrader.Pipeline.Charts.Rendering.IChartRenderer renderer,
                                SabrTrader.Pipeline.Charts.Viewport.ChartViewport vp,
                                BoxSnapshot box,
                                ChartColor topColor, ChartColor bottomColor, double width)
    {
        double x1 = vp.BarIndexToPixelX(box.StartBar);
        double x2 = vp.BarIndexToPixelX(box.EndBar);
        double yTop = vp.PriceToPixelY(box.Top);
        double yBot = vp.PriceToPixelY(box.Bottom);
        renderer.DrawLine(x1, yTop, x2, yTop, topColor,    width); // top
        renderer.DrawLine(x1, yBot, x2, yBot, bottomColor, width); // bottom
        renderer.DrawLine(x1, yTop, x1, yBot, topColor,    width); // left
        renderer.DrawLine(x2, yTop, x2, yBot, topColor,    width); // right
    }

    private int GetNextState()
    {
        switch (_state)
        {
            case 0:
                _boxTop = _currentBarHigh;
                _boxBottom = double.MaxValue;
                return 1;
            case 1:
                if (_boxTop > _currentBarHigh) return 2;
                _boxTop = _currentBarHigh; return 1;
            case 2:
                if (_boxTop > _currentBarHigh) { _boxBottom = _currentBarLow; return 3; }
                _boxTop = _currentBarHigh; return 1;
            case 3:
                if (_boxTop > _currentBarHigh)
                {
                    if (_boxBottom < _currentBarLow) return 4;
                    _boxBottom = _currentBarLow; return 3;
                }
                _boxTop = _currentBarHigh; _boxBottom = double.MaxValue; return 1;
            case 4:
                if (_boxTop > _currentBarHigh)
                {
                    if (_boxBottom < _currentBarLow) return 5;
                    _boxBottom = _currentBarLow; return 3;
                }
                _boxTop = _currentBarHigh; _boxBottom = double.MaxValue; return 1;
            case 5:
                return 5;
            default:
                return _state;
        }
    }
}
