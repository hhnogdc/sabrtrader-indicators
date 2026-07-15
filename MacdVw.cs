using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volume-Weighted MACD (MacdVW) — the MACD computed from
/// Volume-Weighted Moving Averages instead of EMAs, so high-volume
/// bars dominate the fast/slow legs. Tends to ignore thin, low-conviction
/// drifts that move a plain MACD.
///
/// Math:
///   <code>
///   fast = VWMA(Close, Fast)    slow = VWMA(Close, Slow)
///   MACD = fast − slow
///   Avg  = EMA(MACD, Smooth)            // signal line
///   Diff = MACD − Avg                   // histogram
///   </code>
/// VWMA(n) = Σ(Close·Vol) / Σ(Vol) over the trailing n bars (falls back
/// to the price mean when Σvol ≈ 0).
///
/// Defaults Fast=12, Slow=26, Smooth=9. Plots MACD (DarkCyan) /
/// Avg (Crimson) / Diff histogram (DarkGray), zero centerline, sub-panel.
/// </summary>
[IndicatorDescription("Volume-Weighted MACD: MACD built from volume-weighted moving averages so heavy-volume bars dominate the fast/slow legs, plus an EMA signal and histogram. Filters out thin, low-conviction moves a plain MACD reacts to.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MacdVw : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _macd = new();
    private readonly ChunkedSeries<double> _signal = new();
    private readonly ChunkedSeries<double> _hist = new();
    private IndicatorOutputSeries? _macdWrap, _signalWrap, _histWrap;

    private RollingSum _fastPv = null!, _fastV = null!, _slowPv = null!, _slowV = null!;
    private double _aSig, _signalEma;
    private bool _hasValue;

    public MacdVw(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("MACD", _macd,   PlotStyle.Line,      NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Avg",  _signal, PlotStyle.Line,      NTColors.Crimson,  1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Diff", _hist,   PlotStyle.Histogram, NTColors.DarkGray, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast", Description = "Fast VWMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow", Description = "Slow VWMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    [IndicatorParameter(DisplayName = "Smooth", Description = "Signal EMA period over the MACD line.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 9;

    public ISeries<double> Output => _macdWrap ??= new IndicatorOutputSeries(_macd, Bars);
    public ISeries<double> Avg    => _signalWrap ??= new IndicatorOutputSeries(_signal, Bars);
    public ISeries<double> Diff   => _histWrap ??= new IndicatorOutputSeries(_hist, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Fast), "All periods must be at least 1.");
        _fastPv = new RollingSum(Fast); _fastV = new RollingSum(Fast);
        _slowPv = new RollingSum(Slow); _slowV = new RollingSum(Slow);
        _aSig = 2.0 / (1 + Smooth);
        _signalEma = 0;
        _hasValue = false;
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
        double price = bar.Close;
        double vol = bar.Volume;
        _fastPv.Push(price * vol); _fastV.Push(vol);
        _slowPv.Push(price * vol); _slowV.Push(vol);

        // Zero-volume window → VWMA undefined; fall back to the current price so the
        // MACD legs stay finite (matches the @VWMA div-by-zero guard). Both legs then
        // equal price and MACD is 0 for that bar.
        double fast = _fastV.Sum <= double.Epsilon ? price : _fastPv.Sum / _fastV.Sum;
        double slow = _slowV.Sum <= double.Epsilon ? price : _slowPv.Sum / _slowV.Sum;
        double macd = fast - slow;

        if (!_hasValue) { _signalEma = macd; _hasValue = true; }
        else             _signalEma = _aSig * macd + (1 - _aSig) * _signalEma;

        _macd.Append(macd);
        _signal.Append(_signalEma);
        _hist.Append(macd - _signalEma);
    }
}
