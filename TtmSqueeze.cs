using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// TTM Squeeze (John Carter, LazyBear) — detects volatility compression
/// (Bollinger Bands inside Keltner Channels) + a linear-regression momentum histogram.
///
/// Squeeze states:
///   Squeeze ON  = BB upper &lt; KC upper AND BB lower &gt; KC lower (compression)
///   Squeeze OFF = otherwise (release)
///
/// Momentum (Carter's formula):
///   delta = Close − ((max(High,P) + min(Low,P))/2 + SMA(Close,P))/2
///   Momentum = linear-regression value (slope·(P−1) + intercept) of delta over P bars
///
/// Defaults Length=20, BollMult=2, KeltnerMult=1.5.
///
/// Plots:
///   - Momentum (line, color based on rising/falling and sign — exposed via per-bar series here;
///     standard plot uses a single color; a future per-bar coloring feature can split it 4-way)
///   - SqueezeOn (Dots at zero: yellow when squeeze ON, gray when OFF)
/// Sub-panel, zero reference line.
/// </summary>
[IndicatorDescription("John Carter's TTM Squeeze. Signals volatility compression when Bollinger Bands contract inside the Keltner Channels and plots a linear-regression momentum histogram for the release direction.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class TtmSqueeze : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _momentum  = new();
    private readonly ChunkedSeries<double> _squeezeOn = new();   // 1 when squeezed, 0 when released, NaN when warmup
    private IndicatorOutputSeries? _mW, _sW;

    private RollingSum _closeSum = null!;
    private RollingSum _closeSqSum = null!;
    private RollingMinMax _maxH = null!, _minL = null!;
    private double _prevClose;
    private bool _hasPrevClose;
    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;

    // Rolling buffer of "delta" values for the LinReg fit.
    private double[] _deltaRing = null!;
    private int _ringHead, _ringCount;

    public TtmSqueeze(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Momentum",  _momentum,  PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("Squeeze",   _squeezeOn, PlotStyle.Dots,      PlotColors.Yellow,     3.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Length",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int    Length      { get; set; } = 20;

    [IndicatorParameter(DisplayName = "BB σ",       MinValue = 0.1, MaxValue = double.MaxValue, Step = 0.1)]
    public double BollMult    { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Keltner ×",  MinValue = 0.1, MaxValue = double.MaxValue, Step = 0.1)]
    public double KeltnerMult { get; set; } = 1.5;

    public ISeries<double> Momentum => _mW ??= new IndicatorOutputSeries(_momentum,  Bars);
    public ISeries<double> Squeeze  => _sW ??= new IndicatorOutputSeries(_squeezeOn, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 1) throw new ArgumentOutOfRangeException(nameof(Length));
        if (BollMult <= 0 || KeltnerMult <= 0) throw new ArgumentOutOfRangeException();
        _closeSum   = new RollingSum(Length);
        _closeSqSum = new RollingSum(Length);
        _maxH = new RollingMinMax(Length, trackMax: true);
        _minL = new RollingMinMax(Length, trackMax: false);
        _atrSeeded = _hasPrevClose = false;
        _atrSeedSum = 0; _atrSeedCount = 0;
        _deltaRing = new double[Length];
        _ringHead = _ringCount = 0;
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
        _closeSum.Push(bar.Close);
        _closeSqSum.Push(bar.Close * bar.Close);
        _maxH.Push(bar.High);
        _minL.Push(bar.Low);

        // Wilder ATR for Keltner width.
        double tr = !_hasPrevClose
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose), Math.Abs(bar.Low - _prevClose)));
        _prevClose = bar.Close; _hasPrevClose = true;
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= Length) { _atr = _atrSeedSum / Length; _atrSeeded = true; }
        }
        else _atr = (_atr * (Length - 1) + tr) / Length;

        if (_closeSum.Count < Length || !_atrSeeded)
        {
            _momentum.Append(double.NaN); _squeezeOn.Append(double.NaN);
            return;
        }

        double sma = _closeSum.Sum / Length;
        // Population stddev for BB width.
        double var = _closeSqSum.Sum / Length - sma * sma;
        if (var < 0) var = 0;
        double sd = Math.Sqrt(var);
        double bbUpper = sma + BollMult * sd;
        double bbLower = sma - BollMult * sd;
        double kcUpper = sma + KeltnerMult * _atr;
        double kcLower = sma - KeltnerMult * _atr;
        bool squeezed = bbUpper < kcUpper && bbLower > kcLower;
        _squeezeOn.Append(squeezed ? 0 : double.NaN);    // dot rendered only when ON (at the zero line)

        // Carter momentum: linear regression of (Close − midpoint) over Length bars.
        double midH = _maxH.Value, midL = _minL.Value;
        double midpoint = ((midH + midL) / 2 + sma) / 2;
        double delta = bar.Close - midpoint;

        _deltaRing[_ringHead] = delta;
        _ringHead = (_ringHead + 1) % Length;
        if (_ringCount < Length) _ringCount++;

        if (_ringCount < Length) { _momentum.Append(double.NaN); return; }

        // OLS over the ring: oldest..newest at x=0..Length-1, value y.
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        int startIdx = _ringHead;     // oldest is at head (next slot to overwrite)
        for (int k = 0; k < Length; k++)
        {
            double y = _deltaRing[(startIdx + k) % Length];
            sumX += k; sumY += y; sumXY += k * y; sumXX += (double)k * k;
        }
        double denom = Length * sumXX - sumX * sumX;
        double slope = denom == 0 ? 0 : (Length * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / Length;
        double momentum = intercept + slope * (Length - 1);
        _momentum.Append(momentum);
    }
}
