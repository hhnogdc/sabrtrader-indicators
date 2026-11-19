Drawing tools
Geometry & hit-testing
A library of pixel-space distance functions so your HitTest stays a one-liner: project anchors to pixels, then ask DrawingGeometry how far the cursor is from your shape.

What hit-testing is for
HitTest(pixelX, pixelY, ctx, tolerancePx = 6) returns true when a pixel is close enough to count as touching the tool. The interaction layer calls it for hover-highlight and click-to-select. The pattern is always the same two steps: convert your anchors to pixels with ctx.AnchorToPixel, then call the matching DrawingGeometry helper and compare its result to tolerancePx. Writing correct point-to-shape distance maths by hand is fiddly and easy to get subtly wrong, so the helpers cover the cases you actually need.

The distance helpers
DrawingGeometry is a static class in SabrTrader.Pipeline.Drawing. All inputs are pixel coordinates, all results are non-negative distances. There is one helper per common primitive.

Helper	Distance to
DistanceToSegment(px, py, x1, y1, x2, y2)	A finite line segment.
DistanceToRay(px, py, x1, y1, x2, y2)	A ray from p1 through p2 to infinity.
DistanceToHorizontalLine(py, lineY)	An infinite horizontal line.
DistanceToVerticalLine(px, lineX)	An infinite vertical line.
DistanceToHorizontalRay(px, py, lineY, startX, endX)	A horizontal ray (endX may be +∞).
DistanceToRectangleOutline(px, py, x1, y1, x2, y2)	The nearest edge of an axis-aligned rectangle (outline only).
DistanceToCircleOutline(px, py, cx, cy, radius)	A circle outline.
DistanceToEllipseOutline(px, py, x1, y1, x2, y2)	An axis-aligned ellipse outline (sampled).
DistanceToClosedPolyline(px, py, points)	A closed polyline (last point joins the first).
DistanceToOpenPolyline(px, py, points)	An open polyline (freehand / path).
QuadraticBezier(p0, p1, p2, t)	Evaluates a quadratic Bézier at t ∈ [0,1] (curve sampling).
Outlines are hollow. The rectangle / circle / ellipse helpers measure to the outline, so a click inside a hollow shape is not a hit and falls through to the chart. If your tool has a filled interior, hit-test the fill separately (a point-in-rect test) before falling back to the outline distance.
Picking the right helper
Match the helper to the geometry the tool actually draws. A segment-based tool uses DistanceToSegment; a ray uses DistanceToRay; a freehand brush stroke (a list of sampled anchors) uses DistanceToOpenPolyline; a closed triangle uses DistanceToClosedPolyline.

BrushHitTest.cs
public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
{
    // Project every sampled anchor to pixels, then measure to the open polyline.
    Span<(double X, double Y)> pts = AnchorCount <= 64
        ? stackalloc (double, double)[AnchorCount]
        : new (double, double)[AnchorCount];

    for (int i = 0; i < AnchorCount; i++)
        pts[i] = ctx.AnchorToPixel(Anchors[i]);

    return DrawingGeometry.DistanceToOpenPolyline(px, py, pts) <= tol;
}
The polyline helpers take a ReadOnlySpan<(double X, double Y)>, so you can stack-allocate the point buffer for small shapes and avoid a per-frame heap allocation on the hover path. Curved tools (arc, curve, double-curve) sample QuadraticBezier at a handful of t values into such a span, then hit-test it as an open polyline, the same approach the renderer uses to draw the curve.

