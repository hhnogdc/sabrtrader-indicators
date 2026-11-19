Indicators
Chart toolbar menu INTERFACE
Give your indicator its own dropdown button on the chart's top toolbar, and items in the right-click context menu, for quick toggles without opening the settings dialog.

On this page
What it is
Implementing the menu
The menu item kinds
Right-click context menu
Threading & lifecycle
One more surface
What it is
Implement IIndicatorToolbarMenu and your indicator gets its own dropdown button on the chart's top toolbar, the natural home for fast toggles such as "show POC" or "show value area". The scope is one indicator instance on one chart tab: two charts that each carry a Volume Profile get two independent buttons, each editing its own instance. The host does the per-tab bookkeeping.

Implementing the menu
Provide MenuTitle (the button text; keep it short) and BuildMenu(), which returns an IReadOnlyList<ToolbarMenuItem>. The host calls BuildMenu when it creates the button and again every time you raise MenuChanged, and keeps the resulting items. Return fresh items on each call; an empty list shows a disabled placeholder.

The host snapshots every field of every item (labels, IsEnabled, values). The exception is the checked state: ToolbarCheckItem.IsChecked, ToolbarRadioOption.IsSelected and ToolbarSubmenu.IsChecked are delegates the host reads again after every toggle or selection in the menu. Raise MenuChanged whenever anything else in the tree goes stale: an item is enabled or disabled, a label changes, a submenu gains items, or state shown in the menu was changed from outside it (the settings dialog). The button is created when the indicator is added to the chart, which can be before it is initialised, so a command you build disabled at that point stays disabled until you raise MenuChanged. Leaving the event unused is correct only when everything the menu snapshots is constant for the indicator's lifetime.

SessionProfile.cs (toolbar menu)
using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Indicators;

[IndicatorDescription("Volume-at-price profile over each session, with POC and value-area overlays.")]
[IndicatorCategory(IndicatorCategory.OrderFlow)]
[IndicatorInput(IndicatorInputKind.None)]
[RequiresOrderFlow]
public sealed class SessionProfile : IndicatorBase, IChartCustomRender, IRepaintNotifier, IIndicatorToolbarMenu
{
    public event Action? RepaintRequested;
    public event Action? MenuChanged;       // labels and items are constant, so it is never raised

    private bool _showPoc = true;
    private bool _showValueArea = true;

    public string MenuTitle => "Session Profile";

    public IReadOnlyList<ToolbarMenuItem> BuildMenu() => new ToolbarMenuItem[]
    {
        new ToolbarCheckItem("Show POC",
            IsChecked: () => _showPoc,
            Toggle:    on => { _showPoc = on; RepaintRequested?.Invoke(); }),

        new ToolbarCheckItem("Show value area",
            IsChecked: () => _showValueArea,
            Toggle:    on => { _showValueArea = on; RepaintRequested?.Invoke(); }),
    };

    public SessionProfile(IIndicator? parent = null) : base(parent) { }
    public void OnCustomRender(IIndicatorRenderContext ctx) { /* paint columns, POC, value area */ }
}
Two details to get right. A menu whose actions change what is drawn implements IRepaintNotifier as well and raises RepaintRequested from the action (as above); a setting that changes the computed values raises IRecalculateNotifier.RecalculateRequested instead (see Repaint vs. recalculate). And ToolbarCheckItem takes a Func<bool> IsChecked that reads the current state and an Action<bool> Toggle that receives the new state.
The menu item kinds
ToolbarMenuItem is an abstract record with a Label. The host renders the concrete records below; pick the one that fits. The same records are used by the context menu.

Record	Renders as
ToolbarCommand	A click-to-invoke action (Invoke, optional IsEnabled; disabled items are greyed out). A menu that consists of exactly one command renders as a plain button that runs it on click.
ToolbarCheckItem	A two-state toggle (IsChecked / Toggle, optional IsEnabled).
ToolbarSubmenu	A nested submenu of Items. The optional IsChecked delegate shows a check on the submenu itself, so the user sees the grouped feature is on without opening it.
ToolbarSeparator	A visual divider.
ToolbarRadioGroup (+ ToolbarRadioOption)	A submenu of mutually exclusive options. Each option has IsSelected and Select, and an optional IsEnabled.
ToolbarColorPicker	A row with a colour swatch that opens a colour picker seeded from GetColor. SetColor fires only when the user commits; cancelling changes nothing.
ToolbarNumberInput	An inline numeric field seeded from GetValue. On commit the value is clamped to [Min, Max], rounded to Decimals places (0 = integer) and passed to SetValue.
ToolbarSlider	A draggable slider with the same data contract as ToolbarNumberInput, for magnitudes such as opacity or scale.
ToolbarTextInput	An inline free-text field seeded from GetText. The committed string goes to SetText unchanged; you parse and validate it.
ToolbarDescription	A non-interactive, greyed-out, word-wrapped help line (Text).
Right-click context menu
Implement IIndicatorContextMenu to contribute items to the chart's right-click menu, aimed at the clicked location. It uses the same ToolbarMenuItem records. The host calls BuildContextMenu every time the user opens the context menu over your indicator's panel, passing an IndicatorContextMenuLocation with BarIndex (absolute, 0 = oldest), Price, Time, and the raw canvas PixelX / PixelY, so you can build items that act on whatever is under the cursor (for example the profile session the click fell on). Return an empty list to contribute nothing at that spot. Because the items are built per open, ContextMenuChanged is rarely needed.

context menu
public event Action? ContextMenuChanged;   // items are rebuilt on every open

public IReadOnlyList<ToolbarMenuItem> BuildContextMenu(IndicatorContextMenuLocation loc)
{
    return new ToolbarMenuItem[]
    {
        new ToolbarCommand($"Anchor profile from {loc.Time:t}",
            Invoke: () => { AnchorAt(loc.BarIndex); RepaintRequested?.Invoke(); }),
    };
}
Threading & lifecycle
BuildMenu, BuildContextMenu and the item delegates run on the UI thread, so read indicator state directly inside them. MenuChanged and ContextMenuChanged may fire from any thread; the host marshals to the UI thread, coalesces, and rebuilds. The toolbar button appears when the indicator is added to the chart and disappears the frame after it is removed.

One more surface
With these pages you can build anything from a one-line SMA to a footprint tool with order-flow data, custom rendering, coalesced repaints, a toolbar dropdown and a context menu, entirely against the public contracts in SabrTrader.Pipeline.Indicators, SabrTrader.Pipeline.Plots, SabrTrader.Pipeline.Series and the related namespaces. For a member-level reference, see Reference › Indicators. The last page of the chapter covers the chart trader panel, the roomier surface for controls and readouts that do not fit in a dropdown.

