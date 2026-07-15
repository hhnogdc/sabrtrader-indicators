using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// OsMA (Oscillator of Moving Average) — the MACD histogram promoted
/// to a stand-alone study: <c>MACD line − Signal line</c>. Rising OsMA
/// means the MACD is pulling away from its signal (accelerating
/// momentum); a fall through zero marks the MACD/signal crossover.
///
/// Composed from the existing <see cref="Macd"/> built-in (republishes
/// its Diff plot) — no math duplication.
///
/// Defaults Fast=12, Slow=26, Smooth=9. Single histogram plot
/// (DarkGray), zero centerline, sub-panel.
/// </summary>
[IndicatorDescription("OsMA (MACD histogram): the MACD line minus its signal line as a stand-alone histogram. Zero crossings mark MACD/signal crossovers; the bar height shows momentum acceleration.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Osma : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Macd _macd = null!;

    public Osma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("OsMA", _output, PlotStyle.Histogram, NTColors.DarkGray, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast", Description = "Fast EMA period of the underlying MACD.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow", Description = "Slow EMA period of the underlying MACD.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Smooth", Description = "Signal EMA period of the underlying MACD.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 9;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Fast), "All periods must be at least 1.");
        _macd = new Macd(this, input: Input) { Fast = Fast, Slow = Slow, Smooth = Smooth };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _macd.Diff.Count;
        for (int i = 0; i < n; i++) _output.Append(_macd.Diff[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_macd.Diff.Count == 0) return;
        _output.Append(_macd.Diff[0]);
    }
}
