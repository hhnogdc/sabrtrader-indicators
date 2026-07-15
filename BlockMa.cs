using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// BlockMA — Block (stepped) Moving Average. 1-on-1 port of ATAS's
/// <c>BlockMA</c> (Technical/BlockMA.cs). Two ATR-sized "boxes" trail
/// price as stepped mid-lines: while price stays inside the current box
/// the line holds flat; a break above the box top re-anchors the box
/// under the new high, a break below the box bottom re-anchors it above
/// the new low. The box half-height is <c>Multiplier · ATR / 2</c>, so
/// two multipliers give a tight inner line and a wider outer line.
///
/// <para>For box k the per-bar rule is:</para>
/// <code>
///   box = Multiplier_k · ATR / 2
///   if  High &gt; prevTop:                 top=High;     bot=High−2·box; mid=High−box
///   elif Low &lt; prevBot &amp;&amp; High ≤ prevTop: bot=Low;      top=Low+2·box;  mid=Low+box
///   else:                                carry prevTop/prevBot/prevMid
/// </code>
/// Bars before the ATR period emit no value (NaN warm-up). Reuses
/// <see cref="Atr"/> as a child rather than duplicating Wilder's ATR.
/// Default Period=10, Multiplier1=1 (Mid1, red), Multiplier2=2 (Mid2, green).
/// </summary>
[IndicatorDescription("Block (stepped) Moving Average: two ATR-sized boxes trail price as stepped mid-lines, holding flat inside the box and re-anchoring on a break of the box top/bottom. Box half-height = Multiplier*ATR/2.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BlockMa : IndicatorBase
{
    private readonly ChunkedSeries<double> _mid1 = new();
    private readonly ChunkedSeries<double> _mid2 = new();
    private IndicatorOutputSeries? _mid1Wrap, _mid2Wrap;

    private Atr _atr = null!;        // detached child — supplies Wilder ATR
    private double _prevTop1, _prevBot1, _prevMid1;
    private double _prevTop2, _prevBot2, _prevMid2;
    private int _barIdx;

    public BlockMa(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Mid1", _mid1, PlotStyle.Line, NTColors.Red,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Mid2", _mid2, PlotStyle.Line, NTColors.Green, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "ATR Period", Description = "Period of the ATR driving the box size.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Multiplier1", Description = "ATR multiplier for the inner mid-line (Mid1).",
        MinValue = 0.0001, MaxValue = int.MaxValue, Step = 0.1)]
    public double Multiplier1 { get; set; } = 1;

    [IndicatorParameter(DisplayName = "Multiplier2", Description = "ATR multiplier for the outer mid-line (Mid2).",
        MinValue = 0.0001, MaxValue = int.MaxValue, Step = 0.1)]
    public double Multiplier2 { get; set; } = 2;

    public ISeries<double> Mid1 => _mid1Wrap ??= new IndicatorOutputSeries(_mid1, Bars);
    public ISeries<double> Mid2 => _mid2Wrap ??= new IndicatorOutputSeries(_mid2, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (Multiplier1 < 0.0001) throw new ArgumentOutOfRangeException(nameof(Multiplier1));
        if (Multiplier2 < 0.0001) throw new ArgumentOutOfRangeException(nameof(Multiplier2));
        _atr = new Atr(parent: null, bars: Bars) { Period = Period };
        _atr.OnInit(ctx);
        _prevTop1 = _prevBot1 = _prevMid1 = 0;
        _prevTop2 = _prevBot2 = _prevMid2 = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        _atr.OnDataLoaded(ctx);
        var bars = Bars;
        int n = bars.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            Compute(bars[barsAgo], _atr.Output[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        _atr.OnBarUpdate(ctx);
        Compute(bars[0], _atr.Output[0]);
    }

    private void Compute(Bar bar, double atr)
    {
        // ATAS gates output until the ATR window is established.
        if (_barIdx < Period)
        {
            _mid1.Append(double.NaN);
            _mid2.Append(double.NaN);
            _barIdx++;
            return;
        }

        double box1 = Multiplier1 * atr / 2.0;
        double box2 = Multiplier2 * atr / 2.0;

        Step(bar.High, bar.Low, box1, ref _prevTop1, ref _prevBot1, ref _prevMid1);
        Step(bar.High, bar.Low, box2, ref _prevTop2, ref _prevBot2, ref _prevMid2);

        _mid1.Append(_prevMid1);
        _mid2.Append(_prevMid2);
        _barIdx++;
    }

    private static void Step(double high, double low, double box,
                             ref double top, ref double bot, ref double mid)
    {
        if (high > top)
        {
            top = high; bot = high - 2 * box; mid = high - box;
        }
        else if (low < bot && high <= top)
        {
            bot = low; top = low + 2 * box; mid = low + box;
        }
        // else: hold prior top/bot/mid (price inside the box).
    }
}
