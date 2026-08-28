using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// MESA Adaptive MA (MAMA).
/// John Ehlers' Hilbert-Transform Discriminator: adapts smoothing constant per bar based
/// on the dominant cycle period of price action. Output: MAMA (Default, Goldenrod) + FAMA
/// (Following Adaptive MA, DodgerBlue). Overlay.
/// Defaults FastLimit=0.5, SlowLimit=0.05. Bars 0..5 → return 0 (warmup); bar 6 seeds.
/// </summary>
[IndicatorDescription("Ehlers MESA Adaptive Moving Average (MAMA) with companion FAMA: smoothing constant adapts each bar from the dominant cycle period via a Hilbert-transform discriminator.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Mama : IndicatorBase
{
    private readonly ChunkedSeries<double> _mama = new();
    private readonly ChunkedSeries<double> _fama = new();
    private IndicatorOutputSeries? _mamaW, _famaW;

    private readonly ChunkedSeries<double> _smooth    = new();
    private readonly ChunkedSeries<double> _detrender = new();
    private readonly ChunkedSeries<double> _i1        = new();
    private readonly ChunkedSeries<double> _q1        = new();
    private readonly ChunkedSeries<double> _i2        = new();
    private readonly ChunkedSeries<double> _q2        = new();
    private readonly ChunkedSeries<double> _re        = new();
    private readonly ChunkedSeries<double> _im        = new();
    private readonly ChunkedSeries<double> _period    = new();
    private readonly ChunkedSeries<double> _phase     = new();
    private int _barIdx;

    public Mama(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("MAMA", _mama, PlotStyle.Line, PlotColors.Goldenrod,  1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("FAMA", _fama, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "FastLimit", MinValue = 0.05, MaxValue = double.MaxValue, Step = 0.05)]
    public double FastLimit { get; set; } = 0.5;

    [IndicatorParameter(DisplayName = "SlowLimit", MinValue = 0.005, MaxValue = double.MaxValue, Step = 0.005)]
    public double SlowLimit { get; set; } = 0.05;

    public ISeries<double> Default => _mamaW ??= new IndicatorOutputSeries(_mama, Bars);
    public ISeries<double> Fama    => _famaW ??= new IndicatorOutputSeries(_fama, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (FastLimit < 0.05) throw new ArgumentOutOfRangeException(nameof(FastLimit));
        if (SlowLimit < 0.005) throw new ArgumentOutOfRangeException(nameof(SlowLimit));
        _barIdx = 0;
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
        double median = (bar.High + bar.Low) / 2.0;

        // Push fresh slot (0 default) into every series so per-bar indexing works.
        AppendAll();

        if (_barIdx < 6) { _mama.Set(0, 0); _fama.Set(0, 0); _barIdx++; return; }
        if (_barIdx == 6)
        {
            _mama.Set(0, median);
            _fama.Set(0, bar.Close);
            _barIdx++;
            return;
        }

        double period1 = _period[1];
        double smooth0 = (4 * median + 3 * _smooth[1] + 2 * _smooth[2] + _smooth[3]) / 10;
        _smooth.Set(0, smooth0);

        double k = 0.075 * period1 + 0.54;
        double detrender0 = (0.0962 * smooth0 + 0.5769 * _smooth[2]
                            - 0.5769 * _smooth[4] - 0.0962 * _smooth[6]) * k;
        _detrender.Set(0, detrender0);

        double q10 = (0.0962 * detrender0 + 0.5769 * _detrender[2]
                    - 0.5769 * _detrender[4] - 0.0962 * _detrender[6]) * k;
        _q1.Set(0, q10);

        double i10 = _detrender[3];
        _i1.Set(0, i10);

        double jI = (0.0962 * i10 + 0.5769 * _i1[2] - 0.5769 * _i1[4] - 0.0962 * _i1[6]) * k;
        double jQ = (0.0962 * q10 + 0.5769 * _q1[2] - 0.5769 * _q1[4] - 0.0962 * _q1[6]) * k;

        double i2raw = i10 - jQ;
        double q2raw = q10 + jI;
        double i20 = 0.2 * i2raw + 0.8 * _i2[1];
        double q20 = 0.2 * q2raw + 0.8 * _q2[1];
        _i2.Set(0, i20);
        _q2.Set(0, q20);

        double i21 = _i2[1], q21 = _q2[1];
        double reRaw = i20 * i21 + q20 * q21;
        double imRaw = i20 * q21 - q20 * i21;
        double re0 = 0.2 * reRaw + 0.8 * _re[1];
        double im0 = 0.2 * imRaw + 0.8 * _im[1];
        _re.Set(0, re0);
        _im.Set(0, im0);

        double period0 = _period[0];
        if (im0 != 0 && re0 != 0) period0 = 360 / (180 / Math.PI * Math.Atan(im0 / re0));
        if (period0 > 1.5  * period1) period0 = 1.5  * period1;
        if (period0 < 0.67 * period1) period0 = 0.67 * period1;
        if (period0 < 6)  period0 = 6;
        if (period0 > 50) period0 = 50;
        period0 = 0.2 * period0 + 0.8 * period1;
        _period.Set(0, period0);

        double phase0 = _phase[0];
        if (i10 != 0) phase0 = 180 / Math.PI * Math.Atan(q10 / i10);
        _phase.Set(0, phase0);

        double deltaPhase = _phase[1] - phase0;
        if (deltaPhase < 1) deltaPhase = 1;
        double alpha = FastLimit / deltaPhase;
        if (alpha < SlowLimit) alpha = SlowLimit;

        double mama0 = alpha * median + (1 - alpha) * _mama[1];
        double fama0 = 0.5 * alpha * mama0 + (1 - 0.5 * alpha) * _fama[1];
        _mama.Set(0, mama0);
        _fama.Set(0, fama0);
        _barIdx++;
    }

    private void AppendAll()
    {
        _mama.Append(0);
        _fama.Append(0);
        _smooth.Append(0);
        _detrender.Append(0);
        _i1.Append(0);
        _q1.Append(0);
        _i2.Append(0);
        _q2.Append(0);
        _re.Append(0);
        _im.Append(0);
        _period.Append(0);
        _phase.Append(0);
    }
}
