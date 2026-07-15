using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Connors RSI — 1-on-1 port of Skender ConnorsRsi.
///   1. RSI(Close, RsiPeriods)
///   2. Streak = signed consecutive up/down day count;
///      Wilder-RSI on the Streak series, length StreakPeriods.
///   3. Gain = (Close − prevClose) / prevClose; PercentRank of today's gain over
///      the last RankPeriods (returns 0..100).
///   ConnorsRsi = (RSI + StreakRSI + PercentRank) / 3
/// Defaults RsiPeriods=3, StreakPeriods=2, RankPeriods=100.
/// DodgerBlue line, 10/90 reference lines, sub-panel. NaN until startPeriod.
/// </summary>
[IndicatorDescription("Connors RSI. Composite mean-reversion oscillator averaging three components: a short Wilder RSI of close, a Wilder RSI of the up/down streak, and the percent rank of today's return over RankPeriods. Values <10 / >90 mark extremes.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class ConnorsRsi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _streakRsi = new();
    private IndicatorOutputSeries? _wrap, _streakW;

    // Component 1: Wilder RSI of Close
    private double _rsiAvgUp, _rsiAvgDown;
    private double _rsiSeedUp, _rsiSeedDown;
    private bool _rsiSeeded;
    private double _prevClose;
    private int _rsiDeltaCount;

    // Component 2: Wilder RSI of Streak
    private double _streakRsiAvgUp, _streakRsiAvgDown;
    private double _streakRsiSeedUp, _streakRsiSeedDown;
    private bool _streakRsiSeeded;
    private double _prevStreak;
    private int _streakRsiDeltaCount;

    // Streak tracker
    private double _streak;
    private bool _hasPrevClose;

    // Gain history for percent rank
    private double[] _gainRing = [];
    private int _gainHead;
    private int _gainCount;

    private double _lastRsi;

    public ConnorsRsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ConnorsRSI", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "10 oversold / 90 overbought") });
        AddLine("Overbought", 90, NTColors.DarkGray);
        AddLine("Oversold",   10, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "RsiPeriods",    MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiPeriods    { get; set; } = 3;

    [IndicatorParameter(DisplayName = "StreakPeriods", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int StreakPeriods { get; set; } = 2;

    [IndicatorParameter(DisplayName = "RankPeriods",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RankPeriods   { get; set; } = 100;

    public ISeries<double> Output    => _wrap    ??= new IndicatorOutputSeries(_output,    Bars);
    public ISeries<double> StreakRsi => _streakW ??= new IndicatorOutputSeries(_streakRsi, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RsiPeriods < 1 || StreakPeriods < 1 || RankPeriods < 1) throw new ArgumentOutOfRangeException();
        _rsiSeeded = _streakRsiSeeded = _hasPrevClose = false;
        _rsiAvgUp = _rsiAvgDown = _rsiSeedUp = _rsiSeedDown = 0;
        _streakRsiAvgUp = _streakRsiAvgDown = _streakRsiSeedUp = _streakRsiSeedDown = 0;
        _rsiDeltaCount = _streakRsiDeltaCount = 0;
        _streak = 0;
        _gainRing = new double[RankPeriods + 1];      // need RankPeriods + 1 samples (today + RankPeriods prior)
        _gainHead = _gainCount = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double close)
    {
        // Component 1: Wilder RSI on Close.
        double rsiVal = double.NaN;
        if (_hasPrevClose)
        {
            double delta = close - _prevClose;
            double up   = delta > 0 ?  delta : 0;
            double down = delta < 0 ? -delta : 0;
            _rsiDeltaCount++;
            if (!_rsiSeeded)
            {
                _rsiSeedUp += up; _rsiSeedDown += down;
                if (_rsiDeltaCount >= RsiPeriods)
                {
                    _rsiAvgUp = _rsiSeedUp / RsiPeriods;
                    _rsiAvgDown = _rsiSeedDown / RsiPeriods;
                    _rsiSeeded = true;
                }
            }
            else
            {
                _rsiAvgUp   = (_rsiAvgUp   * (RsiPeriods - 1) + up)   / RsiPeriods;
                _rsiAvgDown = (_rsiAvgDown * (RsiPeriods - 1) + down) / RsiPeriods;
            }
            if (_rsiSeeded) rsiVal = _rsiAvgDown == 0 ? 100 : 100 - 100 / (1 + _rsiAvgUp / _rsiAvgDown);
        }
        _lastRsi = rsiVal;

        // Streak update.
        double newStreak;
        if (!_hasPrevClose) newStreak = 0;
        else if (close > _prevClose) newStreak = _streak >= 0 ? _streak + 1 : 1;
        else if (close < _prevClose) newStreak = _streak <= 0 ? _streak - 1 : -1;
        else                          newStreak = 0;

        // Component 2: Wilder RSI on streak (today's streak minus yesterday's streak).
        double streakRsiVal = double.NaN;
        if (_hasPrevClose)   // need at least one prior streak value
        {
            double dStreak = newStreak - _prevStreak;
            double up   = dStreak > 0 ?  dStreak : 0;
            double down = dStreak < 0 ? -dStreak : 0;
            _streakRsiDeltaCount++;
            if (!_streakRsiSeeded)
            {
                _streakRsiSeedUp += up; _streakRsiSeedDown += down;
                if (_streakRsiDeltaCount >= StreakPeriods)
                {
                    _streakRsiAvgUp   = _streakRsiSeedUp   / StreakPeriods;
                    _streakRsiAvgDown = _streakRsiSeedDown / StreakPeriods;
                    _streakRsiSeeded = true;
                }
            }
            else
            {
                _streakRsiAvgUp   = (_streakRsiAvgUp   * (StreakPeriods - 1) + up)   / StreakPeriods;
                _streakRsiAvgDown = (_streakRsiAvgDown * (StreakPeriods - 1) + down) / StreakPeriods;
            }
            if (_streakRsiSeeded)
                streakRsiVal = _streakRsiAvgDown == 0 ? 100 : 100 - 100 / (1 + _streakRsiAvgUp / _streakRsiAvgDown);
        }
        _streakRsi.Append(streakRsiVal);

        // Component 3: PercentRank of today's gain over last RankPeriods.
        double gain = _hasPrevClose && _prevClose > 0 ? (close - _prevClose) / _prevClose : double.NaN;
        double percentRank = double.NaN;
        if (!double.IsNaN(gain))
        {
            // Push gain into ring (capacity RankPeriods+1).
            _gainRing[_gainHead] = gain;
            _gainHead = (_gainHead + 1) % _gainRing.Length;
            if (_gainCount < _gainRing.Length) _gainCount++;

            if (_gainCount >= RankPeriods + 1)
            {
                // Count how many of the prior RankPeriods gains are < today's gain.
                // Today's gain just got added, so it's at index (_gainHead - 1) in ring;
                // the RankPeriods prior are the other entries.
                int qty = 0;
                bool viable = true;
                for (int k = 0; k < _gainRing.Length; k++)
                {
                    double g = _gainRing[k];
                    if (double.IsNaN(g)) { viable = false; break; }
                }
                if (viable)
                {
                    for (int k = 0; k < _gainRing.Length; k++)
                    {
                        int prevIdx = (_gainHead - 1 + _gainRing.Length) % _gainRing.Length;
                        if (k == prevIdx) continue;   // skip today's gain
                        if (_gainRing[k] < gain) qty++;
                    }
                    percentRank = 100.0 * qty / RankPeriods;
                }
            }
        }

        // Combine into ConnorsRSI.
        if (!double.IsNaN(rsiVal) && !double.IsNaN(streakRsiVal) && !double.IsNaN(percentRank))
            _output.Append((rsiVal + streakRsiVal + percentRank) / 3);
        else
            _output.Append(double.NaN);

        // Advance.
        if (_hasPrevClose) _prevStreak = newStreak;
        else                _prevStreak = 0;
        _streak = newStreak;
        _prevClose = close;
        _hasPrevClose = true;
    }
}
