Indicators
Panel placement
Decide whether your indicator draws on top of the price candles or in its own subpanel below. It is a one-property hint the chart reads when the indicator is first added.

On this page
Why placement matters
The default: price overlay
Opening a subpanel
One instance per chart
Custom-render auto-scale
Why placement matters
Some indicators are in price units: a moving average, Bollinger Bands, VWAP, a pivot level. They belong on the price candles, sharing the price axis. Others use a different scale: RSI runs from 0 to 100, MACD oscillates around zero, OBV climbs into the millions. On the price axis they would be invisible or would squash the candles, so they need their own panel with its own vertical scale.

The default: price overlay
If you do nothing, your plots overlay the price panel (PanelPlacement.PriceOverlay). That is the right choice for most indicators, which is why placement is an optional interface and not a member of IIndicator.

Opening a subpanel
To request a fresh subpanel below the price panel, implement IIndicatorPanelHint and return PanelPlacement.NewSubpanel from DefaultPanel.

SimpleRsi.cs (placement)
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorDescription("Relative Strength Index: a bounded 0 to 100 momentum oscillator.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class SimpleRsi : IndicatorBase, IIndicatorPanelHint
{
    private readonly ChunkedSeries<double> _rsi = new();

    [IndicatorParameter(DisplayName = "Period", Group = "Parameters", MinValue = 2)]
    public int Period { get; set; } = 14;

    // RSI is on a 0 to 100 scale, so it gets its own panel.
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    public SimpleRsi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("RSI", _rsi, PlotStyle.Line, new ChartColor(156, 39, 176), 1.5));
    }

    // ... OnDataLoaded / OnBarUpdate compute the oscillator into _rsi ...
}
Placement	Typical indicators
PriceOverlay (default; no interface needed)	SMA, EMA, Bollinger Bands, VWAP, pivots: anything in price units.
NewSubpanel (implement IIndicatorPanelHint)	RSI, Stochastic, MACD, OBV: oscillators and cumulative scales.
The hint sets the initial placement only. It picks the panel when the indicator is first added; the user can move it to another panel afterwards. Threshold reference lines (RSI 30/70) pair naturally with a subpanel; see Threshold lines.
One instance per chart
A tool indicator that owns a chart-wide surface (a designer or builder host, for example) can mark itself [SingleInstancePerChart]. A chart that already carries an instance of the tagged type ignores any further add of that type. The limit is per concrete type, so two different single-instance tools can coexist, and a tagged base class caps each derived type separately. Leave ordinary indicators untagged so users can add as many as they like with different settings. The flag is exposed as IndicatorMetadata.SingleInstancePerChart.

Custom-render auto-scale
A subpanel's auto-fit walks the visible plot values, one per bar. When your subpanel paints its own geometry through IChartCustomRender, from data finer than the bar grid or on the forming bar that has no plot value yet, implement ISubpanelAutoScaleSource so the Y axis fits what you actually draw:

public interface ISubpanelAutoScaleSource
{
    bool TryGetAutoScaleRange(TimeSpan visibleSpan, out double min, out double max);
}
Return the range you would paint across the visible window, or false when there is nothing to draw. visibleSpan is the wall-clock duration on screen, from the oldest visible bar's start to the newest visible bar's end; it is a duration so you can compare it against your own UTC data without mixing clocks. TimeSpan.Zero means the span is unknown: fit over everything. The chart unions your range with the plot-derived range and applies its usual padding.

