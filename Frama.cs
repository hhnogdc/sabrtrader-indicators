using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// FRAMA — Fractal Adaptive Moving Average (John Ehlers, 2005). Adaptive smoothing where the
/// smoothing constant α tracks the price-action fractal dimension:
///   Split last <c>Period</c> bars into two halves of <c>Period/2</c>:
///     H1,L1 = max/min of OLDER half;  H2,L2 = max/min of NEWER half;  H,L = max/min of all Period bars
///     N1 = (H1−L1)/(P/2); N2 = (H2−L2)/(P/2); N3 = (H−L)/P
///     D  = (log(N1+N2) − log(N3)) / log 2     ← fractal dimension
///     α  = exp(W·(D − 1))                     ← W = −4.6 (Ehlers default)
///     α  clamped to [<see cref="AlphaMin"/>, 1] (default min 0.01)
///   FRAMA[i] = α·Price[i] + (1−α)·FRAMA[i−1]
/// Seed: SMA(Period). NaN until Period bars are available. Defaults Period=16 (must be EVEN),
/// AlphaMin=0.01, W=−4.6. DodgerBlue, overlay.
/// </summary>
[IndicatorDescription("FRAMA — Fractal Adaptive Moving Average (Ehlers): adapts its smoothing α = exp(W·(D−1)) to the fractal dimension of the last Period bars, tightening in trends and loosening in chop.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Frama : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    // Ring buffers of High and Low over the last Period bars.
    private double[] _hRing = null!;
    private double[] _lRing = null!;
    private int _head, _count;
    private double _state;
    private bool _seeded;
    private double _seedSum;

    public Frama(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("FRAMA", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",   Description = "Lookback (must be EVEN, ≥ 4).",
        MinValue = 4, MaxValue = int.MaxValue, Step = 2)]
    public int    Period   { get; set; } = 16;

    [IndicatorParameter(DisplayName = "AlphaMin",
        Description = "Clamp for adaptive α — Ehlers default 0.01.",
        MinValue = 0.001, MaxValue = 1, Step = 0.01)]
    public double AlphaMin { get; set; } = 0.01;

    [IndicatorParameter(DisplayName = "W",
        Description = "Decay constant for α = exp(W·(D−1)). Ehlers default −4.6.",
        MinValue = -100, MaxValue = -0.001, Step = 0.1)]
    public double W        { get; set; } = -4.6;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 4 || Period % 2 != 0) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be even and ≥ 4.");
        if (AlphaMin <= 0 || AlphaMin > 1) throw new ArgumentOutOfRangeException(nameof(AlphaMin));
        if (W >= 0) throw new ArgumentOutOfRangeException(nameof(W), "W must be negative.");
        _hRing = new double[Period];
        _lRing = new double[Period];
        _head = _count = 0;
        _state = 0;
        _seeded = false;
        _seedSum = 0;
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
        _hRing[_head] = bar.High;
        _lRing[_head] = bar.Low;
        _head = (_head + 1) % Period;
        if (_count < Period) _count++;
        _seedSum += bar.Close;

        if (_count < Period) { _output.Append(double.NaN); return; }
        if (!_seeded)
        {
            _state = _seedSum / Period;
            _seeded = true;
            _output.Append(_state);
            return;
        }

        int halfP = Period / 2;
        // Newer half = the last halfP slots written = indices (head-halfP .. head-1).
        // Older half = the halfP slots before that = indices (head-Period .. head-halfP-1).
        double h1 = double.MinValue, l1 = double.MaxValue;
        double h2 = double.MinValue, l2 = double.MaxValue;
        double hAll = double.MinValue, lAll = double.MaxValue;
        for (int k = 0; k < Period; k++)
        {
            int idx = (_head + k) % Period;     // oldest..newest as k goes 0..Period-1
            double h = _hRing[idx], l = _lRing[idx];
            if (h > hAll) hAll = h;
            if (l < lAll) lAll = l;
            if (k < halfP)
            { if (h > h1) h1 = h; if (l < l1) l1 = l; }
            else
            { if (h > h2) h2 = h; if (l < l2) l2 = l; }
        }
        double n1 = (h1 - l1) / halfP;
        double n2 = (h2 - l2) / halfP;
        double n3 = (hAll - lAll) / Period;
        double alpha;
        if (n1 > 0 && n2 > 0 && n3 > 0)
        {
            double d = (Math.Log(n1 + n2) - Math.Log(n3)) / Math.Log(2);
            alpha = Math.Exp(W * (d - 1));
            if (alpha < AlphaMin) alpha = AlphaMin;
            if (alpha > 1) alpha = 1;
        }
        else alpha = AlphaMin;

        _state = alpha * bar.Close + (1 - alpha) * _state;
        _output.Append(_state);
    }
}
