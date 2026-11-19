Indicators
Chart trader panel INTERFACE
Give your indicator its own tab on the chart trader panel, for the controls and readouts that need more room than a toolbar dropdown.

On this page
What it is
When the panel appears
Building the panel
The element kinds
Keeping it live
Threading
You have the full picture
What it is
Implement IChartPanelProvider and your indicator contributes its own tab to the chart trader panel, beside Trade and the AI assistants. It has room for more than a toolbar dropdown: a scanner's controls and its findings, a calibration block, a live readout of whatever your indicator computes. You describe the controls; the host renders them natively, so your plugin references no UI framework.

Use the tab when the user needs to see something while they work, or when the controls belong together as a panel. For a couple of quick toggles, the chart toolbar menu is the lighter surface.

When the panel appears
An indicator instance lives on exactly one chart tab, so the rule is simple: the host calls CreateChartPanel when the indicator is added to the chart and OnChartPanelReleased when it is removed. Two charts each carrying your indicator get two independent panels, each driven by its own instance. CreateChartPanel can be called again after a release, so build a fresh ChartPanel each time; element instances released with the old panel can be reused in the new one. Build the panel and return: do not block in CreateChartPanel, and do not add or remove indicators or strategies from inside it.

Building the panel
LiquidityScanner.cs (chart trader tab)
using System.Collections.Generic;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Panels;

[IndicatorDescription("Tracks large resting orders and shows its findings in a chart trader tab.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class LiquidityScanner : IndicatorBase, IChartPanelProvider
{
    private readonly List<double> _found = new();   // prices of the walls found so far (worker thread)
    private volatile bool _clearRequested;              // set on the UI thread, consumed on the worker
    private ChartPanelLabel? _walls;
    private ChartPanelLabel? _lastPull;

    [IndicatorParameter(DisplayName = "Min size", Group = "Detection", MinValue = 1)]
    public int MinSize { get; set; } = 200;

    [IndicatorParameter(DisplayName = "Track pulls", Group = "Detection")]
    public bool TrackPulls { get; set; } = true;

    public LiquidityScanner(IIndicator? parent = null) : base(parent) { }

    public ChartPanel? CreateChartPanel()
    {
        _walls    = new ChartPanelLabel("Walls", "0");
        _lastPull = new ChartPanelLabel("Last pull", "-");

        return new ChartPanel("Liquidity",
            new ChartPanelGroup("Detection",
                new ChartPanelNumber("Min size", v => MinSize = (int)v,
                    value: MinSize, minimum: 1, maximum: 10_000, step: 10),
                new ChartPanelCheckBox("Track pulls", on => TrackPulls = on, isChecked: TrackPulls)),
            new ChartPanelSeparator(),
            _walls,
            _lastPull,
            new ChartPanelButton("Clear", ClearFindings));
    }

    public void OnChartPanelReleased(ChartPanel panel)
    {
        _walls = null;                            // drop references to the released elements
        _lastPull = null;
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (_clearRequested) { _clearRequested = false; _found.Clear(); }
        // ... detection fills _found ...
        if (_walls is null) return;               // no panel on this chart
        _walls.Text   = _found.Count.ToString();
        _walls.Accent = _found.Count > 0 ? ChartPanelAccent.Warning : ChartPanelAccent.Neutral;
    }

    private void ClearFindings()                  // runs on the UI thread
    {
        _clearRequested = true;                   // the calculation thread owns _found
        if (_walls is not null) { _walls.Text = "0"; _walls.Accent = ChartPanelAccent.Neutral; }
    }
}
ChartPanel takes a title (the tab caption) and the elements in layout order, top to bottom. Each element belongs to one panel at a time. Return null from CreateChartPanel to contribute no tab, for a configuration where the panel would be pointless. OnChartPanelReleased has a default implementation that does nothing; implement it when you keep element references in fields, as above.

The element kinds
Element	What it is
ChartPanelButton	A push button. Its callback runs when the user presses it.
ChartPanelToggle	A two-state button that stays pressed while it is on: Run / Pause, Long only, Arm.
ChartPanelCheckBox	A labelled checkbox, for an option the user ticks rather than a button they press.
ChartPanelChoice	One of a fixed set of options: Both / Long only / Short only, One-shot / Continuous.
ChartPanelNumber	A numeric field. The host clamps and rounds every edit to your declared range before your callback sees it, and the mouse wheel steps it by your step.
ChartPanelText	A single-line text field. You get the raw string and may write a normalised value back.
ChartPanelLabel	A read-only caption and value. This is where a live status belongs.
ChartPanelRow	Lays its children side by side on one line, sharing the width evenly.
ChartPanelGroup	A captioned block of elements, for when a panel grows past a handful of controls.
ChartPanelSeparator	A horizontal rule between two stretches of the panel.
Every element carries IsEnabled (false greys it out and blocks input), IsVisible (false removes it from the layout) and an Accent. The accent (ChartPanelAccent) is a semantic role: Neutral, Positive, Negative, Warning, Info. The host resolves it against the user's theme, so a panel keeps matching the platform when they switch theme.

The interactive elements also expose the methods the host calls to deliver a user action: ChartPanelButton.Click, ChartPanelToggle.Toggle, ChartPanelCheckBox.Check, ChartPanelChoice.Select, ChartPanelNumber.Commit and ChartPanelText.Commit. They apply the edit and run your callback. Your own code sets the properties (IsOn, IsChecked, SelectedIndex, Value, Text) instead.

Keeping it live
The elements are live objects. Write to them whenever you have something new and the tab follows:

updating the panel
_lastPull.Text   = $"{size} @ {price} ({time:HH:mm:ss})";
_lastPull.Accent = pulledBid ? ChartPanelAccent.Negative : ChartPanelAccent.Positive;
_clear.IsEnabled = _found.Count > 0;
Writes update the existing elements in place. The structure of a panel is fixed the moment you construct it, and only the value you changed is re-read, so the user keeps focus, scroll position and a half-typed value when an update lands while they are working.

Updating on every tick is fine. A burst of writes collapses into one repaint showing the newest value, so a status readout costs nothing per tick and a fast market cannot flood the UI.

Writing a value from your own code updates the display only; your callback runs for user edits. That lets you reflect a state you changed yourself, a strategy pausing on a risk stop for example, without re-entering the handler that would have made the change. A written ChartPanelNumber.Value is clamped and rounded exactly like a user edit.

Threading
Element callbacks run on the chart UI thread, the same thread as your toolbar menu commands and your custom rendering, so you can share plain fields between all three without locks or volatile. Your calculation callbacks (OnBarUpdate, OnMarketData) can run on another thread, so hand requests from a UI callback to the calculation side through a volatile field or another thread-safe hand-off, as ClearFindings does above.

Writing to elements works from anywhere, including OnBarUpdate on a background calculation pass. The host marshals and coalesces for you.

A handler that throws is contained: the failure is logged against your indicator and the rest of the chart carries on.

You have the full picture
That completes the indicator chapter. You can build the whole spectrum, from a one-line SMA to a footprint tool with orderflow data, custom rendering, coalesced repaints, a toolbar dropdown, a context menu and its own chart-trader tab, entirely against the public contracts in SabrTrader.Pipeline.Indicators, SabrTrader.Pipeline.Panels, SabrTrader.Pipeline.Plots, SabrTrader.Pipeline.Series and the related namespaces. For a member-level reference, see Reference › Indicators.

A strategy contributes a tab through the same contract, with two differences: it gets one when the user attaches it to a chart, and its callbacks run on the strategy thread instead of the UI thread. See Strategies › Chart trader panel. Next up: strategies, which extend the same indicator lifecycle with order and position-state hooks.

