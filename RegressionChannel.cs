using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;
using SabrTrader.Pipeline.Indicators;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Regression Channel — 1-on-1 port of NT @RegressionChannel.
/// OLS linear regression of last Period bars; channels are middle ± Width·stdDev,
/// where stdDev is the population stddev of the *absolute residuals* (NT formula).
/// Middle[i] = intercept + slope·(Period-1).
/// Bar 0 → all three plots = Input[0]. When stdDev is 0 or infinite → bands collapse to Input[0].
/// Defaults Period=35, Width=2. Plots Middle (DarkGray), Upper / Lower (DodgerBlue), overlay.
/// </summary>
[IndicatorDescription("Linear Regression Channel: OLS-fit middle line over the last Period bars, with parallel upper/lower bands offset by Width times the population stddev of absolute residuals.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class RegressionChannel : IndicatorBase, IChartCustomRender
{
    private readonly ChunkedSeries<double> _middle = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private readonly ChunkedSeries<double> _slopeSeries = new();
    private readonly ChunkedSeries<double> _interceptSeries = new();
    private readonly ChunkedSeries<double> _stdDevSeries = new();
    // Plot-backing series are DELIBERATELY never appended to. The three plots
    // exist only to carry the channel's per-line colour/width into the legend
    // (and OnCustomRender reads Plots[i].Color from them); the channel itself is
    // drawn as three straight lines in OnCustomRender, exactly like NT. Backing
    // the plots with the per-bar _middle/_upper/_lower series — as the first port
    // did — made the chart ALSO draw wavy per-bar polylines (a Bollinger-band
    // look) on top of the straight channel. Empty series ⇒ PlotRenderPass draws
    // nothing per bar, so only the custom-rendered channel shows. The per-bar
    // values still live on _middle/_upper/_lower for the public ISeries below.
    private readonly ChunkedSeries<double> _midPlot = new();
    private readonly ChunkedSeries<double> _upPlot  = new();
    private readonly ChunkedSeries<double> _loPlot  = new();
    private IndicatorOutputSeries? _midW, _upW, _loW;
    private int _barIdx;

    public RegressionChannel(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Middle", _midPlot, PlotStyle.Line, NTColors.DarkGray,   1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper",  _upPlot,  PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _loPlot,  PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 35;

    [IndicatorParameter(DisplayName = "Width", MinValue = 1, MaxValue = double.MaxValue, Step = 0.1)]
    public double Width { get; set; } = 2;

    public ISeries<double> Middle => _midW ??= new IndicatorOutputSeries(_middle, Bars);
    public ISeries<double> Upper  => _upW  ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _loW  ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 2) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Width < 1) throw new ArgumentOutOfRangeException(nameof(Width));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    /// <summary>
    /// Recalc reset (driven by <c>ChartIndicatorRegistry.Recalculate</c> — e.g. the
    /// Indicators dialog's Apply). These per-bar compute series back
    /// <see cref="OnCustomRender"/> but are NOT registered plots, so the base reset
    /// (which only clears plot output series) leaves them populated. Without clearing
    /// them here, the subsequent <see cref="OnDataLoaded"/> re-append DOUBLES every
    /// series — <c>_slopeSeries.Count</c> becomes 2×bars, and the render's
    /// absolute-bar-index math (<c>totalBars = _slopeSeries.Count</c>) then points the
    /// channel off the right edge of the chart, so it vanishes. Clear them so a recalc
    /// recomputes from a clean slate, identical to a fresh add.
    /// </summary>
    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _middle.Clear(); _upper.Clear(); _lower.Clear();
        _slopeSeries.Clear(); _interceptSeries.Clear(); _stdDevSeries.Clear();
        _barIdx = 0;
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        double in0 = src[barsAgoOfCurrent];
        if (_barIdx == 0)
        {
            _middle.Append(in0); _upper.Append(in0); _lower.Append(in0);
            _slopeSeries.Append(0); _interceptSeries.Append(in0); _stdDevSeries.Append(0);
            _barIdx++; return;
        }

        double sumX = (double)Period * (Period - 1) * 0.5;
        double divisor = sumX * sumX - (double)Period * Period * (Period - 1) * (2 * Period - 1) / 6.0;
        int barCount = Math.Min(Period, _barIdx + 1);
        double sumXy = 0, sumY = 0;
        for (int count = 0; count < barCount; count++)
        {
            double v = src[barsAgoOfCurrent + count];
            sumXy += count * v;
            sumY  += v;
        }
        if (divisor == 0)
        {
            _middle.Append(in0); _upper.Append(in0); _lower.Append(in0);
            _slopeSeries.Append(0); _interceptSeries.Append(in0); _stdDevSeries.Append(0);
            _barIdx++; return;
        }

        double slope = (Period * sumXy - sumX * sumY) / divisor;
        double intercept = (sumY - slope * sumX) / Period;

        double sumResiduals = 0;
        for (int count = 0; count < barCount; count++)
        {
            double regV = intercept + slope * (Period - 1 - count);
            sumResiduals += Math.Abs(src[barsAgoOfCurrent + count] - regV);
        }
        double avg = sumResiduals / Math.Min(Math.Max(_barIdx, 1), Period); // NT: Math.Min(CurrentBar-1, Period); barIdx maps to CurrentBar+1 → CurrentBar-1 = barIdx-1, but NT uses /(barCount-something); pin to NT exactly: divide by Min(CurrentBar-1, Period). For barIdx==1, CurrentBar==0 in NT terms, so divisor would be 0 — guard with Max(...,1).

        sumResiduals = 0;
        for (int count = 0; count < barCount; count++)
        {
            double regV = intercept + slope * (Period - 1 - count);
            double residual = Math.Abs(src[barsAgoOfCurrent + count] - regV);
            sumResiduals += (residual - avg) * (residual - avg);
        }
        double stdDev = Math.Sqrt(sumResiduals / Math.Min(_barIdx + 1, Period));

        double middle = intercept + slope * (Period - 1);
        _middle.Append(middle);
        if (stdDev == 0 || double.IsInfinity(stdDev))
        { _upper.Append(in0); _lower.Append(in0); }
        else
        { _upper.Append(middle + stdDev * Width); _lower.Append(middle - stdDev * Width); }
        _slopeSeries.Append(slope);
        _interceptSeries.Append(intercept);
        _stdDevSeries.Append(stdDev);
        _barIdx++;
    }

    /// <summary>
    /// Custom render — NT draws ONE straight line for Middle/Upper/Lower spanning
    /// the regression window (last Period bars), not a per-bar plot. We replicate that.
    /// </summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (_slopeSeries.Count == 0) return;
        var renderer = ctx.Renderer;
        var vp = ctx.Viewport;

        // Latest regression coefficients (read from barsAgo=0).
        double slope = _slopeSeries[0];
        double intercept = _interceptSeries[0];
        double stdDev = _stdDevSeries[0];

        // Span: the FIXED last-Period bars the latest regression was fit over —
        // anchored to absolute bar indices, NOT the viewport edge. Mapping those
        // fixed indices through vp.BarIndexToPixelX each frame makes the channel
        // scroll WITH its bars (and slide off-screen with them) when the chart is
        // panned. The first port anchored currentAbs to vp.LastVisibleBarIdx,
        // which glued the channel to the right edge of the view so it appeared to
        // stay still while the bars moved underneath it.
        int totalBars = _slopeSeries.Count;
        int currentAbs = totalBars - 1;
        int startAbs = Math.Max(0, currentAbs - (Period - 1));

        // y at the start of the window: intercept (i.e., y at count=0 of the regression which corresponds to current bar).
        // NT: middle line goes from (xStart, intercept) at oldest bar to (xEnd, intercept + slope*(Period-1)) at newest.
        double xStart = vp.BarIndexToPixelX(startAbs);
        double xEnd = vp.BarIndexToPixelX(currentAbs);
        double yStartMid = vp.PriceToPixelY(intercept);
        double yEndMid   = vp.PriceToPixelY(intercept + slope * (Period - 1));

        renderer.DrawLine(xStart, yStartMid, xEnd, yEndMid, Plots[0].Color, Plots[0].LineWidthPx); // Middle

        if (stdDev != 0 && !double.IsInfinity(stdDev))
        {
            double off = stdDev * Width;
            double yStartUp = vp.PriceToPixelY(intercept + off);
            double yEndUp   = vp.PriceToPixelY(intercept + slope * (Period - 1) + off);
            double yStartLo = vp.PriceToPixelY(intercept - off);
            double yEndLo   = vp.PriceToPixelY(intercept + slope * (Period - 1) - off);
            renderer.DrawLine(xStart, yStartUp, xEnd, yEndUp, Plots[1].Color, Plots[1].LineWidthPx); // Upper
            renderer.DrawLine(xStart, yStartLo, xEnd, yEndLo, Plots[2].Color, Plots[2].LineWidthPx); // Lower
        }
    }
}
