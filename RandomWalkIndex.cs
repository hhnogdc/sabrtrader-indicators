using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Random Walk Index (Michael Poulos) — gauges how far price has
/// travelled relative to what a pure random walk would produce over
/// the same span. Readings above 1 mean the move is statistically
/// "too straight" to be noise, i.e. a real trend; below 1 is range/noise.
///
/// Math (per bar, maximised over every lookback k = 2…Period):
///   <code>
///   RWIHigh = max over k of  (High[0] − Low[k])  / (AvgTrueRange(k) · √k)
///   RWILow  = max over k of  (High[k] − Low[0])  / (AvgTrueRange(k) · √k)
///   </code>
/// where <c>AvgTrueRange(k)</c> is the mean true range over the last k
/// bars and <c>High[k] / Low[k]</c> are the high/low k bars ago.
/// √k is the random-walk scaling: a random walk's expected travel
/// grows with √time, so dividing normalises against it.
///
/// Defaults Period=14. Plots RWIHigh (DodgerBlue) / RWILow (Crimson),
/// a reference line at 1.0, sub-panel. Unbounded (typically 0–5).
/// </summary>
[IndicatorDescription("Random Walk Index (Poulos): the largest ratio of actual price travel to the random-walk expectation (ATR*sqrt(k)) across lookbacks k=2..Period, split into RWIHigh and RWILow. Above 1 signals a statistically real trend; below 1 is noise.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class RandomWalkIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _high = new();
    private readonly ChunkedSeries<double> _low = new();
    private IndicatorOutputSeries? _hiWrap, _loWrap;

    private double[] _highs = [];   // ring, capacity Period+1
    private double[] _lows = [];
    private double[] _trs = [];      // ring, capacity Period
    private int _hlHead, _trHead;
    private int _n;                  // bars seen
    private double? _prevClose;

    public RandomWalkIndex(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("RWI High", _high, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up,   "1.0 = random-walk threshold") });
        AddPlot(new Plot("RWI Low",  _low,  PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Down, "1.0 = random-walk threshold") });
        AddLine("Trend threshold", 1.0, NTColors.DarkCyan, participatesInAutoScale: false);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Maximum lookback k for the random-walk comparison.", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> High => _hiWrap ??= new IndicatorOutputSeries(_high, Bars);
    public ISeries<double> Low  => _loWrap ??= new IndicatorOutputSeries(_low,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 2.");
        _highs = new double[Period + 1];
        _lows = new double[Period + 1];
        _trs = new double[Period];
        _hlHead = _trHead = 0;
        _n = 0;
        _prevClose = null;
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
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        _prevClose = bar.Close;

        _highs[_hlHead] = bar.High;
        _lows[_hlHead] = bar.Low;
        _hlHead = (_hlHead + 1) % _highs.Length;
        _trs[_trHead] = tr;
        _trHead = (_trHead + 1) % _trs.Length;
        _n++;

        int maxK = Math.Min(Period, _n - 1);   // need k+1 bars of history
        double rwiHigh = 0, rwiLow = 0;
        for (int k = 2; k <= maxK; k++)
        {
            double avgTr = AvgTr(k);
            if (avgTr <= 0) continue;
            double denom = avgTr * Math.Sqrt(k);
            double hi = (HlAgo(_highs, 0) - HlAgo(_lows, k)) / denom;
            double lo = (HlAgo(_highs, k) - HlAgo(_lows, 0)) / denom;
            if (hi > rwiHigh) rwiHigh = hi;
            if (lo > rwiLow) rwiLow = lo;
        }
        _high.Append(rwiHigh);
        _low.Append(rwiLow);
    }

    private double HlAgo(double[] ring, int barsAgo)
    {
        int cap = ring.Length;
        int idx = ((_hlHead - 1 - barsAgo) % cap + cap) % cap;
        return ring[idx];
    }

    private double AvgTr(int k)
    {
        // Mean TR over the last k bars (barsAgo 0..k-1).
        int cap = _trs.Length;
        double sum = 0;
        for (int a = 0; a < k; a++)
        {
            int idx = ((_trHead - 1 - a) % cap + cap) % cap;
            sum += _trs[idx];
        }
        return sum / k;
    }
}
