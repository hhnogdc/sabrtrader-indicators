using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Correlation.
/// Pearson correlation between Input and a second host-supplied series over Period bars.
/// r = Σ((avg0-x[i])·(avg1-y[i])) / sqrt(Σ(avg0-x[i])² · Σ(avg1-y[i])²).
/// avg0 = SMA(Input, Period); avg1 = SMA(SecondInput, Period).
/// Defaults Period=10. CorrelationSeries is the symbol identifier (display only).
/// The host MUST set SecondInput before OnInit fires.
/// Goldenrod plot, sub-panel. The original also requires that the secondary series be in-session;
/// the pipeline does not yet model multi-series sessions so this implementation skips
/// the session-iterator guard (correlation is computed on every bar where both sides
/// have at least Period samples).
/// </summary>
[IndicatorDescription("Pearson Correlation. Rolling -1..+1 correlation coefficient between the chart instrument and a host-supplied second series over Period bars, used to gauge how tightly two markets move together.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Correlation : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Sma _smaA = null!, _smaB = null!;
    private int _barIdx;

    public Correlation(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Correlation", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -1, 1, PlotValueUnit.Ratio, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 10;

    [IndicatorParameter(DisplayName = "CorrelationSeries",
        Description = "Display-only symbol identifier. Set SecondInput at runtime to supply the data.")]
    public string CorrelationSeries { get; set; } = string.Empty;

    /// <summary>Host-supplied second input series. Must be aligned bar-for-bar with Input and set before OnInit.</summary>
    public ISeries<double> SecondInput { get; set; } = null!;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        // Both SMAs read source series (Input / SecondInput) — safe as children.
        // Runtime auto-dispatch fires them BEFORE this indicator, so we read fresh
        // averages in OnBarUpdate.
        _smaA = new Sma(this, input: Input) { Period = Period };
        if (SecondInput != null) _smaB = new Sma(this, input: SecondInput) { Period = Period };
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        if (SecondInput == null) { for (int i = 0; i < Input.Count; i++) { _output.Append(0); _barIdx++; } return; }
        int n = Math.Min(Input.Count, SecondInput.Count);
        for (int i = 0; i < n; i++) Compute(n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child (Swing/Sma) on the live bar
        if (Input.Count == 0 || SecondInput == null || SecondInput.Count == 0) { _output.Append(0); return; }
        Compute(0);
    }

    private void Compute(int barsAgoOfCurrent)
    {
        if (_barIdx < Period - 1) { _output.Append(0); _barIdx++; return; }
        double avg0 = _smaA.Output[barsAgoOfCurrent];
        double avg1 = _smaB!.Output[barsAgoOfCurrent];

        double num = 0, d1 = 0, d2 = 0;
        for (int i = 0; i < Period; i++)
        {
            double dx = avg0 - Input[barsAgoOfCurrent + i];
            double dy = avg1 - SecondInput![barsAgoOfCurrent + i];
            num += dx * dy;
            d1 += dx * dx;
            d2 += dy * dy;
        }
        double denom = Math.Sqrt(d1) * Math.Sqrt(d2);
        _output.Append(denom == 0 ? 0 : num / denom);
        _barIdx++;
    }
}
