using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Jurik Moving Average (JMA) — public approximation of Mark Jurik's proprietary
/// JMA (the exact algorithm is unpublished; this is the widely-circulated
/// three-stage adaptive filter used by the Pine/MQL community).
///
/// It is a fully <b>causal</b> (one-sided) recursive filter: it references only
/// past values, so — unlike a centered Triangular MA — it <b>never repaints</b>,
/// yet keeps a centered-MA-like smoothness with low lag. That is exactly the
/// trade the "smooth, non-repainting TMA-bands" competitors make.
///
/// Per bar (src = input):
///   beta  = 0.45·(Length−1) / (0.45·(Length−1)+2)
///   alpha = beta ^ Power
///   phaseRatio = Phase&lt;−100 ? 0.5 : Phase&gt;100 ? 2.5 : Phase/100 + 1.5
///   e0 = (1−alpha)·src + alpha·e0₋₁                       (adaptive EMA)
///   e1 = (src − e0)·(1−beta) + beta·e1₋₁                  (momentum / velocity)
///   e2 = (e0 + phaseRatio·e1 − jma₋₁)·(1−alpha)² + alpha²·e2₋₁
///   jma = e2 + jma₋₁
///
/// Seeded original-style with the first input (jma[0] = src[0]) so there is no NaN
/// warmup and no startup spike — matches <see cref="Ema"/>'s convention.
///
/// Params: Length (smoothing window), Phase (−100..100, lag↔overshoot trade),
/// Power (≥1, higher = smoother/slower). Defaults Length=14, Phase=0, Power=2.
/// Plot: DodgerBlue line, overlay.
/// </summary>
[IndicatorDescription("Jurik Moving Average (public approximation): a causal, non-repainting adaptive filter that is nearly as smooth as a centered MA but with low lag. Phase trades lag against overshoot; Power controls smoothness.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Jma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    // Precomputed coefficients (depend only on parameters).
    private double _alpha, _oneMinusAlpha, _oneMinusAlphaSq, _alphaSq;
    private double _beta, _oneMinusBeta, _phaseRatio;

    // Recursive state.
    private double _e0, _e1, _e2, _jma;
    private bool _hasValue;

    public Jma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("JMA", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Length", Description = "Smoothing window (larger = smoother/slower).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Phase", Description = "Lag↔overshoot trade in [−100, 100]. 0 is neutral; higher reduces lag at the cost of overshoot.",
        MinValue = -100, MaxValue = 100, Step = 1)]
    public double Phase { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Power", Description = "Adaptation power (≥ 1). Higher = smoother and slower.",
        MinValue = 1, MaxValue = double.MaxValue, Step = 1)]
    public double Power { get; set; } = 2;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 1) throw new ArgumentOutOfRangeException(nameof(Length));
        if (Power < 1) throw new ArgumentOutOfRangeException(nameof(Power));

        _beta = 0.45 * (Length - 1) / (0.45 * (Length - 1) + 2);
        _alpha = Math.Pow(_beta, Power);
        _oneMinusAlpha = 1.0 - _alpha;
        _oneMinusAlphaSq = _oneMinusAlpha * _oneMinusAlpha;
        _alphaSq = _alpha * _alpha;
        _oneMinusBeta = 1.0 - _beta;
        _phaseRatio = Phase < -100 ? 0.5 : Phase > 100 ? 2.5 : Phase / 100.0 + 1.5;

        _e0 = _e1 = _e2 = _jma = 0;
        _hasValue = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int i = 0; i < n; i++) Compute(src[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input[0]);
    }

    private void Compute(double src)
    {
        if (!_hasValue)
        {
            // original-style seed: first bar = first input. No NaN warmup, no startup spike.
            _e0 = src; _e1 = 0; _e2 = 0; _jma = src;
            _hasValue = true;
            _output.Append(_jma);
            return;
        }
        _e0 = _oneMinusAlpha * src + _alpha * _e0;
        _e1 = (src - _e0) * _oneMinusBeta + _beta * _e1;
        _e2 = (_e0 + _phaseRatio * _e1 - _jma) * _oneMinusAlphaSq + _alphaSq * _e2;
        _jma = _e2 + _jma;
        _output.Append(_jma);
    }
}
