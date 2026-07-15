using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Average True Range — 1-on-1 port of NinjaTrader's @ATR
/// (Wilder 1978).
///
/// NT formula: bar 0 → Value = High − Low (no prev close to gap).
/// Subsequent bars compute the true range
/// <c>TR = max(|low − prevClose|, max(high − low, |high − prevClose|))</c>
/// then a running mean while CurrentBar+1 &lt; Period, transitioning
/// smoothly into Wilder smoothing once the window fills:
/// <c>Value = ((min(CurrentBar+1, Period) − 1) · prevValue + TR) /
/// min(CurrentBar+1, Period)</c>. No NaN warmup — every bar has a value.
///
/// Defaults: Period=14, plot DarkCyan line width 1, name "ATR".
/// Range: [1, int.MaxValue]. Sub-panel.
/// </summary>
[IndicatorDescription("Average True Range (Wilder). Wilder-smoothed mean of the true range (the larger of bar range or gap from prior close). Pure volatility gauge widely used for stop placement and position sizing.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Atr : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _outputAsBarSourced;
    private double _value;
    private double? _prevClose;
    private int _barIdx;     // 0-based bar counter; NT's CurrentBar equivalent

    public Atr(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("ATR", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(
        DisplayName = "Period",
        Description = "Number of bars used for Wilder's smoothing of true range.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    /// <summary>Per-bar ATR as a chainable bar-sourced series.</summary>
    public ISeries<double> Output =>
        _outputAsBarSourced ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period),
                "ATR period must be at least 1.");
        _value = 0;
        _prevClose = null;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            ConsumeBar(bars[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        ConsumeBar(bars[0]);
    }

    private void ConsumeBar(Bar bar)
    {
        if (_prevClose is null)
        {
            // First bar — no prev close; TR is just the intraday range.
            _value = bar.High - bar.Low;
            _prevClose = bar.Close;
            _barIdx = 1;
            _output.Append(_value);
            return;
        }

        double prev = _prevClose.Value;
        double hl   = bar.High - bar.Low;
        double hPC  = Math.Abs(bar.High - prev);
        double lPC  = Math.Abs(bar.Low  - prev);
        double tr   = Math.Max(hl, Math.Max(hPC, lPC));

        // NT formula combines the warmup running-average and post-warmup
        // Wilder smoothing into a single expression. Once _barIdx >=
        // Period it reduces to ((Period-1) * prev + tr) / Period.
        int n = Math.Min(_barIdx + 1, Period);
        _value = ((n - 1) * _value + tr) / n;
        _prevClose = bar.Close;
        _barIdx++;
        _output.Append(_value);
    }
}
