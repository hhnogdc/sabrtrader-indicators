using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Standard Deviation Channels — derived from Skender.Stock.Indicators (Apache-2.0).
///   LinReg over Period bars → slope + intercept + sample stddev of values within window
///   Centerline = LinReg value at current bar (intercept + slope·(Period−1))
///   Upper / Lower = Centerline ± StdDeviations · stddev
/// NaN until Period bars are available.
/// Defaults Period=20, StdDeviations=2. DarkGray center, DodgerBlue bands, overlay.
/// </summary>
[IndicatorDescription("Standard Deviation Channels: linear regression centerline over Period bars with upper/lower bands at StdDeviations times the population stddev of values in the window. Statistical alternative to Bollinger Bands around a sloped axis.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class StdDevChannels : IndicatorBase
{
    private readonly ChunkedSeries<double> _center = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _cW, _uW, _lW;
    private int _barIdx;

    public StdDevChannels(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Center", _center, PlotStyle.Line, PlotColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",        MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period        { get; set; } = 20;

    [IndicatorParameter(DisplayName = "StdDeviations", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.1)]
    public double StdDeviations { get; set; } = 2;

    public ISeries<double> Center => _cW ??= new IndicatorOutputSeries(_center, Bars);
    public ISeries<double> Upper  => _uW ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _lW ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period));
        if (StdDeviations <= 0) throw new ArgumentOutOfRangeException(nameof(StdDeviations));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx + 1 < Period)
        {
            _center.Append(double.NaN);
            _upper.Append(double.NaN);
            _lower.Append(double.NaN);
            _barIdx++;
            return;
        }
        // OLS fit over the window (x = 0..Period-1, oldest..newest).
        OlsFit.Compute(src, barsAgoOfCurrent, Period, out double slope, out double intercept);
        double center = intercept + slope * (Period - 1);

        // Population stddev of the values (matches Skender's stdDevY).
        double sumY = 0;
        for (int k = 0; k < Period; k++) sumY += src[barsAgoOfCurrent + k];
        double avgY = sumY / Period;
        double sumSqY = 0;
        for (int k = 0; k < Period; k++) { double d = src[barsAgoOfCurrent + k] - avgY; sumSqY += d * d; }
        double std = Math.Sqrt(sumSqY / Period);
        double width = StdDeviations * std;

        _center.Append(center);
        _upper.Append(center + width);
        _lower.Append(center - width);
        _barIdx++;
    }
}
