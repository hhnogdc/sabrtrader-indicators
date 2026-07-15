using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Hurst Exponent — 1-on-1 port of Skender Hurst.
/// Computes the per-bar rescaled-range slope (Hurst exponent) over a fixed lookback window.
/// Pipeline per evaluation bar:
///   1. Take last LookbackPeriods+1 prices (today + N priors) → N return values.
///   2. Split returns into chunks of size N, N/2, N/4, ... (chunkQty doubles each set, chunk size = N/chunkQty,
///      with each chunk ≥ 8 observations, max 32 chunks).
///   3. For each chunk, compute the rescaled range R/S = (max cumulative deviation − min cumdev) / population stddev.
///   4. Hurst = slope of log(rs̄) over log(chunkSize), where rs̄ = average R/S across chunks.
/// Defaults LookbackPeriods=100. NaN until LookbackPeriods bars are available.
/// DodgerBlue, sub-panel, 0.5 reference line.
/// </summary>
[IndicatorDescription("Hurst Exponent via rescaled-range analysis over LookbackPeriods returns. Values > 0.5 indicate persistence (trending), < 0.5 mean-reversion, ≈ 0.5 random walk.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Hurst : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public Hurst(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Hurst", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 1, PlotValueUnit.Ratio, PlotBullishDirection.None, null) });
        AddLine("Random walk", 0.5, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "LookbackPeriods", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int LookbackPeriods { get; set; } = 100;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LookbackPeriods < 2) throw new ArgumentOutOfRangeException(nameof(LookbackPeriods));
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
        if (_barIdx + 1 <= LookbackPeriods) { _output.Append(double.NaN); _barIdx++; return; }
        var values = new double[LookbackPeriods];
        double prev = src[barsAgoOfCurrent + LookbackPeriods];   // value LookbackPeriods bars ago
        for (int k = 0; k < LookbackPeriods; k++)
        {
            int barsAgo = barsAgoOfCurrent + (LookbackPeriods - 1 - k);
            double v = src[barsAgo];
            values[k] = prev != 0 ? v / prev - 1 : 0;
            prev = v;
        }
        _output.Append(CalcHurstWindow(values));
        _barIdx++;
    }

    private static double CalcHurstWindow(double[] values)
    {
        int totalSize = values.Length;
        int setQty = 0;
        int maxChunks = 0;
        for (int chunkQty = 1; chunkQty <= 32; chunkQty *= 2)
        {
            if (totalSize / chunkQty >= 8) { maxChunks = chunkQty; setQty++; } else break;
        }
        if (setQty < 2) return double.NaN;

        var logRs = new double[setQty];
        var logSize = new double[setQty];
        int setNum = 0;

        for (int chunkQty = 1; chunkQty <= maxChunks; chunkQty *= 2)
        {
            int chunkSize = totalSize / chunkQty;
            double sumChunkRs = 0;
            int startIndex = totalSize - chunkSize * chunkQty;
            for (int chunk = 0; chunk < chunkQty; chunk++)
            {
                double sum = 0;
                for (int i = startIndex; i < startIndex + chunkSize; i++) sum += values[i];
                double mean = sum / chunkSize;

                double sumY = 0, sumSq = 0;
                double maxY = values[startIndex] - mean;
                double minY = maxY;
                for (int i = startIndex; i < startIndex + chunkSize; i++)
                {
                    double y = values[i] - mean;
                    sumY += y;
                    if (sumY > maxY) maxY = sumY;
                    if (sumY < minY) minY = sumY;
                    sumSq += y * y;
                }
                double r = maxY - minY;
                double s = Math.Sqrt(sumSq / chunkSize);
                double rs = s != 0 ? r / s : 0;
                sumChunkRs += rs;
                startIndex += chunkSize;
            }
            logSize[setNum] = Math.Log10(chunkSize);
            logRs[setNum]   = Math.Log10(sumChunkRs / chunkQty);
            setNum++;
        }
        // Slope of best-fit line through (logSize, logRs).
        double sumX = 0, sumYL = 0;
        for (int i = 0; i < setQty; i++) { sumX += logSize[i]; sumYL += logRs[i]; }
        double mX = sumX / setQty, mY = sumYL / setQty;
        double num = 0, den = 0;
        for (int i = 0; i < setQty; i++) { double dx = logSize[i] - mX; num += dx * (logRs[i] - mY); den += dx * dx; }
        return den == 0 ? double.NaN : num / den;
    }
}
