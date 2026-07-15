using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Hilbert Transform Instantaneous Trendline (HTL, Ehlers) — 1-on-1 port of Skender HtTrendline.
/// Operates on HL2. Identifies dominant-cycle period via the homodyne discriminator and outputs
/// an instantaneous trendline = WMA( running mean of price over the smoothed dominant-cycle
/// period, [4,3,2,1]/10 ). SmoothPrice = WMA(price, [4,3,2,1]/10).
/// First 5 bars seed; bars 6..10 use price as trendline; bar 11+ uses the 4-bar weighted average.
/// No parameters. DodgerBlue Trendline, DarkGray SmoothPrice, overlay.
/// </summary>
[IndicatorDescription("Hilbert Transform Instantaneous Trendline (Ehlers): runs the homodyne discriminator on HL2 to estimate the dominant cycle period, then averages price over that period for a near-zero-lag trendline.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class HtTrendline : IndicatorBase
{
    private readonly ChunkedSeries<double> _trend  = new();
    private readonly ChunkedSeries<double> _smooth = new();
    private readonly ChunkedSeries<double> _dcPeriods = new();
    private IndicatorOutputSeries? _tW, _sW, _dW;

    // Rolling 12-bar history for HL2-derived series.
    private double[] _pr   = new double[12];
    private double[] _sp   = new double[12];
    private double[] _dt   = new double[12];
    private double[] _i1   = new double[12];
    private double[] _q1   = new double[12];
    private double[] _i2   = new double[12];
    private double[] _q2   = new double[12];
    private double[] _re   = new double[12];
    private double[] _im   = new double[12];
    private double[] _pd   = new double[12];
    private double[] _sd   = new double[12];
    private double[] _it   = new double[12];

    private int _barIdx;

    public HtTrendline(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("HT Trendline", _trend,  PlotStyle.Line, NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("SmoothPrice",  _smooth, PlotStyle.Line, NTColors.DarkGray,   1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("DC Periods",   _dcPeriods, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    public ISeries<double> Trendline   => _tW ??= new IndicatorOutputSeries(_trend,     Bars);
    public ISeries<double> SmoothPrice => _sW ??= new IndicatorOutputSeries(_smooth,    Bars);
    public ISeries<double> DcPeriods   => _dW ??= new IndicatorOutputSeries(_dcPeriods, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        Array.Clear(_pr); Array.Clear(_sp); Array.Clear(_dt);
        Array.Clear(_i1); Array.Clear(_q1); Array.Clear(_i2); Array.Clear(_q2);
        Array.Clear(_re); Array.Clear(_im); Array.Clear(_pd); Array.Clear(_sd); Array.Clear(_it);
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
        double price = (bar.High + bar.Low) / 2;
        Shift(_pr); _pr[0] = price;

        if (_barIdx <= 5)
        {
            _trend.Append(price);
            _smooth.Append(double.NaN);
            _dcPeriods.Append(double.NaN);
            Shift(_sp); _sp[0] = 0;
            Shift(_dt); _dt[0] = 0;
            Shift(_i1); _i1[0] = 0;
            Shift(_q1); _q1[0] = 0;
            Shift(_i2); _i2[0] = 0;
            Shift(_q2); _q2[0] = 0;
            Shift(_re); _re[0] = 0;
            Shift(_im); _im[0] = 0;
            Shift(_pd); _pd[0] = 0;
            Shift(_sd); _sd[0] = 0;
            Shift(_it); _it[0] = 0;
            _barIdx++;
            return;
        }

        double adj = 0.075 * _pd[1] + 0.54;          // _pd[1] = previous bar's period

        Shift(_sp);
        _sp[0] = (4 * _pr[0] + 3 * _pr[1] + 2 * _pr[2] + _pr[3]) / 10;

        Shift(_dt);
        _dt[0] = (0.0962 * _sp[0] + 0.5769 * _sp[2] - 0.5769 * _sp[4] - 0.0962 * _sp[6]) * adj;

        Shift(_q1);
        _q1[0] = (0.0962 * _dt[0] + 0.5769 * _dt[2] - 0.5769 * _dt[4] - 0.0962 * _dt[6]) * adj;

        Shift(_i1);
        _i1[0] = _dt[3];

        double jI = (0.0962 * _i1[0] + 0.5769 * _i1[2] - 0.5769 * _i1[4] - 0.0962 * _i1[6]) * adj;
        double jQ = (0.0962 * _q1[0] + 0.5769 * _q1[2] - 0.5769 * _q1[4] - 0.0962 * _q1[6]) * adj;

        Shift(_i2);
        Shift(_q2);
        double i2raw = _i1[0] - jQ;
        double q2raw = _q1[0] + jI;
        _i2[0] = 0.2 * i2raw + 0.8 * _i2[1];
        _q2[0] = 0.2 * q2raw + 0.8 * _q2[1];

        Shift(_re); Shift(_im);
        double reRaw = _i2[0] * _i2[1] + _q2[0] * _q2[1];
        double imRaw = _i2[0] * _q2[1] - _q2[0] * _i2[1];
        _re[0] = 0.2 * reRaw + 0.8 * _re[1];
        _im[0] = 0.2 * imRaw + 0.8 * _im[1];

        Shift(_pd);
        double pd0 = _im[0] != 0 && _re[0] != 0 ? 2 * Math.PI / Math.Atan(_im[0] / _re[0]) : 0;
        if (pd0 > 1.5 * _pd[1]) pd0 = 1.5 * _pd[1];
        if (pd0 < 0.67 * _pd[1]) pd0 = 0.67 * _pd[1];
        if (pd0 < 6) pd0 = 6;
        if (pd0 > 50) pd0 = 50;
        pd0 = 0.2 * pd0 + 0.8 * _pd[1];
        _pd[0] = pd0;

        Shift(_sd);
        _sd[0] = 0.33 * _pd[0] + 0.67 * _sd[1];

        int dcPeriods = (int)(double.IsNaN(_sd[0]) ? 0 : _sd[0] + 0.5);
        // Sum last dcPeriods of _pr (we have 12 bars of history; OK for typical dcPeriods 6..50 if barIdx >= 12;
        // before that we trim scope).
        double sumPr = 0;
        int valid = 0;
        for (int d = 0; d < dcPeriods && d < _pr.Length && d <= _barIdx; d++)
        { sumPr += _pr[d]; valid++; }
        Shift(_it);
        _it[0] = valid > 0 ? sumPr / valid : _pr[0];

        // Trendline = WMA of last 4 it values.
        double trendline = _barIdx >= 11
            ? (4 * _it[0] + 3 * _it[1] + 2 * _it[2] + _it[3]) / 10
            : _pr[0];

        _trend.Append(trendline);
        _smooth.Append(_sp[0]);
        _dcPeriods.Append(dcPeriods > 0 ? dcPeriods : double.NaN);
        _barIdx++;
    }

    private static void Shift(double[] ring)
    {
        for (int i = ring.Length - 1; i > 0; i--) ring[i] = ring[i - 1];
        // ring[0] is set by caller after this.
    }
}
