using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Strength Quality Index (RSX) — public approximation of Mark Jurik's
/// proprietary RSX (the exact algorithm is unpublished; this is the
/// widely-circulated cascaded low-lag filter used by the MT4 / Pine / MQL
/// community).
///
/// Same job as Wilder <see cref="Rsi"/> (0–100 momentum / trend-quality
/// oscillator, overbought above 70, oversold below 30) but with Jurik-style
/// multi-stage filtration so the line is substantially smoother without the
/// lag a plain EMA-of-RSI would add.
///
/// Per bar (src = input; community formula):
///   f18 = 3 / (Length + 2);  f20 = 1 − f18
///   Δ = 100·src − 100·src₋₁
///   Three cascaded (EMA, EMA, 1.5·fast−0.5·slow) stages on Δ → v14
///   Same cascade on |Δ| → v20
///   After Length-bar warmup: RSX = clamp((v14/v20 + 1)·50, 0, 100); else 50
///
/// Params: Length (default 14). Plot: DodgerBlue "RSX" line, new sub-panel.
/// Scalar input — so Input series can be Close or another indicator plot
/// (e.g. <c>Jma(Length=14).JMA</c>).
/// </summary>
[IndicatorDescription("Relative Strength Quality Index (public Jurik RSX approximation): a low-lag, noise-reduced RSI-style oscillator (0–100). Overbought above 70, oversold below 30.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Rsx : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    private double _f0, _f8, _f10, _f18, _f20;
    private double _f28, _f30, _f38, _f40, _f48, _f50;
    private double _f58, _f60, _f68, _f70, _f78, _f80;
    private double _f88, _f90;
    private bool _started;

    public Rsx(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var oscillator = new PlotSemantics(
            PlotKind.Oscillator, RangeMin: 0, RangeMax: 100,
            PlotValueUnit.Percent, PlotBullishDirection.Up,
            Reference: "30 oversold / 70 overbought");
        AddPlot(new Plot("RSX", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = oscillator });
        AddLine("Lower", 30, NTColors.DarkCyan);
        AddLine("Upper", 70, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Length",
        Description = "Filter length (larger = smoother/slower). Same role as RSI Period; default 14.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 14;

    /// <summary>RSX — chainable bar-sourced series.</summary>
    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 1) throw new ArgumentOutOfRangeException(nameof(Length));

        _f0 = _f8 = _f10 = _f18 = _f20 = 0;
        _f28 = _f30 = _f38 = _f40 = _f48 = _f50 = 0;
        _f58 = _f60 = _f68 = _f70 = _f78 = _f80 = 0;
        _f88 = _f90 = 0;
        _started = false;
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
        // Community RSX: first bar (and any re-seed after a flat warmup) initialises
        // coefficients and emits the neutral 50 until the Length window is filled.
        if (!_started || _f90 == 0.0)
        {
            _f90 = 1.0;
            _f0 = 0.0;
            _f88 = Length - 1 >= 5 ? Length - 1.0 : 5.0;
            _f8 = 100.0 * src;
            _f18 = 3.0 / (Length + 2.0);
            _f20 = 1.0 - _f18;
            _f28 = _f30 = _f38 = _f40 = _f48 = _f50 = 0;
            _f58 = _f60 = _f68 = _f70 = _f78 = _f80 = 0;
            _started = true;
            _output.Append(50.0);
            return;
        }

        if (_f88 <= _f90) _f90 = _f88 + 1.0;
        else _f90 += 1.0;

        _f10 = _f8;
        _f8 = 100.0 * src;
        double v8 = _f8 - _f10;

        _f28 = _f20 * _f28 + _f18 * v8;
        _f30 = _f18 * _f28 + _f20 * _f30;
        double vC = _f28 * 1.5 - _f30 * 0.5;

        _f38 = _f20 * _f38 + _f18 * vC;
        _f40 = _f18 * _f38 + _f20 * _f40;
        double v10 = _f38 * 1.5 - _f40 * 0.5;

        _f48 = _f20 * _f48 + _f18 * v10;
        _f50 = _f18 * _f48 + _f20 * _f50;
        double v14 = _f48 * 1.5 - _f50 * 0.5;

        _f58 = _f20 * _f58 + _f18 * Math.Abs(v8);
        _f60 = _f18 * _f58 + _f20 * _f60;
        double v18 = _f58 * 1.5 - _f60 * 0.5;

        _f68 = _f20 * _f68 + _f18 * v18;
        _f70 = _f18 * _f68 + _f20 * _f70;
        double v1C = _f68 * 1.5 - _f70 * 0.5;

        _f78 = _f20 * _f78 + _f18 * v1C;
        _f80 = _f18 * _f78 + _f20 * _f80;
        double v20 = _f78 * 1.5 - _f80 * 0.5;

        if (_f88 >= _f90 && _f8 != _f10) _f0 = 1.0;
        if (_f88 == _f90 && _f0 == 0.0) _f90 = 0.0;

        double v4;
        if (_f88 < _f90 && v20 > 1e-10)
        {
            v4 = (v14 / v20 + 1.0) * 50.0;
            if (v4 > 100.0) v4 = 100.0;
            else if (v4 < 0.0) v4 = 0.0;
        }
        else
        {
            v4 = 50.0;
        }

        _output.Append(v4);
    }
}
