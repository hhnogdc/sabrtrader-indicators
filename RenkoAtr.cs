using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Renko (ATR-sized bricks) — 1-on-1 port of Skender RenkoAtr.
/// Identical brick formation logic as <see cref="Renko"/> but brick size is the Wilder ATR
/// computed at the LAST bar of the source series (Skender) and frozen for all bricks.
/// Defaults AtrPeriods=14, EndType=Close.
/// </summary>
[IndicatorDescription("ATR-Renko: Renko bricks whose size is the current Wilder ATR over AtrPeriods rather than a fixed value, so the brick size adapts to changing volatility.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class RenkoAtr : IndicatorBase, IChartCustomRender
{
    public sealed record Brick(int SourceBarIdx, double Open, double Close, double High, double Low, bool IsUp);

    private readonly ChunkedSeries<double> _open  = new();
    private readonly ChunkedSeries<double> _close = new();
    private readonly ChunkedSeries<double> _high  = new();
    private readonly ChunkedSeries<double> _low   = new();
    private IndicatorOutputSeries? _oW, _cW, _hW, _lW;

    private readonly List<Brick> _bricks = new();
    private double _brickSize;
    private double _lastBrickOpen, _lastBrickClose;
    private double _intraHigh = double.MinValue;
    private double _intraLow  = double.MaxValue;
    private bool _seeded;

    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;
    private double? _prevClose;

    private int _barIdx;

    public RenkoAtr(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Renko Open",  _open,  PlotStyle.Line, NTColors.DarkGray,   1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko Close", _close, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko High",  _high,  PlotStyle.Line, NTColors.LimeGreen,  1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko Low",   _low,   PlotStyle.Line, NTColors.Red,        1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "AtrPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int AtrPeriods { get; set; } = 14;

    [IndicatorParameter(DisplayName = "EndType")]
    public RenkoEndType EndType { get; set; } = RenkoEndType.Close;

    public ISeries<double> Open  => _oW ??= new IndicatorOutputSeries(_open,  Bars);
    public ISeries<double> Close => _cW ??= new IndicatorOutputSeries(_close, Bars);
    public ISeries<double> High  => _hW ??= new IndicatorOutputSeries(_high,  Bars);
    public ISeries<double> Low   => _lW ??= new IndicatorOutputSeries(_low,   Bars);
    public IReadOnlyList<Brick> Bricks => _bricks;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (AtrPeriods < 1) throw new ArgumentOutOfRangeException(nameof(AtrPeriods));
        _bricks.Clear();
        _seeded = _atrSeeded = false;
        _atrSeedSum = 0; _atrSeedCount = 0;
        _prevClose = null;
        _intraHigh = double.MinValue; _intraLow = double.MaxValue;
        _barIdx = 0;
        _brickSize = 0;
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
        // Wilder ATR.
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        _prevClose = bar.Close;
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= AtrPeriods) { _atr = _atrSeedSum / AtrPeriods; _atrSeeded = true; _brickSize = _atr; }
        }
        else _atr = (_atr * (AtrPeriods - 1) + tr) / AtrPeriods;
        // Skender uses the ATR at last bar — in streaming we update _brickSize each bar so it
        // reflects the latest ATR. Existing bricks remain at their original sizes.
        if (_atrSeeded) _brickSize = _atr;

        if (!_seeded)
        {
            if (!_atrSeeded || _brickSize <= 0) { EmitNoBrick(); _barIdx++; return; }
            _lastBrickOpen = _lastBrickClose = Math.Round(bar.Close / _brickSize) * _brickSize;
            _seeded = true;
            EmitNoBrick();
            _barIdx++;
            return;
        }
        if (bar.High > _intraHigh) _intraHigh = bar.High;
        if (bar.Low  < _intraLow)  _intraLow  = bar.Low;

        double up   = EndType == RenkoEndType.Close ? bar.Close : bar.High;
        double down = EndType == RenkoEndType.Close ? bar.Close : bar.Low;
        int qty;
        bool isUp;
        if (up >= _lastBrickClose + _brickSize)
        {
            qty = (int)Math.Floor((up - _lastBrickClose) / _brickSize);
            isUp = true;
        }
        else if (down <= _lastBrickClose - _brickSize)
        {
            qty = (int)Math.Floor((_lastBrickClose - down) / _brickSize);
            isUp = false;
        }
        else { EmitNoBrick(); _barIdx++; return; }

        for (int b = 0; b < qty; b++)
        {
            double o = isUp ? Math.Max(_lastBrickOpen, _lastBrickClose)
                            : Math.Min(_lastBrickOpen, _lastBrickClose);
            double c = isUp ? o + _brickSize : o - _brickSize;
            _bricks.Add(new Brick(_barIdx, o, c, _intraHigh, _intraLow, isUp));
            _lastBrickOpen = o; _lastBrickClose = c;
        }
        EmitLatestBrick();
        _intraHigh = double.MinValue; _intraLow = double.MaxValue;
        _barIdx++;
    }

    private void EmitNoBrick()
    { _open.Append(double.NaN); _close.Append(double.NaN); _high.Append(double.NaN); _low.Append(double.NaN); }

    private void EmitLatestBrick()
    {
        var b = _bricks[^1];
        _open.Append(b.Open); _close.Append(b.Close); _high.Append(b.High); _low.Append(b.Low);
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_bricks.Count == 0) return;
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        double w = Math.Max(2, vp.BarSpacingPx * 0.8);
        foreach (var b in _bricks)
        {
            if (b.SourceBarIdx < vp.FirstVisibleBarIdx || b.SourceBarIdx > vp.LastVisibleBarIdx) continue;
            double cx = vp.BarIndexToPixelX(b.SourceBarIdx);
            double yTop = vp.PriceToPixelY(Math.Max(b.Open, b.Close));
            double yBot = vp.PriceToPixelY(Math.Min(b.Open, b.Close));
            var color = b.IsUp ? NTColors.LimeGreen : NTColors.Red;
            renderer.FillRect(cx - w / 2, yTop, w, Math.Max(1, yBot - yTop), new ChartColor(color.R, color.G, color.B, 0x99));
        }
    }
}
