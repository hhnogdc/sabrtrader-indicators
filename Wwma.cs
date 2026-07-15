using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// WWMA — Welles Wilder's Moving Average. 1-on-1 port of ATAS's
/// <c>WWMA</c> (Technical/WWMA.cs). Wilder's recursive smoothing
/// (α = 1/Period) exposed as a standalone overlay:
/// <c>WWMA[i] = WWMA[i−1] + (value − WWMA[i−1]) / Period</c>, seeded with
/// the first input. ATAS re-seeds from a skip-zeros SMA
/// (<see cref="Szma"/>) whenever the previous output is exactly 0 — so a
/// flat-at-zero series recovers cleanly. We reuse <see cref="Szma"/> as a
/// child for that seed rather than duplicating the math.
///
/// Mathematically the same family as <see cref="Smma"/> (Wilder α=1/P)
/// but with ATAS's first-input seed + zero-recovery, kept as a separate,
/// faithfully-named indicator. Default Period=10, overlay.
/// </summary>
[IndicatorDescription("Welles Wilder's Moving Average: Wilder smoothing WWMA = prev + (value - prev)/Period, seeded with the first input and re-seeded from a skip-zeros SMA when the prior value is zero. A standalone Wilder-smoothing plot.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Wwma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Szma _szma = null!;       // detached child — supplies the zero-recovery seed
    private double _value;            // WWMA[bar-1]
    private int _barIdx;

    public Wwma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("WWMA", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Wilder smoothing period — α = 1/Period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        // parent:null detached child fed the SAME input — we drive its
        // lifecycle ourselves (KeltnerChannel pattern) so there is no
        // auto-dispatch double-processing.
        _szma = new Szma(parent: null, input: Input) { Period = Period };
        _szma.OnInit(ctx);
        _value = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        _szma.OnDataLoaded(ctx);
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            Compute(src[barsAgo], _szma.Output[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        _szma.OnBarUpdate(ctx);
        Compute(src[0], _szma.Output[0]);
    }

    private void Compute(double v, double szmaSeed)
    {
        if (_barIdx == 0)
            _value = v;
        else
            _value = _value == 0 ? szmaSeed : _value + (v - _value) / Period;
        _output.Append(_value);
        _barIdx++;
    }
}
