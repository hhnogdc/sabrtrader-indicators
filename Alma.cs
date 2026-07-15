using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Arnaud Legoux Moving Average (ALMA) — 1-on-1 port of Skender Alma.
/// Gaussian-weighted MA over the last Period bars:
///   m = Offset · (Period − 1)
///   s = Period / Sigma
///   w[i] = exp(−(i − m)² / (2s²))
///   ALMA = Σw[i]·value[i] / Σw[i]
/// First (Period − 1) bars emit NaN per Skender.
/// Defaults Period=9, Offset=0.85, Sigma=6. DodgerBlue, overlay.
/// Weights are window-constant and pre-computed once in OnInit;
/// per-bar cost is O(Period) (vectorisable if needed later).
/// </summary>
[IndicatorDescription("Arnaud Legoux Moving Average. Gaussian-weighted moving average with adjustable Offset (0..1 shifts weight toward newest data) and Sigma (curve width), trading less lag for less smoothness than a classic MA.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Alma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double[] _weights = [];
    private double _normSum;
    private int _barIdx;

    public Alma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ALMA", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 9;

    [IndicatorParameter(DisplayName = "Offset", Description = "Gaussian center offset in [0, 1].",
        MinValue = 0, MaxValue = 1, Step = 0.05)]
    public double Offset { get; set; } = 0.85;

    [IndicatorParameter(DisplayName = "Sigma", Description = "Gaussian width (> 0).",
        MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.1)]
    public double Sigma { get; set; } = 6;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Offset < 0 || Offset > 1) throw new ArgumentOutOfRangeException(nameof(Offset));
        if (Sigma <= 0) throw new ArgumentOutOfRangeException(nameof(Sigma));

        double m = Offset * (Period - 1);
        double s = Period / Sigma;
        double twoS2 = 2 * s * s;
        _weights = new double[Period];
        _normSum = 0;
        for (int i = 0; i < Period; i++)
        {
            double w = Math.Exp(-((i - m) * (i - m)) / twoS2);
            _weights[i] = w;
            _normSum += w;
        }
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        for (int i = 0; i < n; i++) Compute(src, n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx < Period - 1) { _output.Append(double.NaN); _barIdx++; return; }
        // weight[k] is the weight for the value k bars from the start of the window
        // (k=0 = oldest, k=Period-1 = newest). barsAgo for oldest = barsAgoOfCurrent + Period - 1.
        double sum = 0;
        for (int k = 0; k < Period; k++)
        {
            int barsAgo = barsAgoOfCurrent + (Period - 1 - k);
            sum += _weights[k] * src[barsAgo];
        }
        _output.Append(sum / _normSum);
        _barIdx++;
    }
}
