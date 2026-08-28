using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum ChartPattern
{
    BearishBeltHold = 0,
    BearishEngulfing = 1,
    BearishHarami = 2,
    BearishHaramiCross = 3,
    BullishBeltHold = 4,
    BullishEngulfing = 5,
    BullishHarami = 6,
    BullishHaramiCross = 7,
    DarkCloudCover = 8,
    Doji = 9,
    DownsideTasukiGap = 10,
    EveningStar = 11,
    FallingThreeMethods = 12,
    Hammer = 13,
    HangingMan = 14,
    InvertedHammer = 15,
    MorningStar = 16,
    PiercingLine = 17,
    RisingThreeMethods = 18,
    ShootingStar = 19,
    StickSandwich = 20,
    ThreeBlackCrows = 21,
    ThreeWhiteSoldiers = 22,
    UpsideGapTwoCrows = 23,
    UpsideTasukiGap = 24,
}

/// <summary>
/// Candlestick Pattern.
/// Detects 25 classic candlestick patterns. One plot (PatternFound, Transparent) emits 1.0
/// on detection, 0.0 otherwise. Trend is established via a child <see cref="Swing"/>
/// (TrendStrength=4 default); trend-dependent patterns (Hammer, BearishEngulfing, etc.)
/// require the appropriate trend direction.
/// Match-once-per-2-bars deduplication mirrors the original's prior[CurrentBar%2] gate.
/// IChartCustomRender draws the pattern label above/below the candle and an in-corner
/// "{n} {Pattern} patterns found" counter (the original's TextFixed BottomRight).
/// Defaults Pattern=MorningStar, TrendStrength=4, ShowAlerts=true, ShowPatternCount=true.
/// </summary>
[IndicatorDescription("Candlestick Pattern recognizer. Scans bars for one of 25 classic patterns (engulfings, hammers, stars, harami, three-soldiers, etc.) with trend-context filtering via a Swing pivot, labelling each hit on the chart.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class CandlestickPattern : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _patternFound = new();
    private IndicatorOutputSeries? _wrap;

    private Swing _swing = null!;
    private RollingMinMax _max = null!, _min = null!;
    private readonly bool[] _prior = new bool[2];

    private readonly List<(int BarIdx, string Text, double Price)> _hits = new();
    private int _numPatternsFound;
    private int _barIdx;

    public CandlestickPattern(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Pattern Found", _patternFound, PlotStyle.Line, PlotColors.Transparent, 1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Pattern")]
    public ChartPattern Pattern { get; set; } = ChartPattern.MorningStar;

    [IndicatorParameter(DisplayName = "TrendStrength", MinValue = 0, MaxValue = int.MaxValue, Step = 1)]
    public int TrendStrength { get; set; } = 4;

    [IndicatorParameter(DisplayName = "ShowAlerts")]
    public bool ShowAlerts { get; set; } = true;

    [IndicatorParameter(DisplayName = "ShowPatternCount")]
    public bool ShowPatternCount { get; set; } = true;

    /// <summary>Tick size — host-supplied (the original uses Instrument.MasterInstrument.TickSize).</summary>
    public double TickSize { get; set; } = 0.25;

    public ChartColor TextColor { get; set; } = PlotColors.DimGray;

    public ISeries<double> PatternFound => _wrap ??= new IndicatorOutputSeries(_patternFound, Bars);

    public int NumPatternsFound => _numPatternsFound;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (TrendStrength < 0) throw new ArgumentOutOfRangeException(nameof(TrendStrength));
        if (TrendStrength > 0 && Pattern != ChartPattern.Doji && Pattern != ChartPattern.DownsideTasukiGap
            && Pattern != ChartPattern.EveningStar && Pattern != ChartPattern.FallingThreeMethods
            && Pattern != ChartPattern.MorningStar && Pattern != ChartPattern.RisingThreeMethods
            && Pattern != ChartPattern.StickSandwich && Pattern != ChartPattern.UpsideTasukiGap)
        {
            _swing = new Swing(this, bars: Bars) { Strength = TrendStrength };
        }
        if (TrendStrength > 0 && (Pattern == ChartPattern.HangingMan || Pattern == ChartPattern.InvertedHammer))
            _max = new RollingMinMax(TrendStrength, trackMax: true);
        if (TrendStrength > 0 && Pattern == ChartPattern.Hammer)
            _min = new RollingMinMax(TrendStrength, trackMax: false);
        _numPatternsFound = 0;
        _barIdx = 0;
        _hits.Clear();
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child (Swing/Sma) on the live bar
        if (Bars.Count == 0) return;
        Consume(Bars, 0);
    }

    private void Consume(IBarSeries bars, int barsAgoOfCurrent)
    {
        _patternFound.Append(0);
        if (_max != null) _max.Push(bars[barsAgoOfCurrent].High);
        if (_min != null) _min.Push(bars[barsAgoOfCurrent].Low);

        bool found = Evaluate(bars, barsAgoOfCurrent);
        if (found)
        {
            _patternFound.Set(0, 1);
            _numPatternsFound++;
            string text = LabelOf(Pattern);
            var bar = bars[barsAgoOfCurrent];
            double price = (Pattern == ChartPattern.BearishBeltHold || Pattern == ChartPattern.BearishEngulfing ||
                            Pattern == ChartPattern.BearishHarami   || Pattern == ChartPattern.BearishHaramiCross)
                ? Math.Max(bar.High, _barIdx > 0 ? bars[barsAgoOfCurrent + 1].High : bar.High)
                : bar.Close;
            _hits.Add((_barIdx, text, price));
        }
        _prior[_barIdx % 2] = found;
        _barIdx++;
    }

    private bool Evaluate(IBarSeries bars, int barsAgoOfCurrent)
    {
        if (_barIdx < TrendStrength || _barIdx < 2) return false;
        bool isInUpTrend = false, isInDownTrend = false;

        if (_swing == null)
        {
            // For patterns that don't need a trend (Doji, MorningStar, ...) the original logic sets both true
            // unconditionally when trendStrength == 0; for the other family we leave both false.
            if (TrendStrength == 0) { isInUpTrend = true; isInDownTrend = true; }
        }
        else
        {
            // Walk swing pivots to determine current trend (same algorithm as the original).
            int upStart = 0, upEnd = 0; int upOcc = 1;
            while (LowAt(bars, barsAgoOfCurrent, upEnd) <= LowAt(bars, barsAgoOfCurrent, upStart))
            {
                upStart = _swing.SwingLowBar(0, upOcc + 1, _barIdx);
                upEnd   = _swing.SwingLowBar(0, upOcc,     _barIdx);
                if (upStart < 0 || upEnd < 0) break;
                upOcc++;
            }
            int dnStart = 0, dnEnd = 0; int dnOcc = 1;
            while (HighAt(bars, barsAgoOfCurrent, dnEnd) >= HighAt(bars, barsAgoOfCurrent, dnStart))
            {
                dnStart = _swing.SwingHighBar(0, dnOcc + 1, _barIdx);
                dnEnd   = _swing.SwingHighBar(0, dnOcc,     _barIdx);
                if (dnStart < 0 || dnEnd < 0) break;
                dnOcc++;
            }
            if (upStart > 0 && upEnd > 0 && upStart < dnStart) { isInUpTrend = true; }
            else if (dnStart > 0 && dnEnd > 0 && upStart > dnStart) { isInDownTrend = true; }
        }

        if (_prior[0] || _prior[1]) return false;

        // Shortcut accessors for n.X[i] mapping to our bars[barsAgoOfCurrent + i].
        double O(int i) => bars[barsAgoOfCurrent + i].Open;
        double H(int i) => bars[barsAgoOfCurrent + i].High;
        double L(int i) => bars[barsAgoOfCurrent + i].Low;
        double C(int i) => bars[barsAgoOfCurrent + i].Close;
        double ts = TickSize;

        switch (Pattern)
        {
            case ChartPattern.BearishBeltHold:
                return isInUpTrend && C(1) > O(1) && O(0) > C(1) + 5 * ts && O(0) == H(0) && C(0) < O(0);
            case ChartPattern.BearishEngulfing:
                return isInUpTrend && C(1) > O(1) && C(0) < O(0) && O(0) > C(1) && C(0) < O(1);
            case ChartPattern.BearishHarami:
                return isInUpTrend && C(0) < O(0) && C(1) > O(1) && L(0) >= O(1) && H(0) <= C(1);
            case ChartPattern.BearishHaramiCross:
                return isInUpTrend && H(0) <= C(1) && L(0) >= O(1) && O(0) <= C(1) && C(0) >= O(1)
                    && ((C(0) >= O(0) && C(0) <= O(0) + ts) || (C(0) <= O(0) && C(0) >= O(0) - ts));
            case ChartPattern.BullishBeltHold:
                return isInDownTrend && C(1) < O(1) && O(0) < C(1) - 5 * ts && O(0) == L(0) && C(0) > O(0);
            case ChartPattern.BullishEngulfing:
                return isInDownTrend && C(1) < O(1) && C(0) > O(0) && C(0) > O(1) && O(0) < C(1);
            case ChartPattern.BullishHarami:
                return isInDownTrend && C(0) > O(0) && C(1) < O(1) && L(0) >= C(1) && H(0) <= O(1);
            case ChartPattern.BullishHaramiCross:
                return isInDownTrend && H(0) <= O(1) && L(0) >= C(1) && O(0) >= C(1) && C(0) <= O(1)
                    && ((C(0) >= O(0) && C(0) <= O(0) + ts) || (C(0) <= O(0) && C(0) >= O(0) - ts));
            case ChartPattern.DarkCloudCover:
                return isInUpTrend && O(0) > H(1) && C(1) > O(1) && C(0) < O(0)
                    && C(0) <= C(1) - (C(1) - O(1)) / 2 && C(0) >= O(1);
            case ChartPattern.Doji:
                return Math.Abs(C(0) - O(0)) <= (H(0) - L(0)) * 0.07;
            case ChartPattern.DownsideTasukiGap:
                return C(2) < O(2) && C(1) < O(1) && C(0) > O(0) && H(1) < L(2)
                    && O(0) > C(1) && O(0) < O(1) && C(0) > O(1) && C(0) < C(2);
            case ChartPattern.EveningStar:
                return C(2) > O(2) && C(1) > C(2) && O(0) < Math.Abs((C(1) - O(1)) / 2) + O(1) && C(0) < O(0);
            case ChartPattern.FallingThreeMethods:
                return _barIdx > 5 && C(4) < O(4) && C(0) < O(0) && C(0) < L(4)
                    && H(3) < H(4) && L(3) > L(4) && H(2) < H(4) && L(2) > L(4) && H(1) < H(4) && L(1) > L(4);
            case ChartPattern.Hammer:
                return isInDownTrend && (_min == null || _min.Value == L(0)) && L(0) < O(0) - 5 * ts
                    && Math.Abs(O(0) - C(0)) < 0.10 * (H(0) - L(0)) && H(0) - C(0) < 0.25 * (H(0) - L(0));
            case ChartPattern.HangingMan:
                return isInUpTrend && (_max == null || _max.Value == H(0)) && L(0) < O(0) - 5 * ts
                    && Math.Abs(O(0) - C(0)) < 0.10 * (H(0) - L(0)) && H(0) - C(0) < 0.25 * (H(0) - L(0));
            case ChartPattern.InvertedHammer:
                return isInUpTrend && (_max == null || _max.Value == H(0)) && H(0) > O(0) + 5 * ts
                    && Math.Abs(O(0) - C(0)) < 0.10 * (H(0) - L(0)) && C(0) - L(0) < 0.25 * (H(0) - L(0));
            case ChartPattern.MorningStar:
                return C(2) < O(2) && C(1) < C(2) && O(0) > Math.Abs((C(1) - O(1)) / 2) + O(1) && C(0) > O(0);
            case ChartPattern.PiercingLine:
                return isInDownTrend && O(0) < L(1) && C(1) < O(1) && C(0) > O(0)
                    && C(0) >= C(1) + (O(1) - C(1)) / 2 && C(0) <= O(1);
            case ChartPattern.RisingThreeMethods:
                return _barIdx > 5 && C(4) > O(4) && C(0) > O(0) && C(0) > H(4)
                    && H(3) < H(4) && L(3) > L(4) && H(2) < H(4) && L(2) > L(4) && H(1) < H(4) && L(1) > L(4);
            case ChartPattern.ShootingStar:
                return isInUpTrend && H(0) > O(0) && H(0) - O(0) >= 2 * (O(0) - C(0)) && C(0) < O(0) && C(0) - L(0) <= 2 * ts;
            case ChartPattern.StickSandwich:
                return C(2) == C(0) && C(2) < O(2) && C(1) > O(1) && C(0) < O(0);
            case ChartPattern.ThreeBlackCrows:
                return isInUpTrend && C(0) < O(0) && C(1) < O(1) && C(2) < O(2)
                    && C(0) < C(1) && C(1) < C(2)
                    && O(0) < O(1) && O(0) > C(1) && O(1) < O(2) && O(1) > C(2);
            case ChartPattern.ThreeWhiteSoldiers:
                return isInDownTrend && C(0) > O(0) && C(1) > O(1) && C(2) > O(2)
                    && C(0) > C(1) && C(1) > C(2)
                    && O(0) < C(1) && O(0) > O(1) && O(1) < C(2) && O(1) > O(2);
            case ChartPattern.UpsideGapTwoCrows:
                return isInUpTrend && C(2) > O(2) && C(1) < O(1) && C(0) < O(0)
                    && L(1) > H(2) && C(0) > H(2) && C(0) < C(1) && O(0) > O(1);
            case ChartPattern.UpsideTasukiGap:
                return C(2) > O(2) && C(1) > O(1) && C(0) < O(0) && L(1) > H(2)
                    && O(0) < C(1) && O(0) > O(1) && C(0) < O(1) && C(0) > C(2);
        }
        return false;
    }

    private static double HighAt(IBarSeries bars, int baseIdx, int back)
        => back < 0 ? 0 : bars[baseIdx + back].High;
    private static double LowAt(IBarSeries bars, int baseIdx, int back)
        => back < 0 ? 0 : bars[baseIdx + back].Low;

    private static string LabelOf(ChartPattern p) => p switch
    {
        ChartPattern.BearishBeltHold      => "Bearish Belt Hold",
        ChartPattern.BearishEngulfing     => "Bearish Engulfing",
        ChartPattern.BearishHarami        => "Bearish Harami",
        ChartPattern.BearishHaramiCross   => "Bearish Harami Cross",
        ChartPattern.BullishBeltHold      => "Bullish Belt Hold",
        ChartPattern.BullishEngulfing     => "Bullish Engulfing",
        ChartPattern.BullishHarami        => "Bullish Harami",
        ChartPattern.BullishHaramiCross   => "Bullish Harami Cross",
        ChartPattern.DarkCloudCover       => "Dark Cloud Cover",
        ChartPattern.Doji                 => "Doji",
        ChartPattern.DownsideTasukiGap    => "Downside Tasuki Gap",
        ChartPattern.EveningStar          => "Evening Star",
        ChartPattern.FallingThreeMethods  => "Falling Three Methods",
        ChartPattern.Hammer               => "Hammer",
        ChartPattern.HangingMan           => "Hanging Man",
        ChartPattern.InvertedHammer       => "Inverted Hammer",
        ChartPattern.MorningStar          => "Morning Star",
        ChartPattern.PiercingLine         => "Piercing Line",
        ChartPattern.RisingThreeMethods   => "Rising Three Methods",
        ChartPattern.ShootingStar         => "Shooting Star",
        ChartPattern.StickSandwich        => "Stick Sandwich",
        ChartPattern.ThreeBlackCrows      => "Three Black Crows",
        ChartPattern.ThreeWhiteSoldiers   => "Three White Soldiers",
        ChartPattern.UpsideGapTwoCrows    => "Upside Gap Two Crows",
        ChartPattern.UpsideTasukiGap      => "Upside Tasuki Gap",
        _                                  => p.ToString(),
    };

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;
        double sizePx = 11;
        foreach (var hit in _hits)
        {
            if (hit.BarIdx < vp.FirstVisibleBarIdx || hit.BarIdx > vp.LastVisibleBarIdx) continue;
            double x = vp.BarIndexToPixelX(hit.BarIdx);
            double y = vp.PriceToPixelY(hit.Price);
            var (w, _) = renderer.MeasureText(hit.Text, sizePx);
            renderer.DrawText(x - w / 2, y - sizePx - 4, hit.Text, TextColor, sizePx);
        }
        if (ShowPatternCount)
        {
            string label = $"{_numPatternsFound} {Pattern}\npatterns found";
            var (lw, lh) = renderer.MeasureText(label, sizePx);
            renderer.DrawText(ctx.PlotArea.Right - lw - 8, ctx.PlotArea.Bottom - lh - 8, label, TextColor, sizePx);
        }
    }
}
