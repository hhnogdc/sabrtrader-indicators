Drawing tools
Overview & anchors
Interactive chart annotations (lines, shapes, Fibonacci level sets, patterns), built on a small base class that owns selection, movement and styling, and on bar-index-independent anchors that survive scrolling, live updates and replays.

On this page
What a drawing tool is
The drawing-tool model
Anchors
Placement modes
Panels
Visual order
Per-timeframe visibility
Which coordinates your tool uses
Related pages
What a drawing tool is
A drawing tool is one placed annotation on a chart: a trend line, a ray, a horizontal line, a rectangle, a Fibonacci retracement, an Elliott-wave count. Each placed instance is a live object that stores its own anchors and style, paints itself every frame, and answers "did the user click me?". The contract is IDrawingTool in the SabrTrader.Pipeline.Drawing namespace; the chart's drawing layer owns the list of tools and drives their lifecycle.

Why it exists. The host gives you the projection maths (time/price↔pixel), the placement state machine, selection, the properties dialog, persistence and toolbar discovery. You supply only the geometry of your shape. That separation is what lets a two-anchor trend line be roughly twenty lines of code.

When to build one. Reach for a drawing tool whenever you want a user-placed, user-draggable annotation tied to points on the price timeline, as opposed to an indicator, which computes a series from bar data. If the thing follows the cursor during placement and survives a workspace save, it is a drawing tool.

The drawing-tool model
You subclass DrawingToolBase and tag it with [DrawingToolMetadata]. The base class owns the anchor list, the process-unique Id, selection state (IsSelected), the placed flag (IsPlaced), the standard Color / LineStyle / LineWidthPx properties, the Scope, IsLocked and Visibility settings, the stacking position (ZOrder), and the default Move. It reads ToolName from the metadata attribute. That leaves your subclass with exactly two abstract methods to implement: OnRender (paint the tool) and HitTest (is this pixel close enough to count as a hit?).

IDrawingTool.cs
using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Plots;

namespace SabrTrader.Pipeline.Drawing;

public interface IDrawingTool
{
    Guid Id { get; }
    string ToolName { get; }
    ChartPanelKey PanelId { get; set; }
    bool IsSelected { get; set; }
    bool IsPlaced { get; set; }
    bool IsLocked { get; set; }

    int AnchorCount { get; }
    int MaxAnchorCount { get; }
    DrawingPlacementMode PlacementMode { get; }
    bool CanAddAnchor { get; }
    IReadOnlyList<DrawingAnchor> Anchors { get; }
    DrawingAnchorCoordinates AnchorCoordinates => DrawingAnchorCoordinates.TimeAndPrice;
    bool UsesCornerAndSideHandles => false;

    ChartColor Color { get; set; }
    double LineWidthPx { get; set; }
    LineStyle LineStyle { get; set; }
    DrawingScope Scope { get; set; }
    DrawingVisibility Visibility => DrawingVisibility.Everywhere;
    int ZOrder { get; set; }

    void SetAnchor(int index, DrawingAnchor anchor);
    void AddAnchor(DrawingAnchor anchor);
    bool RemoveLastAnchor();
    void Move(TimeSpan dTime, double dPrice);

    void OnRender(IDrawingToolRenderContext ctx);
    bool HitTest(double pixelX, double pixelY, IDrawingToolRenderContext ctx, double tolerancePx = 6);
}
DrawingToolBase has two construction modes. The fixed-anchor constructor takes an anchor count and pre-allocates that many slots, so SetAnchor works on every index from the start; its placement mode is FixedAnchors. The variable-anchor constructor takes a placement mode (and optionally a max count and an initial count) for open-ended or paint tools.

DrawingToolBase.cs
public abstract class DrawingToolBase : IDrawingTool
{
    // Fixed-anchor tools (line, ray, rectangle, triangle, arrow):
    protected DrawingToolBase(int anchorCount);

    // Variable-anchor tools (path, polyline, brush, highlighter):
    protected DrawingToolBase(
        DrawingPlacementMode mode,
        int maxAnchorCount = int.MaxValue,
        int initialAnchorCount = 0);

    public IReadOnlyList<DrawingAnchor> Anchors { get; }
    public ChartColor Color { get; set; }          // already an editable property
    public LineStyle LineStyle { get; set; }        // Solid / Dashed / Dotted / DashDot / DashDotDot
    public double LineWidthPx { get; set; }         // throws if <= 0
    public DrawingScope Scope { get; set; }         // ThisChart / AllCharts (same symbol)
    public bool IsLocked { get; set; }              // not selectable, movable or deletable while true
    public DrawingVisibility Visibility { get; set; } // which timeframes it shows on; never null
    public int ZOrder { get; set; }                 // position in the visual stack
    public virtual bool UsesCornerAndSideHandles { get; }   // false; true for two-anchor boxes
    public int MinAnchorsForFinalize { get; protected set; } = 2;

    public abstract void OnRender(IDrawingToolRenderContext ctx);
    public abstract bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6);
    public virtual void Move(TimeSpan dTime, double dPrice);   // default: shift all anchors
}
Defaults that already work. The base sets Color to DodgerBlue (new ChartColor(0x1E, 0x90, 0xFF)), LineStyle to Solid and LineWidthPx to 1.0, Scope to ThisChart and Visibility to DrawingVisibility.Everywhere. All of these, plus IsLocked, carry [DrawingToolProperty], so every tool gets their editors in the properties dialog and has them saved with the drawing, with no extra work.
Anchors
An anchor is a (Time, Price) pair on the bar series' canonical timeline. It is a readonly record struct and is deliberately bar-index independent: a tool stores when and at what price it lives, never which bar number. That is what lets a tool survive scrolling, live bar updates, replays and even bar-type changes (range / Renko / tick): time and price do not move when the bar grid does.

Two factory helpers cover the degenerate cases: a horizontal line cares only about price, a vertical line only about time. The convention is to store the unused coordinate as a sentinel (DateTime.MinValue or double.NaN) and never read it.

DrawingAnchor.cs
namespace SabrTrader.Pipeline.Drawing;

public readonly record struct DrawingAnchor(DateTime Time, double Price)
{
    public static DrawingAnchor PriceOnly(double price) => new(DateTime.MinValue, price);
    public static DrawingAnchor TimeOnly(DateTime time) => new(time, double.NaN);
}
The tool's Anchors list always returns AnchorCount entries. For fixed-count tools the slots are pre-allocated (zero-valued until set) and you write them with SetAnchor(index, anchor). For variable-count tools the list starts empty (or at the initial count) and grows via AddAnchor; RemoveLastAnchor pops the most recent point.

Placement modes
How the host gathers anchors during placement is driven by DrawingPlacementMode, which your constructor choice sets. The chart's interaction state machine dispatches on it.

DrawingPlacementMode	Placement gesture	Used by
FixedAnchors	Click once per anchor; placement completes when AnchorCount clicks land.	Line, ray, rectangle, triangle, arrow.
OpenEnded	Click to append a point; double-click or Escape finalizes (must reach MinAnchorsForFinalize).	Path, polyline.
DragToPaint	Mouse-down + drag samples points while held; mouse-up finalizes.	Brush, highlighter.
The fixed-anchor constructor always selects FixedAnchors; passing that mode to the variable-anchor constructor throws, because fixed tools must pre-allocate their slots. Variable tools default MinAnchorsForFinalize to 2, because a single point is not a meaningful shape.

Panels
Every tool carries a PanelId, a ChartPanelKey naming a data series and a panel within it. ChartPanelKey.Main (equal to default) is the primary series' main candle panel and is where a freshly constructed tool starts; any other key names a subpanel (typically a panel-hint indicator's panel id) of series SeriesIndex. The chart's render and hit-test passes filter tools by this so each panel keeps its own drawings in its own price domain. The host sets the key when the user places the tool, so a tool rarely touches it.

Visual order
The chart's visual stack is one number line, and the price series sits on it like everything else. A drawing whose ZOrder is below DrawingZOrder.PriceLevel paints under the candles; at or above it, over them. That is what lets a trader send a zone behind price without it disappearing.

DrawingZOrder
using SabrTrader.Pipeline.Drawing;

public static class DrawingZOrder
{
    public const int PriceLevel = 0;

    public static bool IsBehindPrice(int zOrder);   // zOrder < PriceLevel
}
ZOrder is an ordinary settable property on IDrawingTool / DrawingToolBase, so your tool inherits it and persists it with the rest of its state; there is nothing to implement. The chart paints the drawings behind price first, then the candles, then the drawings in front. The Arrange menu (Bring to Front, Bring Forward, Send Backward, Send to Back) changes ZOrder for the user, and one step may cross under or over price.

Per-timeframe visibility
A trader can limit a drawing to the charts where it means something: a weekly level on 1 to 24 hour and daily charts only, an intraday line on seconds and 1 to 15 minute charts only. On every other chart the drawing is neither painted nor clickable, and a selected drawing that becomes hidden (the trader switches the chart from 5 minutes to daily) is deselected. The setting is the tool's Visibility property, a DrawingVisibility.

If you derive from DrawingToolBase, you do nothing. The base declares Visibility as a [DrawingToolProperty] in its own "Visibility" category, so the properties dialog shows the Visibility editor for your tool, the value is saved with the drawing and restored on load, and drawing templates copy it. The chart does the filtering: your OnRender and HitTest are called only on charts where the drawing is visible. A tool that implements IDrawingTool directly gets the interface default, DrawingVisibility.Everywhere, and shows on every chart.

DrawingVisibility
using SabrTrader.Pipeline.Drawing;

// One rule per unit. Immutable: With() returns a new instance.
public sealed class DrawingVisibility : IEquatable<DrawingVisibility>
{
    public static DrawingVisibility Everywhere { get; }     // shared default instance
    public bool IsEverywhere { get; }                       // no rule hides it anywhere
    public DrawingTimeframeRule RuleFor(DrawingTimeframeUnit unit);
    public DrawingVisibility With(DrawingTimeframeUnit unit, DrawingTimeframeRule rule);
    public bool IsVisibleOn(DrawingTimeframe timeframe);    // allocation-free
}

// Enabled, and for a time unit the inclusive From..To range (0, 0 for the other units).
public readonly record struct DrawingTimeframeRule(bool Enabled, int From, int To)
{
    public static DrawingTimeframeRule Full(DrawingTimeframeUnit unit);
}

public enum DrawingTimeframeUnit
{
    Ticks, Seconds, Minutes, Hours, Days, Weeks,   // Seconds..Weeks are the time units
    Range, Volume, Renko, Other
}
Time charts are split per unit and each time unit's rule carries a range: Seconds and Minutes go up to 59, Hours to 24, Days to 366, Weeks to 52 (DrawingTimeframeUnits.RangeMax). A chart above the top counts as the top, so a Minutes rule reaching 59 also covers a 90-minute chart. Tick, range, volume, Renko (including Median Renko and UniRenko) and other bar types are one family each, switched on or off as a whole. Heikin-Ashi counts as the bars it is built from.

To read or set it from code, work with the whole object. Leave the constructor default at Everywhere: a drawing that shows everywhere is saved without a visibility entry and loads back with whatever the constructor set. This sets a placed tool to show only on 1 to 4 hour charts and daily charts:

Presetting visibility
using SabrTrader.Pipeline.Drawing;

static void ShowOnSwingCharts(DrawingToolBase tool)
{
    // Start with every unit switched off. Full() gives each unit a valid range.
    var v = DrawingVisibility.Everywhere;
    foreach (var unit in DrawingTimeframeUnits.All)
        v = v.With(unit, DrawingTimeframeRule.Full(unit) with { Enabled = false });

    v = v.With(DrawingTimeframeUnit.Hours, new DrawingTimeframeRule(true, 1, 4))
         .With(DrawingTimeframeUnit.Days, DrawingTimeframeRule.Full(DrawingTimeframeUnit.Days));

    tool.Visibility = v;   // one reference write: a chart painting meanwhile sees the old set or the new one

    DrawingTimeframeRule hours = tool.Visibility.RuleFor(DrawingTimeframeUnit.Hours);   // Enabled, 1, 4
    bool on15m = tool.Visibility.IsVisibleOn(DrawingTimeframe.Time(DrawingTimeframeUnit.Minutes, 15)); // false
}
With validates the rule against its unit and throws ArgumentOutOfRangeException for one it cannot hold: a time unit needs 1 <= From <= To <= RangeMax, every other unit needs From = To = 0. That is why the switched-off rules above start from Full. Assigning null to Visibility throws ArgumentNullException. DrawingTimeframe describes a chart: DrawingTimeframe.Of(barSpec) classifies a BarSpecification, OfPeriod(TimeSpan) picks the largest unit that divides a period exactly, and DrawingTimeframe.Unknown (no bar type yet) is visible under every rule. The full member list is in the reference.

Which coordinates your tool actually uses
Placement always fills both halves of an anchor (the cursor has a real time and a real price), so a horizontal line carries a Time its renderer never reads, and a vertical line carries a Price nobody looks at. Invisible while anchors are only ever dragged; not invisible in the properties dialog's coordinate editor, where a user types a price into a vertical line and watches nothing happen. Declare what you use:

AnchorCoordinates
public enum DrawingAnchorCoordinates { TimeAndPrice = 0, TimeOnly = 1, PriceOnly = 2 }

public sealed class VerticalMarker : DrawingToolBase
{
    public override DrawingAnchorCoordinates AnchorCoordinates => DrawingAnchorCoordinates.TimeOnly;
    // ...
}
The default is TimeAndPrice, which is right for every tool whose renderer maps both components to pixels: trend lines, rectangles, fibs. The editor then offers only the fields your tool honours.

Own grips and trade plans
Two optional interfaces extend what the chart does with a tool. Implement either one on the tool class, next to DrawingToolBase; a tool that implements neither behaves as described above.

IDrawingToolGrips is for a drawing that is resized through grips of its own instead of by dragging its stored anchors, such as a box whose stop and entry each move in price without dragging the other along. While the drawing is selected and unlocked the chart calls TryHitGrip; you answer with a DrawingGrip carrying your own number for the grip and the cursor direction. For as long as the grip is held the chart calls DragGrip with that grip and the time and price under the pointer. The chart offers no anchor drag for such a drawing, and pressing anywhere else on it moves it as a whole.

IDrawingToolTradePlan is for a drawing that describes a trade. When TryGetTradePlan returns a DrawingTradePlan, the drawing's menu offers to place it as an entry order protected by its stop and target. Your tool only describes the trade: the host places it through its own IDrawingTradePlanPlacer and decides whether the entry goes out as a limit or a stop order (DrawingTradeEntryKind).

RiskBox.cs
public sealed class RiskBox : DrawingToolBase, IDrawingToolGrips, IDrawingToolTradePlan
{
    public RiskBox() : base(2) { }   // Anchors[0] = stop, Anchors[1] = entry

    public bool TryHitGrip(double pixelX, double pixelY, IDrawingToolRenderContext ctx,
        out DrawingGrip grip, double tolerancePx = 8)
    {
        for (int i = 0; i < 2; i++)
        {
            var (x, y) = ctx.AnchorToPixel(Anchors[i]);
            if (Math.Abs(pixelX - x) > tolerancePx || Math.Abs(pixelY - y) > tolerancePx) continue;
            grip = new DrawingGrip(i, IsHorizontal: false);   // moves in price: up-down cursor
            return true;
        }
        grip = default;
        return false;
    }

    // Take the price only: the grip keeps its place in time.
    public void DragGrip(DrawingGrip grip, DrawingAnchor pointer)
        => SetAnchor(grip.Id, Anchors[grip.Id] with { Price = pointer.Price });

    public bool TryGetTradePlan(IDrawingToolRenderContext ctx, out DrawingTradePlan plan)
    {
        plan = default;
        if (ctx.TickSize is not { } tick || tick <= 0) return false;   // an order needs prices on the grid
        double stop  = Math.Round(Anchors[0].Price / tick) * tick;
        double entry = Math.Round(Anchors[1].Price / tick) * tick;
        if (stop == entry) return false;
        double target = entry + 2 * (entry - stop);                      // twice the risk
        if (target <= 0) return false;
        bool isLong = entry > stop;                                     // stop below the entry: a buy
        plan = new DrawingTradePlan(isLong, entry, stop, target, 1m);    // 1m: the order quantity
        return true;
    }

    // OnRender and HitTest as for any tool. Paint the grips yourself while selected:
    // if (ctx.IsSelected) ctx.DrawHandle(x, y);
}
Every member is listed in the reference: Drawings that own their grips and Drawings that describe a trade.

Related pages
This overview is the entry point. The pages below cover each part of building a tool, in the order you will usually need them.

Page	What it covers
The render context	The IDrawingToolRenderContext that does time/price↔pixel work.
Your first tool	A complete two-anchor trend line, a labeled line, a text box that wraps, and the metadata fields.
Editable properties	Exposing extra settings with [DrawingToolProperty].
Constrained movement	Overriding Move to restrict dragging.
Fibonacci levels	Editable FibLevelSet level sets.
Geometry & hit-testing	The DrawingGeometry distance helpers.
Discovery & persistence
