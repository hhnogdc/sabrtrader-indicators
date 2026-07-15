using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// SMA with extra error metrics — 1-on-1 port of Skender SmaAnalysis.
///   Sma  = SMA(value, Period)
///   Mad  = Σ |value − sma| / Period             (mean absolute deviation)
///   Mse  = Σ (value − sma)² / Period            (mean squared error)
///   Mape = Σ |value − sma| / value / Period     (mean absolute percent error)
/// NaN before bar Period − 1. Defaults Period=14.
/// Goldenrod SMA line; analysis series exposed as plots (hidden by default).
/// </summary>
[IndicatorDescription("SMA with diagnostic series: standard SMA plus Mean Absolute Deviation, Mean Squared Error and Mean Absolute Percent Error around the average, exposing how well the SMA fits the data.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class SmaAnalysis : IndicatorBase
{
    private readonly ChunkedSeries<double> _sma  = new();
    private readonly ChunkedSeries<double> _mad  = new();
    private readonly ChunkedSeries<double> _mse  = new();
    private readonly ChunkedSeries<double> _mape = new();
    private IndicatorOutputSeries? _smaW, _madW, _mseW, _mapeW;

    private double[] _window = null!;
    private int _head, _count;
    private double _sum;
    private int _barIdx;

    public SmaAnalysis(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("SMA",  _sma,  PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("MAD",  _mad,  PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("MSE",  _mse,  PlotStyle.Line, NTColors.Crimson,    1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
        AddPlot(new Plot("MAPE", _mape, PlotStyle.Line, NTColors.SlateBlue,  1.0) { Visible = false, Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _smaW  ??= new IndicatorOutputSeries(_sma,  Bars);
    public ISeries<double> Mad    => _madW  ??= new IndicatorOutputSeries(_mad,  Bars);
    public ISeries<double> Mse    => _mseW  ??= new IndicatorOutputSeries(_mse,  Bars);
    public ISeries<double> Mape   => _mapeW ??= new IndicatorOutputSeries(_mape, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _window = new double[Period];
        _head = _count = 0;
        _sum = 0;
        _barIdx = 0;
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

    private void Consume(double v)
    {
        // Push into ring, maintain running sum.
        if (_count < Period) { _window[_head] = v; _sum += v; _count++; }
        else { _sum += v - _window[_head]; _window[_head] = v; }
        _head = (_head + 1) % Period;

        if (_count < Period)
        {
            _sma.Append(double.NaN); _mad.Append(double.NaN); _mse.Append(double.NaN); _mape.Append(double.NaN);
            _barIdx++;
            return;
        }
        double sma = _sum / Period;
        double sumMad = 0, sumMse = 0, sumMape = 0;
        for (int k = 0; k < Period; k++)
        {
            double x = _window[k];
            double d = x - sma;
            sumMad += Math.Abs(d);
            sumMse += d * d;
            sumMape += x == 0 ? double.NaN : Math.Abs(d) / x;
        }
        _sma.Append(sma);
        _mad.Append(sumMad / Period);
        _mse.Append(sumMse / Period);
        _mape.Append(sumMape / Period);
        _barIdx++;
    }
}
