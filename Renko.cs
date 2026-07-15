using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum RenkoEndType { Close, HighLow }

/// <summary>
/// Renko chart transform — 1-on-1 port of Skender Renko.
/// Generates new Renko bricks based on Close (or High/Low) movement of BrickSize. Multiple bricks
/// can appear per source bar when price moves by more than one brick. Exposes the brick OHLC as
/// 4 overlay plots tracking the LATEST brick on each bar (no plot value when no new brick).
/// IChartCustomRender draws all confirmed bricks as filled rectangles.
/// Defaults BrickSize=user-required (no Skender default), EndType=Close.
/// </summary>
[IndicatorDescription("Renko transform: plots fixed-size price bricks (BrickSize) that advance only when price moves a full brick using Close or High/Low. Filters out small fluctuations to highlight trend structure.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Renko : IndicatorBase, IChartCustomRender
{
    public sealed record Brick(int SourceBarIdx, double Open, double Close, double High, double Low, bool IsUp);

    private readonly ChunkedSeries<double> _open  = new();
    private readonly ChunkedSeries<double> _close = new();
    private readonly ChunkedSeries<double> _high  = new();
    private readonly ChunkedSeries<double> _low   = new();
    private IndicatorOutputSeries? _oW, _cW, _hW, _lW;

    private readonly List<Brick> _bricks = new();
    private double _lastBrickOpen, _lastBrickClose;
    private double _intraHigh = double.MinValue;
    private double _intraLow  = double.MaxValue;
    private bool _seeded;
    private int _barIdx;

    public Renko(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Renko Open",  _open,  PlotStyle.Line, NTColors.DarkGray,   1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko Close", _close, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko High",  _high,  PlotStyle.Line, NTColors.LimeGreen,  1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Renko Low",   _low,   PlotStyle.Line, NTColors.Red,        1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "BrickSize",
        Description = "Renko brick price size (> 0).",
        MinValue = 0.0001, MaxValue = double.MaxValue, Step = 0.25)]
    public double BrickSize { get; set; } = 1;

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
        if (BrickSize <= 0) throw new ArgumentOutOfRangeException(nameof(BrickSize));
        _bricks.Clear();
        _seeded = false;
        _intraHigh = double.MinValue;
        _intraLow  = double.MaxValue;
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
        if (!_seeded)
        {
            // Skender baseline: rounded close.
            _lastBrickOpen = _lastBrickClose = Math.Round(bar.Close / BrickSize, MidpointRounding.AwayFromZero) * BrickSize;
            _seeded = true;
            EmitNoBrick();
            _barIdx++;
            return;
        }
        // Track intra-brick H/L/Vol.
        if (bar.High > _intraHigh) _intraHigh = bar.High;
        if (bar.Low  < _intraLow)  _intraLow  = bar.Low;

        // Decide brick count for this source bar.
        double up   = EndType == RenkoEndType.Close ? bar.Close : bar.High;
        double down = EndType == RenkoEndType.Close ? bar.Close : bar.Low;
        int qty;
        bool isUp;
        if (up >= _lastBrickClose + BrickSize)
        {
            qty = (int)Math.Floor((up - _lastBrickClose) / BrickSize);
            isUp = true;
        }
        else if (down <= _lastBrickClose - BrickSize)
        {
            qty = (int)Math.Floor((_lastBrickClose - down) / BrickSize);
            isUp = false;
        }
        else { EmitNoBrick(); _barIdx++; return; }

        for (int b = 0; b < qty; b++)
        {
            double o = isUp ? Math.Max(_lastBrickOpen, _lastBrickClose)
                            : Math.Min(_lastBrickOpen, _lastBrickClose);
            double c = isUp ? o + BrickSize : o - BrickSize;
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

    /// <summary>Render the brick chain as outlined rectangles per brick.</summary>
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
