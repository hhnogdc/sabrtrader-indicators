Indicators
Your first indicator SIMPLE
A complete simple moving average, explained line by line. Most line indicators follow the same template: declare a parameter, register a plot, fill a series in two lifecycle methods.

On this page
What we are building
The complete indicator
Line by line
Run it
What we are building
A simple moving average (SMA) has four parts that reappear in nearly every indicator:

an output series: a ChunkedSeries<double> the indicator appends one value to per bar;
a parameter: the period, tagged with [IndicatorParameter] so the settings dialog shows it;
a plot: the line on the chart, registered once with AddPlot(...);
two lifecycle methods: OnDataLoaded computes the whole history, OnBarUpdate appends each live bar.
The complete indicator
Every type, attribute and method below comes from SabrTrader.Pipeline.Indicators, SabrTrader.Pipeline.Plots and SabrTrader.Pipeline.Series.

MySma.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

[IndicatorDescription("Simple moving average. The unweighted mean of the input " +
    "over the last N bars.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MySma : IndicatorBase
{
    // The indicator owns the writable backing store; the plot reads it.
    private readonly ChunkedSeries<double> _output = new();

    [IndicatorParameter(DisplayName = "Period", Description = "Number of bars to average.",
        Group = "Parameters", Order = 0, MinValue = 1, MaxValue = 1000, Step = 1)]
    public int Period { get; set; } = 14;

    // Single ctor of the canonical shape: parent for child composition (null = standalone),
    // input for an explicit source series. The derived body sets parameters + plots.
    public MySma(IIndicator? parent = null, ISeries<double>? input = null, int period = 14)
        : base(parent, input)
    {
        Period = period;
        AddPlot(new Plot("SMA", _output, PlotStyle.Line, new ChartColor(33, 150, 243), 1.5));
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);                       // defaults Input to Bars(0).Close
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period), "Period must be >= 1.");
    }

    // History: one pass over the whole backfill, oldest bar first.
    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int count = Input.Count;
        for (int i = count - 1; i >= 0; i--)    // i is barsAgo: count-1 = oldest, 0 = newest
            _output.Append(Average(barsAgo: i));
    }

    // Live: one new closed bar; append its value.
    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        _output.Append(Average(barsAgo: 0));
    }

    // Shared maths so history and live can never drift apart.
    private double Average(int barsAgo)
    {
        if (Input.Count - barsAgo < Period) return double.NaN;   // warmup: not enough bars yet
        double sum = 0;
        for (int k = 0; k < Period; k++)
            sum += Input[barsAgo + k];
        return sum / Period;
    }
}
Line by line
The class attributes
[IndicatorDescription] supplies the paragraph the Add Indicator and settings dialogs show; give every indicator one. [IndicatorCategory] places it in a family for grouping and filtering. [IndicatorInput(IndicatorInputKind.Scalar)] declares that the indicator reads the scalar Input stream, which also adds the "Input series" picker to its settings so a user can run the SMA on High, Median, Volume or another indicator's plot.

Class attribute	Effect
[IndicatorDescription("...")]	Description text in the Add Indicator and settings dialogs.
[IndicatorDisplayName("...")]	Name shown in the indicator list. Without it the type name is used, with a trailing Indicator removed and PascalCase split into words (OpeningRangeIndicator becomes "Opening Range").
[IndicatorFolder("...")]	Lists the indicator under this folder in the indicator pickers instead of under its category folder. Indicators that name the same folder share it.
[IndicatorCategory(...)]	One of Trend, Momentum, Volume, Volatility, OrderFlow, Structure, Custom. Unspecified when omitted.
[IndicatorInput(...)]	Scalar: reads Input and gets the "Input series" picker. Bars: reads OHLCV through Bars, no picker. None: consumes neither (orderflow stores, render-only studies), no picker.
[SingleInstancePerChart]	At most one instance per chart (see Panel placement).
The output series
ChunkedSeries<double> is the SDK's writable series: one writer, any number of readers. It grows without bound, appends are O(1) amortised, and double.NaN round-trips unchanged so warmup bars render as gaps. You hand it to a plot; the chart reads it from the render thread while your indicator writes to it.

The parameter
[IndicatorParameter] on a public read/write property renders an editor row in the settings dialog, persists the value in the saved chart template, and makes the value part of the indicator's content key, the identity that makes a 14-period and a 50-period SMA distinct instances. Give every parameter a real default (here 14): the host constructs the indicator with defaults, then applies the saved value. Parameters covers this in full.

The constructor and the plot
An IndicatorBase subclass exposes one constructor of the shape (IIndicator? parent = null, ISeries<double>? input = null, /* your params */) and passes parent and input to base(...). When parent is supplied, the base constructor registers the indicator as its child (see Child indicators). Every parameter needs a default value, because the host creates the indicator through this constructor with all defaults. In the body you set your parameters and call AddPlot. The Plot constructor takes (name, values, style, color, lineWidthPx); ChartColor is plain RGBA (new ChartColor(33, 150, 243) is a blue).

Add plots in the constructor. The Plots list is append-only and reference-stable for the indicator's lifetime: the chart caches it and the settings dialog binds to it. OnInit runs again on every recalculation, so a plot added there would be added again each time. You may change a plot's colour, style and visibility at any time.
OnInit
OnInit runs before any data callback, after the saved parameter values have been applied, and again after each recalculation. It is the place to validate and to size buffers. Call base.OnInit(ctx) first. The base implementation defaults Input to Bars(0).Close (and Bars to Bars(0)) when you did not pass an explicit source, and applies the user's "Input series" choice. Read Input only after that call.

OnDataLoaded: the history pass
When OnDataLoaded fires, the entire backfill is already in the series. You compute every historical value in one loop. Indexing is bars-ago: Input[0] is the most recent bar and Input[Input.Count - 1] the oldest, so to append in chronological order you walk barsAgo from Count - 1 down to 0.

OnBarUpdate: the live pass
OnBarUpdate fires once per closed live bar (the default CalculationMode.OnBarClose cadence). The bar that just closed is at barsAgo: 0; you append exactly one value. Both passes call the same Average helper, which keeps history and live output identical.

Run it
Compile the indicator into a plugin assembly and place it in your SabrTrader plugins folder. The host picks up the concrete IIndicator types in the assembly that it can construct with default arguments, and the indicator appears in the chart's Add Indicator dialog with its description and an editable Period field. See Building & deploying for discovery and deployment details.

