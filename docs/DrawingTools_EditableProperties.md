Drawing tools
Editable properties
Expose per-tool settings (a fill colour, a flag, a number) with one attribute, and the generic properties dialog renders an editor for it and the drawing saves it.

What it is
Beyond the inherited Color, LineWidthPx, LineStyle, Scope, IsLocked and Visibility (already annotated on the base class), you surface any extra setting by tagging a public, read/write property with [DrawingToolProperty]. There is no registration step: the discovery helper finds the attribute reflectively and the properties dialog renders an editor row for it.

It is opt-in: infrastructure properties (IsPlaced, PanelId, Anchors, …) stay hidden, and a tool controls exactly which knobs it exposes. The attribute lives in SabrTrader.Pipeline.Drawing.

DrawingToolPropertyAttribute.cs
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DrawingToolPropertyAttribute : Attribute
{
    public string? DisplayName { get; set; }     // defaults to the property name
    public string? Description { get; set; }      // one-line tooltip
    public string Category { get; set; } = "General";
    public int Order { get; set; }                // lower renders first
    public object? MinValue { get; set; }         // advisory, for numeric editors
    public object? MaxValue { get; set; }         // advisory
    public object? Step { get; set; }             // spinner increment
    public bool Multiline { get; set; }           // string only: a box that accepts line breaks
}
Constraints are advisory. MinValue / MaxValue / Step guide the dialog's numeric editor, but programmatic writes are not silently clamped. Enforce hard invariants in your setter (as the base does for LineWidthPx, which throws on a non-positive value).
Text that spans lines. Set Multiline = true on a string property and the dialog shows a box three lines tall, growing to eight before it scrolls, where Enter starts a new line. The value then holds line breaks as "\r\n", "\n" or "\r" (the box inserts the platform's own), so split on all three when you draw it. Every other string property keeps a one-line box. The flag has no effect on other property types.
Adding a property
Give each property a real default so a freshly placed tool looks right before the user touches anything. The example below adds an interior fill colour to a rectangle. The default fill uses an alpha of 0x33 so it is translucent; an alpha of 0 means "no fill", which the render code checks before painting.

AcmeRectangle.cs
using System;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Plots;

[DrawingToolMetadata("Acme Rectangle", Category = "Shapes", AnchorCount = 2)]
public sealed class AcmeRectangle : DrawingToolBase
{
    public AcmeRectangle() : base(anchorCount: 2) { }

    [DrawingToolProperty(DisplayName = "Fill Color", Category = "Style", Order = 50,
        Description = "Interior fill. Alpha 0 = no fill.")]
    public ChartColor FillColor { get; set; } = new(0x1E, 0x90, 0xFF, 0x33);

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        var a = ctx.AnchorToPixel(Anchors[0]);
        var b = ctx.AnchorToPixel(Anchors[1]);
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);

        if (FillColor.A > 0) ctx.Renderer.FillRect(x, y, w, h, FillColor);   // fill first
        DrawLine(ctx, x, y, x + w, y);                                         // then the outline,
        DrawLine(ctx, x + w, y, x + w, y + h);                                 // in the tool's Color,
        DrawLine(ctx, x + w, y + h, x, y + h);                                 // LineWidthPx and
        DrawLine(ctx, x, y + h, x, y);                                         // LineStyle

        if (ctx.IsSelected) { ctx.DrawHandle(a.X, a.Y); ctx.DrawHandle(b.X, b.Y); }
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        var a = ctx.AnchorToPixel(Anchors[0]);
        var b = ctx.AnchorToPixel(Anchors[1]);
        return DrawingGeometry.DistanceToRectangleOutline(px, py, a.X, a.Y, b.X, b.Y) <= tol;
    }
}
Property fields mirror the metadata. DisplayName, Category (groups rows under a heading), Order (sequence within the group) and Description (tooltip) work the same way they do on [DrawingToolMetadata], so the dialog stays consistent across tools.
Saved with the drawing. Every annotated property is written into the drawing's saved state and restored on load, so FillColor above survives a workspace save with no extra code. That covers bool, string, int, long, float, double, decimal, enums (saved by member name), ChartColor, FibLevelSet and DrawingVisibility. A value of another type is skipped when the drawing is saved, so keep tool settings to those types. Drawing templates copy the same properties, except IsLocked.
How discovery works
The host reflects over the tool type for [DrawingToolProperty] attributes and builds one editor entry per property, sorted by (Category, Order, Name). Results are cached per type, so the per-dialog-open cost is a dictionary lookup. The properties dialog, drawing templates and saved drawings all read and write your properties through the same entries, so a value is coerced the same way on every path. Each entry carries the attribute's editor metadata (display name, description, category, order, minimum, maximum, step, multiline) and the property's type.

Because [DrawingToolProperty] is declared Inherited = true, the annotations on DrawingToolBase (Color, LineWidthPx, LineStyle, Scope, IsLocked, Visibility) flow to every subclass automatically: built-ins and plugin tools alike surface those editors with zero boilerplate. Discovery skips indexers and any property whose getter or setter is not public, so a tool can opt a property out simply by making its setter non-public.

