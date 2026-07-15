using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Stochastic Percentile — a robust Stochastic that replaces the
/// (Close − Low)/(High − Low) ratio with the PERCENTILE RANK of the
/// current value inside the lookback window. Because it counts ordering
/// rather than measuring distance, a single outlier bar can't dominate
/// the scale the way it does in a classic Stochastic.
///
/// Math:
///   <code>
///   Percentile = 100 · count(window value ≤ current) / windowCount
///   Signal     = SMA(Percentile, SmoothD)
///   </code>
/// The window is the last <c>Period</c> input values (inclusive of the
/// current bar), so a value at the top of its range reads 100 and at
/// the bottom reads (100 / windowCount).
///
/// Defaults Period=14, SmoothD=3. Plots Percentile (Goldenrod) /
/// Signal (DodgerBlue), 20 / 80 reference lines, range 0–100, sub-panel.
/// </summary>
[IndicatorDescription("Stochastic Percentile: a Stochastic built from the percentile rank of price within its lookback window instead of the high-low range, so outliers don't distort the scale. Plots the percentile and an SMA signal with 20/80 bands.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class StochasticPercentile : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _pct = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _pctWrap, _sigWrap;

    private double[] _window = [];
    private int _count, _writeIdx;
    private RollingSum _sigSum = null!;
    private int _sigCount;

    public StochasticPercentile(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var osc = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "20/80");
        AddPlot(new Plot("Percentile", _pct,    PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = osc });
        AddPlot(new Plot("Signal",     _signal, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = osc });
        AddLine("Oversold",   20, NTColors.DarkCyan);
        AddLine("Overbought", 80, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Percentile-rank lookback window.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "SmoothD", Description = "SMA period over the percentile (gives the signal).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothD { get; set; } = 3;

    public ISeries<double> Output => _pctWrap ??= new IndicatorOutputSeries(_pct, Bars);
    public ISeries<double> Signal => _sigWrap ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (SmoothD < 1) throw new ArgumentOutOfRangeException(nameof(SmoothD));
        _window = new double[Period];
        _count = 0;
        _writeIdx = 0;
        _sigSum = new RollingSum(SmoothD);
        _sigCount = 0;
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

    private void Consume(double value)
    {
        if (_count < Period) { _window[_writeIdx] = value; _count++; }
        else                  _window[_writeIdx] = value;
        _writeIdx = (_writeIdx + 1) % Period;

        int atOrBelow = 0;
        for (int i = 0; i < _count; i++)
            if (_window[i] <= value) atOrBelow++;
        double pct = 100.0 * atOrBelow / _count;   // current is always counted → never 0
        _pct.Append(pct);

        _sigSum.Push(pct);
        _sigCount++;
        _signal.Append(_sigSum.Sum / Math.Min(_sigCount, SmoothD));
    }
}
