Reference
Drawing
The contracts for chart drawing tools: the tool interface and base class, the time/price anchor model, the per-frame render context, hit-test geometry helpers, the discovery attributes/descriptors, and the line/placement enums. All types live under SabrTrader.Pipeline.Drawing (Fibonacci helpers under SabrTrader.Pipeline.Drawing.Tools).

On this page
IDrawingTool
DrawingToolBase
DrawingAnchor
DrawingGeometry
IDrawingToolRenderContext
Visual order, resize grips & text
Drawings that own their grips
Drawings that describe a trade
Per-timeframe visibility
DrawingToolMetadataAttribute
DrawingToolPropertyAttribute
LineStyle
DrawingPlacementMode
DrawingScope
ElliottDegree
FibLevels
Drawing tools: SabrTrader.Pipeline.Drawing
IDrawingTool INTERFACE
One placed instance of a drawing tool (trend line, ray, horizontal line, …). State, render, and hit-test all live on the tool; the chart's drawing layer owns the list and coordinates the lifecycle. Most authors derive from DrawingToolBase rather than implementing this directly.

Member	Description
Guid Id { get; }	Process-unique identity, generated at construction. DrawingToolBase adds a setter so a saved drawing is restored with its id; treat it as immutable.
string ToolName { get; }	Display-only name (e.g. "Trend Line"); sourced from DrawingToolMetadataAttribute.
ChartPanelKey PanelId { get; set; }	Panel the tool belongs to: a ChartPanelKey (series index plus panel id). ChartPanelKey.Main, equal to default, is the primary series' main candle panel; any other key names a subpanel. Render and hit-test filter by this.
bool IsSelected { get; set; }	True when selected (handles drawn, draggable, deletable).
bool IsPlaced { get; set; }	True once the user has finalised the tool (all anchors placed).
bool IsLocked { get; set; }	A locked drawing cannot be selected by clicking, moved or deleted until it is unlocked. See below.
int AnchorCount { get; }	Currently-placed anchor count.
int MaxAnchorCount { get; }	Upper bound on anchor count. Fixed tools: equals AnchorCount; variable tools: a cap or int.MaxValue.
DrawingPlacementMode PlacementMode { get; }	How the placement state machine gathers anchors.
bool CanAddAnchor { get; }	True when another anchor can still be appended (AnchorCount < MaxAnchorCount).
IReadOnlyList<DrawingAnchor> Anchors { get; }	Currently-set anchors; uninitialised slots are zero-valued.
DrawingAnchorCoordinates AnchorCoordinates { get; }	Which anchor components the tool reads. Default member: TimeAndPrice. See below.
bool UsesCornerAndSideHandles { get; }	True for an axis-aligned two-anchor box that resizes from eight grips. Default member: false. See below.
ChartColor Color { get; set; }	Stroke colour.
double LineWidthPx { get; set; }	Line width in pixels (> 0).
LineStyle LineStyle { get; set; }	Stroke style.
DrawingScope Scope { get; set; }	Whether the drawing shows on this chart only (default) or on every open chart of the same symbol. See DrawingScope.
DrawingVisibility Visibility { get; }	On which timeframes and bar types the drawing shows; on every other chart it is neither painted nor clickable. Default member: DrawingVisibility.Everywhere. DrawingToolBase makes it an editable, saved property with a setter. See Per-timeframe visibility.
int ZOrder { get; set; }	Position in the chart's visual stack; below DrawingZOrder.PriceLevel paints under the candles. See below.
void SetAnchor(int index, DrawingAnchor anchor)	Set a single anchor by index; validates against AnchorCount.
void AddAnchor(DrawingAnchor anchor)	Append an anchor (open-ended / drag-to-paint). Throws InvalidOperationException when CanAddAnchor is false.
bool RemoveLastAnchor()	Remove the most-recently-appended anchor; false when nothing to remove.
void Move(TimeSpan dTime, double dPrice)	Move all anchors by a (time, price) delta. Constrained tools override.
void OnRender(IDrawingToolRenderContext ctx)	Render the tool; called every repaint frame.
bool HitTest(double pixelX, double pixelY, IDrawingToolRenderContext ctx, double tolerancePx = 6)	True when the pixel point hits the tool within tolerance; drives hover-highlight + click-to-select.
void OnAdded()	Default member, does nothing. Called by the chart on its UI thread when the drawing is put on it: placed, loaded, mirrored from another chart or brought back by an undo.
void OnRemoved()	Default member, does nothing. Called by the chart on its UI thread when the drawing leaves it: deleted, cleared, removed by the all-charts sync or by an undo. Release what the drawing holds that outlives it, such as a LatestBar volume profile. An undo or redo can add the same drawing again.
IDrawingTool.cs
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
    void OnAdded() { }
    void OnRemoved() { }
}
DrawingToolBase
Default IDrawingTool base. Anchor storage, identity, selection, default styling (Color / LineWidth / LineStyle), the Scope, Locked and Visibility settings, the stack position, and reading the metadata attribute for ToolName all live in the base, so a subclass only implements OnRender + HitTest. Two construction modes: fixed-anchor (base(anchorCount: N)) pre-allocates N slots and runs DrawingPlacementMode.FixedAnchors; variable-anchor (base(mode, maxAnchorCount, initialAnchorCount)) starts at the initial count and grows via AddAnchor.

Member	Description
protected DrawingToolBase(int anchorCount)	Fixed-anchor ctor: pre-allocates anchorCount default anchors; mode = FixedAnchors. Throws if anchorCount < 1.
protected DrawingToolBase(DrawingPlacementMode mode, int maxAnchorCount = int.MaxValue, int initialAnchorCount = 0)	Variable-anchor ctor. Throws ArgumentException if mode == FixedAnchors (use the int ctor).
int MinAnchorsForFinalize { get; protected set; }	Minimum anchors before a finalize gesture is accepted (open-ended / drag-to-paint). Defaults to 2.
ChartColor Color { get; set; }	Stroke colour; annotated [DrawingToolProperty]. Defaults to DodgerBlue.
LineStyle LineStyle { get; set; }	Stroke pattern; annotated [DrawingToolProperty]. Defaults to Solid.
double LineWidthPx { get; set; }	Stroke thickness in px; annotated [DrawingToolProperty]. Setter throws ArgumentOutOfRangeException when <= 0.
DrawingScope Scope { get; set; }	Attach-to setting; annotated [DrawingToolProperty] (Category "Behavior"). Defaults to ThisChart.
bool IsLocked { get; set; }	Lock setting; annotated [DrawingToolProperty] (Category "Behavior"). Saved with the drawing, left out of drawing templates.
DrawingVisibility Visibility { get; set; }	Per-timeframe visibility; annotated [DrawingToolProperty] (Category "Visibility"). Defaults to DrawingVisibility.Everywhere. Saved with the drawing and copied by drawing templates. Replace the whole value to change it (the type is immutable). The setter throws ArgumentNullException on null.
int ZOrder { get; set; }	Stack position; defaults to DrawingZOrder.PriceLevel (in front of price). Saved with the drawing; the chart's Arrange commands change it.
public virtual DrawingAnchorCoordinates AnchorCoordinates { get; }	Returns TimeAndPrice. Override for a time-only or price-only tool.
public virtual bool UsesCornerAndSideHandles { get; }	Returns false. Override to return true for an axis-aligned two-anchor box.
protected void DrawLine(IDrawingToolRenderContext ctx, double x0, double y0, double x1, double y1)	Convenience stroke through the renderer using the tool's own Color, LineWidthPx and LineStyle.
protected void DrawLine(IDrawingToolRenderContext ctx, double x0, double y0, double x1, double y1, ChartColor color, double widthPx)	Same, with an explicit colour and width (still applies the tool's LineStyle).
public abstract void OnRender(IDrawingToolRenderContext ctx)	Implement to paint the tool.
public abstract bool HitTest(double pixelX, double pixelY, IDrawingToolRenderContext ctx, double tolerancePx = 6)	Implement the hit-test.
public virtual void Move(TimeSpan dTime, double dPrice)	Shifts every anchor by the delta; override for constrained tools.
protected IOrderFlowWindow? Profile(IDrawingToolRenderContext ctx, in OrderFlowWindowRequest request)	The volume profile this drawing holds for the request, opened on the chart's host when the drawing has none, its request changed or its chart got a new host; the one it replaces is disposed. Null when the chart has no order-flow host or after the drawing left its chart. Call it every frame from OnRender: the same request returns the same profile with no allocation. See Order flow & depth.
public IOrderFlowWindow? HeldProfile { get; }	The volume profile this drawing holds, or null.
public virtual void OnAdded(), public virtual void OnRemoved()	The chart's add and remove calls. OnRemoved stops a held LatestBar profile; a fixed one is kept, so an undo shows it at once. Overrides call the base.
The interface members (Id, ToolName, PanelId, IsSelected, IsPlaced, AnchorCount, MaxAnchorCount, PlacementMode, CanAddAnchor, Anchors, SetAnchor, AddAnchor, RemoveLastAnchor) are all implemented by the base. The example below is a box that resizes from its corners and side midpoints.

AcmeRectangle.cs
using SabrTrader.Pipeline.Drawing;

[DrawingToolMetadata("Acme Rectangle", Category = "Shapes", AnchorCount = 2)]
public sealed class AcmeRectangle : DrawingToolBase
{
    public AcmeRectangle() : base(anchorCount: 2) { }

    public override bool UsesCornerAndSideHandles => true;

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        var p1 = ctx.AnchorToPixel(Anchors[0]);
        var p2 = ctx.AnchorToPixel(Anchors[1]);
        DrawLine(ctx, p1.X, p1.Y, p2.X, p1.Y);
        DrawLine(ctx, p2.X, p1.Y, p2.X, p2.Y);
        DrawLine(ctx, p2.X, p2.Y, p1.X, p2.Y);
        DrawLine(ctx, p1.X, p2.Y, p1.X, p1.Y);
        if (ctx.IsSelected) AxisAlignedBoxResize.DrawHandles(ctx, p1.X, p1.Y, p2.X, p2.Y);
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        var p1 = ctx.AnchorToPixel(Anchors[0]);
        var p2 = ctx.AnchorToPixel(Anchors[1]);
        return DrawingGeometry.DistanceToRectangleOutline(px, py, p1.X, p1.Y, p2.X, p2.Y) <= tol;
    }
}
DrawingAnchor
A single anchor point: a (time, price) pair in the bar series' canonical timeline. Anchors are bar-index-independent, so a tool survives live bar updates, scroll, replays, and bar-type changes. Time-only tools ignore Price; price-only tools ignore Time; the unused coordinate is stored as a sentinel and never read.

DrawingAnchor.cs
public readonly record struct DrawingAnchor(DateTime Time, double Price)
{
    public static DrawingAnchor PriceOnly(double price);   // Time = DateTime.MinValue
    public static DrawingAnchor TimeOnly(DateTime time);   // Price = double.NaN
}
DrawingGeometry
Static 2-D geometry helpers for hit tests. All inputs are pixel coordinates, so call them after converting anchors via IDrawingToolRenderContext.AnchorToPixel. Outline helpers return a non-negative distance; compare it against the tool's pixel tolerance.

Signature	Description
double DistanceToSegment(px, py, x1, y1, x2, y2)	Perpendicular distance to a finite line segment.
double DistanceToRay(px, py, x1, y1, x2, y2)	Distance to a ray from (x1,y1) through (x2,y2) extending to infinity.
double DistanceToHorizontalLine(py, lineY)	Distance to an infinite horizontal line at y = lineY.
double DistanceToVerticalLine(px, lineX)	Distance to an infinite vertical line at x = lineX.
double DistanceToHorizontalRay(px, py, lineY, startX, endX)	Distance to a horizontal ray from startX (to endX or +∞).
double DistanceToRectangleOutline(px, py, x1, y1, x2, y2)	Distance to the closest edge of an axis-aligned rectangle (outline only).
double DistanceToClosedPolyline(px, py, ReadOnlySpan<(double X, double Y)> points)	Distance to a closed polyline (last point auto-connects to first).
double DistanceToOpenPolyline(px, py, ReadOnlySpan<(double X, double Y)> points)	Distance to an open polyline.
double DistanceToCircleOutline(px, py, cx, cy, radius)	Absolute distance to a circle outline.
double DistanceToEllipseOutline(px, py, x1, y1, x2, y2)	Distance to an axis-aligned ellipse outline (64-sample approximation).
(double X, double Y) QuadraticBezier((double X, double Y) p0, (double X, double Y) p1, (double X, double Y) p2, double t)	Evaluate a quadratic Bézier at t ∈ [0, 1].
IDrawingToolRenderContext INTERFACE
Per-frame context handed to OnRender and HitTest. Encapsulates the renderer, viewport, plot-area rectangle, selection state, and time→pixel conversion. All the plumbing lives here so tool authors write almost-trivial render/hit-test methods. The instrument-metadata members default to null/identity so headless and test shims need not supply them.

Member	Description
IChartRenderer Renderer { get; }	Active renderer for the current frame.
ChartViewport Viewport { get; }	Bar↔pixel + price↔pixel transforms for the current paint.
PlotAreaRect PlotArea { get; }	Pixel rectangle of the main candle pane (excludes axis strips).
bool IsSelected { get; }	True when the tool being rendered/hit-tested is selected.
double TimeToBarIndex(DateTime time)	Convert an anchor time to a (possibly fractional) bar index.
(double X, double Y) AnchorToPixel(DrawingAnchor anchor)	Convenience: anchor → pixel (X, Y), combining TimeToBarIndex + viewport transforms.
void DrawHandle(double x, double y, ChartColor? fill = null, double sizePx = 7)	Draw a small selection handle at a pixel point.
IBarSeries? Bars { get; }	Bar series driving the chart, when the host has one. Default null.
double? TickSize { get; }	Instrument minimum price increment, when known. Default null.
double? PointValue { get; }	Currency value of a 1.0 price move per contract, when known. Default null.
double? PipSize { get; }	Instrument pip size (forex-style quoting), when known. Default null.
QuantityUnit QuantityUnit { get; }	Unit the instrument's quantity is counted in. Default QuantityUnit.Contract.
double? QuantityStep { get; }	Smallest tradable quantity increment, when known. Default null.
IOrderFlowWindowSource? OrderFlowWindows { get; }	The host that builds the volume profile of any time window: a drawing opens one with Open (or DrawingToolBase.Profile) and holds it, then reads its rows with TryGetProfile or lets the host paint it with Paint (see Order flow & depth). Needs an order-flow licence. Default null.
Visual order, resize grips & text
Type	Members
DrawingZOrder	static: const int PriceLevel = 0, bool IsBehindPrice(int zOrder). The chart's visual stack is one number line and the price series is a slot on it; a drawing below PriceLevel paints under the candles. The chart's Arrange menu (Bring to Front, Bring Forward, Send Backward, Send to Back) changes ZOrder, and a one-step move may cross under or over price.
IDrawingTool.IsLocked	bool, get/set, on the interface and on DrawingToolBase. A locked drawing cannot be selected by clicking, moved or deleted until it is unlocked. It is an editable property, so it persists with the drawing and gets a checkbox in the properties dialog for free. Drawing templates leave it out: a lock is per-instance state, not style, so applying a template (or saving one as a tool default) never locks or unlocks anything.
IDrawingTool.UsesCornerAndSideHandles	bool, default false (virtual on DrawingToolBase). Return true for an axis-aligned box stored as two diagonal anchors: the host then lets the user resize it from all four corners and all four side midpoints, not only from the two placed anchors. Rectangle and Gann box opt in. Paint the grips from OnRender while selected: if (ctx.IsSelected) AxisAlignedBoxResize.DrawHandles(ctx, x0, y0, x1, y1);. Hit-testing, the drag and the cursor are the host's.
AxisAlignedBoxResize	static: DrawHandles(ctx, x0, y0, x1, y1) paints the eight grips (four corners, four side midpoints) of the box spanned by those pixel corners. Call it from OnRender while the tool is selected. The host hit-tests the grips (corners win over side midpoints on a small box), shows the resize cursor and runs the drag for every tool that UsesCornerAndSideHandles: a corner moves the time of one anchor and the price of the other when the box was drawn "backwards", a side moves only the edge it owns, and pulling an edge through the opposite one keeps following the pointer.
IDrawingTool.ZOrder	int, get/set. Ordinary state on DrawingToolBase: inherited, persisted, nothing to implement.
DrawingAnchorCoordinates	enum: TimeAndPrice (default), TimeOnly (vertical line), PriceOnly (horizontal line). Declared through IDrawingTool.AnchorCoordinates (virtual on the base) so the properties dialog's coordinate editor never offers a field the tool ignores.
IDrawingToolTextContent	string Text { get; set; }, double FontSizePx, bool Bold, bool Italic, double WrapWidthPx (default member, returns 0). Implement it on a tool whose primary content is text the user types (the built-in Text tool does) and the tool opts into the chart's INLINE editor: placing it drops a caret straight onto the chart, and double-clicking an existing one re-opens that caret, so a three-word annotation never needs the properties dialog. The font members let the editor match the text it replaces, so the caret sits where the glyphs will land. WrapWidthPx is the width in screen pixels the tool wraps its text within, and the caret wraps at the same place; 0 means no wrapping, each line runs as long as it is. In the caret Enter commits and Shift+Enter starts a new line, so Text can hold line breaks ("\r\n", "\n" or "\r"); draw one line per break. This is about the editing GESTURE; which values are configurable is still [DrawingToolProperty]. Worked example: a text box that wraps.
IDrawingToolWidthGrip	bool TryHitWidthGrip(double pixelX, double pixelY, IDrawingToolRenderContext ctx, out double originXPx, double tolerancePx = 8), void ResizeFromGrip(double offsetFromOriginPx). For a drawing whose width the user sets by dragging a grip, in screen pixels rather than chart time or price (the Text tool's box). The chart asks for the grip only while the drawing is selected and unlocked. Return true from TryHitWidthGrip when the pointer is on your grip and set originXPx to the pixel X the width is measured from (the box's left edge); it stays fixed for the whole drag. The chart then shows a left-right resize cursor and, while the grip is dragged, calls ResizeFromGrip with the pointer's distance right of that origin (negative when left of it). Clamp it to what your drawing can show. Paint the grip yourself (for example with ctx.DrawHandle) while ctx.IsSelected.
LabeledLineToolBase : DrawingToolBase	Base for line tools carrying a user-typed label: string Text, DrawingLabelPosition LabelPosition, DrawingLabelSide LabelSide, double LabelFontSizePx, ChartColor LabelColor, bool LabelBold, bool LabelItalic; protected void DrawLabel(ctx, x0, y0, x1, y1) called at the end of OnRender with the segment just stroked. Empty Text renders nothing.
DrawingLabelPosition · DrawingLabelSide	enum Start, Middle, End; and Above, Below.
Drawings that own their grips
A drawing implements IDrawingToolGrips when it is resized through grips of its own instead of by dragging its stored anchors. The built-in Position Sizing tool does: its target is calculated, not stored, and its stop, entry and target each move without dragging the others along. The chart asks for a grip only while the drawing is selected and unlocked, shows the grip's resize cursor over it, and reports the pointer for as long as the grip is held. The chart offers no anchor drag for such a drawing: every resize runs through DragGrip, and pressing anywhere else on the drawing moves it as a whole. The drawing paints its grips itself, from OnRender while ctx.IsSelected (for example with ctx.DrawHandle). Guide with an example: Own grips and trade plans.

Type	Members
IDrawingToolGrips	bool TryHitGrip(double pixelX, double pixelY, IDrawingToolRenderContext ctx, out DrawingGrip grip, double tolerancePx = 8): return true when the pointer is on one of the drawing's grips and set grip to it. void DragGrip(DrawingGrip grip, DrawingAnchor pointer): the held grip was dragged to pointer, the chart time and price under the pointer. The drawing takes the component the grip owns (the price for a grip that moves in price, the time for one that moves in time) and ignores a position it cannot show.
DrawingGrip (readonly record struct)	DrawingGrip(int Id, bool IsHorizontal). Id: the drawing's own number for the grip. The chart hands it back unchanged on every step of the drag and gives it no meaning of its own. IsHorizontal: which resize cursor the chart shows, left-right when true (the grip moves in time), up-down when false (the grip moves in price).
Drawings that describe a trade
A drawing implements IDrawingToolTradePlan when it describes a trade: a side, an entry, a stop and a target with a size, as a position-sizing box does. The chart then offers to place that trade as an order from the drawing's own menu. A tool that does not implement the interface is never offered. The drawing only describes the trade. The host's trader places it, through IDrawingTradePlanPlacer, which the host implements and the chart calls. The menu item appears only when the host has a trader, the drawing sits in the price panel of the chart's primary series, and TryGetTradePlan returns true.

Type	Members
IDrawingToolTradePlan	bool TryGetTradePlan(IDrawingToolRenderContext ctx, out DrawingTradePlan plan): the trade the drawing describes on the instrument ctx states (TickSize, PointValue, QuantityStep). Return false when the drawing describes no trade that could be sent: the instrument's tick size is unknown, the stop sits on the entry, or the size is zero.
DrawingTradePlan (readonly record struct)	DrawingTradePlan(bool IsLong, double EntryPrice, double StopPrice, double TargetPrice, decimal Quantity). IsLong: true for a buy entry (the stop below it, the target above it), false for a sell entry. EntryPrice: the price the entry rests at. StopPrice: the protective stop, on the loss side. TargetPrice: the profit target, on the profit side. Quantity: the size as an order carries it (base-currency units for forex), positive. Every price lies on the instrument's tick grid, so snap the prices to ctx.TickSize before building the plan.
DrawingTradeEntryKind (enum)	How the entry of a plan goes out, given where its entry price lies against the market. Limit = 0: the entry rests on the passive side of the market (a buy at or below it, a sell at or above it) and goes out as a limit order. Stop = 1: the market has to reach the entry first (a buy above it, a sell below it) and it goes out as a stop order.
IDrawingTradePlanPlacer	Implemented by the host, not by a drawing tool: it is what a chart hands a drawing's trade to. DrawingTradeEntryKind? EntryKindOf(DrawingTradePlan plan): how the entry of plan would go out at this moment, or null when the trader has no live price to tell a limit from a stop. The menu item reads it for its label. void Place(DrawingTradePlan plan): places the plan as a limit or stop entry protected by its stop and target, deciding the entry kind again from the market at that moment. A plan that cannot be placed is refused and the trader is told why; nothing is sent then.
Per-timeframe visibility
On which charts a drawing shows. A DrawingVisibility holds one DrawingTimeframeRule per DrawingTimeframeUnit; the chart classifies itself as a DrawingTimeframe and paints and hit-tests only the drawings whose visibility IsVisibleOn it. DrawingToolBase exposes it as the editable, saved Visibility property, so a tool deriving from it has nothing to implement. Guide with an example: Per-timeframe visibility.

Type	Members
DrawingVisibility (sealed class)	static DrawingVisibility Everywhere { get; }: visible on every chart, one shared instance and the default of every drawing. bool IsEverywhere { get; }: true when no rule hides the drawing anywhere. DrawingTimeframeRule RuleFor(DrawingTimeframeUnit unit): the rule for one unit; throws ArgumentOutOfRangeException for an undeclared unit. DrawingVisibility With(DrawingTimeframeUnit unit, DrawingTimeframeRule rule): a copy with that unit's rule replaced (returns the same instance when the rule is unchanged); throws ArgumentOutOfRangeException for a rule the unit cannot hold (a time unit needs 1 <= From <= To <= RangeMax, any other unit From = To = 0). bool IsVisibleOn(DrawingTimeframe timeframe): true when the drawing shows on that chart; an unknown timeframe is always visible, a time chart above its unit's top counts as the top. Allocation-free, safe to call per drawing per frame. Immutable, value equality (Equals, GetHashCode), shared freely between drawings, clones and templates.
DrawingTimeframeRule (readonly record struct)	DrawingTimeframeRule(bool Enabled, int From, int To). Enabled: the drawing shows on charts of this unit at all. From / To: for a time unit, the inclusive range of periods it shows on (Hours 1 to 4); 0 and 0 for a unit without a range. static DrawingTimeframeRule Full(DrawingTimeframeUnit unit): the rule that shows on every chart of the unit (true, 1, RangeMax for a time unit, true, 0, 0 otherwise).
DrawingTimeframe (readonly record struct)	What a chart shows. DrawingTimeframeUnit Unit { get; }; int Value { get; }: units per bar for a time chart (at least 1), 0 for the other families; bool IsKnown { get; }: false for Unknown, and Unit is meaningless then. static DrawingTimeframe Unknown { get; }: no bar type yet (chart still loading); every drawing is visible. static DrawingTimeframe Time(DrawingTimeframeUnit unit, int value): a time chart; throws for a unit that is not a time unit or a value below 1. static DrawingTimeframe Family(DrawingTimeframeUnit unit): a chart of a family without a range (Ticks, Range, Volume, Renko, Other); throws for a time unit. static DrawingTimeframe Of(BarSpecification? spec): classifies a bar type; Heikin-Ashi counts as the bars it is built from, a bar type this build does not know (a plugin's) counts as Other, null is Unknown. static DrawingTimeframe OfPeriod(TimeSpan period): a period in the largest unit that divides it exactly (2 hours is Hours 2, 90 minutes is Minutes 90, 14 days is Weeks 2); a sub-second period counts as Seconds 1, a non-positive one is Unknown.
DrawingTimeframeUnit (enum)	Ticks (a bar every N trades), Seconds (anything that is not a whole minute), Minutes (whole minutes that are not whole hours), Hours (whole hours that are not whole days), Days (whole days that are not whole weeks), Weeks, Range, Volume (a bar every N contracts), Renko (Renko, Median Renko, UniRenko), Other (Kagi, Point & Figure, Line Break and plugin bar types). Values 0 to 9, in the order the Visibility editor lists them. The member names are the saved form.
DrawingTimeframeUnits (static)	const int Count = 10: number of units, one rule each. ReadOnlySpan<DrawingTimeframeUnit> All { get; }: every unit in editor order. bool IsDefined(DrawingTimeframeUnit unit): true for a declared unit. bool IsRanged(DrawingTimeframeUnit unit): true for the time units, whose rule carries a From..To range. int RangeMax(DrawingTimeframeUnit unit): top of the range, Seconds 59, Minutes 59, Hours 24, Days 366, Weeks 52, and 0 for a unit without a range. A range reaching the top also covers every larger period of that unit.
Discovery: attributes & descriptors
DrawingToolMetadataAttribute
Marks a class as a discoverable drawing tool, read at assembly-scan time to populate the toolbar menu, hotkeys, icons, and anchor-count requirements. One per tool class; no registration boilerplate. [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)].

Member	Description
string Name { get; }	Display name (ctor argument; required, non-blank). Also the tool's identity: the catalog indexes tools by it and a saved drawing is matched back by it, so it must be unique across all loaded tools and stable once shipped.
string Category { get; init; }	Toolbar submenu group. Default "Lines".
string? Hotkey { get; init; }	Optional global hotkey (e.g. "Alt+Shift+L"). Unique across all loaded tools: when another tool (built-ins first) already has it, this tool loads without a hotkey and the Plugins log says so.
string? IconKey { get; init; }	One of the toolbar's built-in icons, named after the built-in tool that uses it without spaces or punctuation ("AndrewsPitchfork"). A key that is not an icon shows the default icon and the Plugins log names the tool. The keys are listed under Icons. Ignored when IconPath is set.
string? IconPath { get; init; }	The tool's own icon as path data (SVG / XAML path syntax) in a 24 x 24 box, drawn as a stroke in the theme's icon colour. Takes precedence over IconKey; path data that does not parse shows the default icon.
int AnchorCount { get; init; }	Anchor points the user clicks before finalising. Default 2.
int Order { get; init; }	Display order within the category (lower first). Default 0.
string? Description { get; init; }	Tooltip text.
DrawingToolPropertyAttribute
Marks a tool property as user-editable in the generic properties dialog. Opt-in: infrastructure properties stay hidden. DrawingToolBase already annotates Color, Line Width, Line Style, Attach to (Scope), Locked and Visibility, so every subclass surfaces those by default; decorate your own for extra editors. Annotated values are saved with the drawing when the property type is bool, string, a number (int, long, float, double, decimal), an enum, ChartColor, FibLevelSet or DrawingVisibility. Constraints are advisory (the dialog enforces them; programmatic writes are not clamped). [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)].

Member	Description
string? DisplayName { get; set; }	Editor label (defaults to property name).
string? Description { get; set; }	One-line tooltip.
string Category { get; set; }	Group bucket. Default "General".
int Order { get; set; }	Order within the category (lower first).
object? MinValue { get; set; }	Advisory numeric lower bound. Null = none.
object? MaxValue { get; set; }	Advisory numeric upper bound. Null = none.
object? Step { get; set; }	Spinner increment. Null = editor decides.
bool Multiline { get; set; }	For a string property: the dialog shows a taller box where Enter starts a new line. Ignored for other types.
Enums
LineStyle
Stroke style for drawing-tool segments. Mirrors PlotStyle's line variants.

Value	Meaning
Solid	Continuous stroke.
Dashed	Dashed stroke.
Dotted	Dotted stroke.
DashDot	Dash, gap, dot, gap, repeating. Value 3.
DashDotDot	Dash, gap, dot, gap, dot, gap, repeating. Value 4.
DrawingPlacementMode
How a tool gathers its anchors during placement. The chart's interaction state machine dispatches on this value; each mode has a distinct user gesture.

Value	Meaning
FixedAnchors	Click once per anchor; placement completes when AnchorCount clicks land. Default for line / rectangle / triangle / arrow.
OpenEnded	Click to append anchors, double-click or Escape to finalize (must meet MinAnchorsForFinalize). Used by Path, Polyline.
DragToPaint	Mouse-down + drag samples points; mouse-up finalizes. Used by Brush, Highlighter.
DrawingScope
Where a placed drawing is shown. Surfaced on IDrawingTool.Scope as an "Attach to" [DrawingToolProperty]; each member's [Description] is the dropdown label the editor renders in place of the raw name.

Value	Meaning
ThisChart	"This chart": the drawing lives only on the chart it was placed on (default).
AllCharts	"All charts (same symbol)": the drawing is shared and kept in sync across every open chart of the same symbol.
ElliottDegree
Wave degree for Elliott-wave labelling tools, largest to smallest. Each member's [Description] is its dropdown label (with the notation shown in parentheses). ElliottDegrees.Label(ElliottDegree degree, string key) formats a single wave label for a degree and key.

Value	Notation
GrandSupercycle	[I] / [a]
Supercycle	(I) / (a)
Cycle	I / a
Primary	[1] / [A]
Intermediate	(1) / (A)
Minor	1 / A
Minute	i / a
Minuette	(i) / (a)
Subminuette	i / a
Micro	1 / A
Submicro	(1) / (A)
Miniscule	1 / A
Fibonacci levels: SabrTrader.Pipeline.Drawing.Tools
FibLevels
Standard Fibonacci ratios used by the Fib retracement / extension / channel / wedge / fan family. Centralised so changing a default updates every tool at once. Static fields are the raw ratio ladders; the Default*Set() factories build a ready-to-edit FibLevelSet.

Member	Description
static readonly double[] StandardRetracements	{ 0.0, 0.236, 0.382, 0.5, 0.618, 0.786, 1.0 }: classic retracement ladder.
static readonly double[] StandardExtensions	{ 1.272, 1.414, 1.618, 2.0, 2.618, 3.618, 4.236 }: extension ratios (> 1.0).
static readonly double[] StandardRetracementsAndExtensions	{ 0.0, 0.236, 0.382, 0.5, 0.618, 0.786, 1.0, 1.272, 1.618, 2.0, 2.618 }.
static readonly double[] StandardSpeedRatios	{ 0.382, 0.5, 0.618 }: speed-resistance fan / arc ratios.
static readonly int[] StandardTimeZones	{ 1, 2, 3, 5, 8, 13, 21, 34, 55, 89 }: time-zone multiples.
static readonly double[] StandardGannBoxRatios	{ 0.25, 0.382, 0.5, 0.618, 0.75 }: the Gann box inner grid, default for both its price and its time lines (the outline already marks 0 and 1).
static readonly (int Price, int Time)[] GannAngles	Gann fan slopes from 1×8 to 8×1 (1×1 = central reference).
static FibLevelSet DefaultRetracementSet()	Visible retracement ladder paired with default colours.
static FibLevelSet DefaultExtensionSet()	Visible retracements + extensions with the extension palette.
static FibLevelSet DefaultRetracementSetInheritColor()	Retracement ladder, every level inherits the tool's stroke colour.
static FibLevelSet DefaultSpeedRatioSetInheritColor()	Three speed ratios, inherit stroke colour.
static FibLevelSet DefaultCircleRatioSetInheritColor()	Fib-circle ring set, inherit stroke colour.
static FibLevelSet DefaultGannBoxSetInheritColor()	Gann box grid from StandardGannBoxRatios, inherit stroke colour. Build one set for the price lines and one for the time lines.
FibLevel: one configurable level (a sealed class):

Member	Description
double Ratio { get; set; }	Ratio applied to the tool's anchor range.
ChartColor? Color { get; set; }	Per-level stroke override; null = inherit the tool's stroke colour.
bool IsVisible { get; set; }	Render flag. False hides the level without removing it. Default true.
FibLevel(double ratio, ChartColor? color = null, bool visible = true)	Convenience constructor (also has a parameterless ctor).
FibLevel Clone()	Deep copy.
FibLevelSet: ordered, mutable collection of levels; inherits ObservableCollection<FibLevel> so the properties dialog's list editor binds to it directly:

Member	Description
FibLevelSet() / FibLevelSet(IEnumerable<FibLevel> items)	Empty or seeded from an item sequence.
FibLevelSet Clone()	Deep copy: every level is cloned, not shared.
void ReplaceWith(IEnumerable<FibLevel> other)	Replace the contents with clones of other in one step, announced by a single Reset notification, so a painting thread never sees the set half-empty.
ReadOnlySpan<FibLevel> Snapshot { get; }	The levels as of the last structural change, allocation-free and safe from any thread. Read this on a render thread: enumerating the observable collection itself while the editor mutates it can throw.
protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)	Runs on every structural change (add, insert, remove, replace, move, clear) and republishes FibLevelSet.Snapshot before the collection-changed listeners run, so a listener that reads it sees the changed set. The class is sealed, so there is nothing to override or call here.
