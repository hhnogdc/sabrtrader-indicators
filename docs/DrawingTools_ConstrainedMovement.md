Drawing tools
Constrained movement
Override one virtual method to control how a tool responds to dragging, for example pinning a horizontal line to a single price so it never drifts sideways in time.

What it is
When the user drags a placed tool, the host calls Move(TimeSpan dTime, double dPrice) with the change in time and price for the drag. The default DrawingToolBase.Move shifts every anchor by both deltas, which is exactly right for a free-floating shape like a trend line or rectangle:

DrawingToolBase.cs
public virtual void Move(TimeSpan dTime, double dPrice)
{
    for (int i = 0; i < AnchorCount; i++)
    {
        var a = Anchors[i];
        SetAnchor(i, new DrawingAnchor(a.Time + dTime, a.Price + dPrice));
    }
}
When a tool has a geometric constraint (a horizontal line that should only change price, a vertical line that should only change time), override Move and apply just the deltas that make sense. Commit each new anchor with SetAnchor.

A horizontal line: price only
A horizontal line keeps each anchor's time and adds only the price delta, so dragging it up or down moves the level but never slides it through time.

AcmeHorizontalLine.cs
using System;
using SabrTrader.Pipeline.Drawing;

[DrawingToolMetadata("Acme Horizontal Line", Category = "Lines", IconKey = "HorizontalLine", AnchorCount = 1)]
public sealed class AcmeHorizontalLine : DrawingToolBase
{
    public AcmeHorizontalLine() : base(anchorCount: 1) { }

    public override void Move(TimeSpan dTime, double dPrice)
    {
        var a = Anchors[0];
        SetAnchor(0, new DrawingAnchor(a.Time, a.Price + dPrice));   // price only
    }

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        double y = ctx.Viewport.PriceToPixelY(Anchors[0].Price);
        DrawLine(ctx, ctx.PlotArea.X, y, ctx.PlotArea.Right, y);
        if (ctx.IsSelected) ctx.DrawHandle((ctx.PlotArea.X + ctx.PlotArea.Right) / 2, y);
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        double y = ctx.Viewport.PriceToPixelY(Anchors[0].Price);
        return DrawingGeometry.DistanceToHorizontalLine(py, y) <= tol;
    }
}
A vertical line: time only
The mirror image keeps each anchor's price and adds only the time delta. Pair it with the DrawingAnchor.TimeOnly factory at placement so the unused price stays a sentinel.

VerticalLine.cs
public override void Move(TimeSpan dTime, double dPrice)
{
    var a = Anchors[0];
    SetAnchor(0, new DrawingAnchor(a.Time + dTime, a.Price));   // time only
}
Use SetAnchor, not direct mutation. Anchors is exposed as a read-only list; route every change through SetAnchor(index, anchor) so the base class keeps anchor count, hit-testing and the rest of the tool's state consistent. SetAnchor validates the index against AnchorCount and throws on an out-of-range slot.
