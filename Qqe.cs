using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// QQE (Quantitative Qualitative Estimation, Igor Livshin) — a
/// smoothed RSI paired with an ATR-derived trailing line. The fast
/// line is an EMA of RSI; the slow line is a volatility-adaptive
/// trailing stop of that fast line. Crossings of the two, and the
/// 50 centerline, are the signals.
///
/// Math:
///   <code>
///   rsi        = RSI(Input, RsiPeriod)
///   RsiMa      = EMA(rsi, SmoothFactor)                 // fast line
///   atrRsi     = |RsiMa − RsiMa[1]|
///   MaAtrRsi   = EMA(atrRsi, Wilders)                   // Wilders = RsiPeriod·2 − 1
///   dar        = EMA(MaAtrRsi, Wilders) · QqeFactor     // adaptive band width
///   </code>
/// then a longband/shortband trailing-stop walk over RsiMa produces
/// the slow line (longband while up-trending, shortband while down).
///
/// Defaults RsiPeriod=14, SmoothFactor=5, QqeFactor=4.236. Plots
/// RsiMa (DodgerBlue) + QQE trailing line (Goldenrod), 50 centerline,
/// range 0–100, sub-panel.
/// </summary>
[IndicatorDescription("QQE (Quantitative Qualitative Estimation): an EMA-smoothed RSI (fast line) with an ATR-of-RSI volatility-adaptive trailing stop (slow line). Fast/slow crossovers and the 50 level are the signals.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Qqe : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _fast = new();   // RsiMa
    private readonly ChunkedSeries<double> _slow = new();   // trailing line
    private IndicatorOutputSeries? _fastWrap, _slowWrap;

    private Rsi _rsi = null!;
    private double _alphaSf, _alphaW;
    private double _rsiMa; private bool _hasRsiMa;
    private double _prevRsiMa; private bool _hasPrevRsiMa;
    private double _maAtr; private bool _hasMaAtr;
    private double _darEma; private bool _hasDar;
    // Trailing state
    private bool _seededTrail;
    private int _trend;
    private double _longband, _shortband;
    private double _prevRSIndex, _prevLongband, _prevShortband;

    public Qqe(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var sem = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "50 centerline");
        AddPlot(new Plot("RsiMa", _fast, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = sem });
        AddPlot(new Plot("QQE",   _slow, PlotStyle.Line, NTColors.Goldenrod,  1.5) { Semantics = sem });
        AddLine("Centerline", 50, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "RsiPeriod", Description = "Wilder RSI period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiPeriod { get; set; } = 14;

    [IndicatorParameter(DisplayName = "SmoothFactor", Description = "EMA smoothing of the RSI (gives the fast line).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothFactor { get; set; } = 5;

    [IndicatorParameter(DisplayName = "QqeFactor", Description = "Multiplier on the smoothed ATR-of-RSI band width.", MinValue = 0.1, MaxValue = double.MaxValue, Step = 0.1)]
    public double QqeFactor { get; set; } = 4.236;

    /// <summary>RsiMa — the fast (smoothed RSI) line.</summary>
    public ISeries<double> Output => _fastWrap ??= new IndicatorOutputSeries(_fast, Bars);

    /// <summary>The slow ATR-trailing line.</summary>
    public ISeries<double> Trailing => _slowWrap ??= new IndicatorOutputSeries(_slow, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RsiPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RsiPeriod));
        if (SmoothFactor < 1) throw new ArgumentOutOfRangeException(nameof(SmoothFactor));
        if (QqeFactor <= 0) throw new ArgumentOutOfRangeException(nameof(QqeFactor));
        _rsi = new Rsi(this, input: Input) { Period = RsiPeriod, Smooth = 1 };
        int wilders = RsiPeriod * 2 - 1;
        if (wilders < 1) wilders = 1;
        _alphaSf = 2.0 / (SmoothFactor + 1);
        _alphaW = 2.0 / (wilders + 1);
        _hasRsiMa = _hasPrevRsiMa = _hasMaAtr = _hasDar = false;
        _rsiMa = _prevRsiMa = _maAtr = _darEma = 0;
        _seededTrail = false;
        _trend = 1;
        _longband = _shortband = _prevRSIndex = _prevLongband = _prevShortband = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _rsi.Default.Count;
        for (int i = 0; i < n; i++) Step(n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_rsi.Default.Count == 0) return;
        Step(0);
    }

    private void Step(int barsAgo)
    {
        double rsi = _rsi.Default[barsAgo];
        if (double.IsNaN(rsi)) { _fast.Append(double.NaN); _slow.Append(double.NaN); return; }

        // Fast line: EMA of RSI.
        if (!_hasRsiMa) { _rsiMa = rsi; _hasRsiMa = true; }
        else             _rsiMa = _alphaSf * rsi + (1 - _alphaSf) * _rsiMa;

        // ATR of the fast line.
        double atrRsi = _hasPrevRsiMa ? Math.Abs(_rsiMa - _prevRsiMa) : 0;
        _prevRsiMa = _rsiMa;
        _hasPrevRsiMa = true;

        // Two stacked Wilder-period EMAs → adaptive band width.
        if (!_hasMaAtr) { _maAtr = atrRsi; _hasMaAtr = true; }
        else             _maAtr = _alphaW * atrRsi + (1 - _alphaW) * _maAtr;
        if (!_hasDar) { _darEma = _maAtr; _hasDar = true; }
        else           _darEma = _alphaW * _maAtr + (1 - _alphaW) * _darEma;
        double dar = _darEma * QqeFactor;

        double rsIndex = _rsiMa;
        double newLong = rsIndex - dar;
        double newShort = rsIndex + dar;

        if (!_seededTrail)
        {
            _longband = newLong;
            _shortband = newShort;
            _trend = 1;
            _seededTrail = true;
        }
        else
        {
            _longband = (_prevRSIndex > _prevLongband && rsIndex > _prevLongband)
                ? Math.Max(_prevLongband, newLong) : newLong;
            _shortband = (_prevRSIndex < _prevShortband && rsIndex < _prevShortband)
                ? Math.Min(_prevShortband, newShort) : newShort;

            if (rsIndex > _prevShortband) _trend = 1;
            else if (rsIndex < _prevLongband) _trend = -1;
            // else: trend unchanged
        }

        double trail = _trend == 1 ? _longband : _shortband;
        _prevRSIndex = rsIndex;
        _prevLongband = _longband;
        _prevShortband = _shortband;

        _fast.Append(_rsiMa);
        _slow.Append(trail);
    }
}
