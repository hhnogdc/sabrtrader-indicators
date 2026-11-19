Drawing tools
Your first tool
A complete two-anchor trend line, end to end (metadata, render, hit-test), showing how little code a real drawing tool needs once the base class and render context do their part.

SIMPLE A trend line
The simplest useful tool has two anchors and draws one segment between them. The shape of every drawing tool is the same three steps: declare the [DrawingToolMetadata] attribute, pass the anchor count to the base constructor, then fill in OnRender and HitTest.

In OnRender you convert both anchors to pixels with AnchorToPixel, draw the line with the base's DrawLine helper (which applies the tool's Color, LineWidthPx and LineStyle), and, only when selected, paint a handle at each end. In HitTest you convert the same anchors and ask DrawingGeometry whether the cursor is within the tolerance of the segment. That is the whole tool.

AcmeTrendLine.cs
using SabrTrader.Pipeline.Drawing;

[DrawingToolMetadata("Acme Trend Line",
    Category = "Lines", Hotkey = "Alt+Shift+L", IconKey = "TrendLine",
    AnchorCount = 2, Order = 0, Description = "Straight line between two points.")]
public sealed class AcmeTrendLine : DrawingToolBase
{
    public AcmeTrendLine() : base(anchorCount: 2) { }

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        var (x1, y1) = ctx.AnchorToPixel(Anchors[0]);
        var (x2, y2) = ctx.AnchorToPixel(Anchors[1]);
        DrawLine(ctx, x1, y1, x2, y2);   // Color, LineWidthPx and LineStyle

        if (ctx.IsSelected)
        {
            ctx.DrawHandle(x1, y1);
            ctx.DrawHandle(x2, y2);
        }
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        var (x1, y1) = ctx.AnchorToPixel(Anchors[0]);
        var (x2, y2) = ctx.AnchorToPixel(Anchors[1]);
        return DrawingGeometry.DistanceToSegment(px, py, x1, y1, x2, y2) <= tol;
    }
}
A line that carries a label
Line tools that can be NAMED ("Support", "VAH", "London open") derive from LabeledLineToolBase instead of DrawingToolBase. The label is a property of the line, not a separate text drawing, so it moves, locks, copies, persists and syncs across charts with the line it names.

LabeledLine.cs
using SabrTrader.Pipeline.Drawing;

[DrawingToolMetadata("My Support Line",
    Category = "Lines", IconKey = "HorizontalLine", AnchorCount = 1,
    Description = "Horizontal line with an optional label.")]
public sealed class MySupportLine : LabeledLineToolBase
{
    public MySupportLine() : base(anchorCount: 1) { }

    // Only the anchor's Price positions this tool, so the coordinate editor hides Time.
    public override DrawingAnchorCoordinates AnchorCoordinates => DrawingAnchorCoordinates.PriceOnly;

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        double y  = ctx.Viewport.PriceToPixelY(Anchors[0].Price);
        double x1 = ctx.PlotArea.X;
        double x2 = ctx.PlotArea.Right;
        DrawLine(ctx, x1, y, x2, y, Color, LineWidthPx);

        // Everything about the label - measuring, aligning to LabelPosition / LabelSide,
        // keeping the glyphs inside the plot area - happens in the base.
        DrawLabel(ctx, x1, y, x2, y);
    }
}
You inherit the editable properties the trader expects: Text, LabelPosition (Start / Middle / End), LabelSide (Above / Below), LabelFontSizePx, LabelColor, LabelBold and LabelItalic, all grouped under a "Label" category in the properties dialog. An empty Text renders nothing, so a line that was never named costs no pixels.

A text box that wraps
A note tool combines two small interfaces. IDrawingToolTextContent opts the tool into the chart's inline editor: placing it drops a caret on the chart and double-clicking it re-opens the caret. Its WrapWidthPx tells that caret where your text wraps. IDrawingToolWidthGrip gives the box a grip on its right edge that the trader drags to set the width, in screen pixels, so zooming the chart never re-wraps the note.

The chart asks TryHitWidthGrip only while the drawing is selected and unlocked. You return true when the pointer is on the grip and hand back originXPx, the pixel X the width is measured from (here the text's left edge). While the trader drags, the chart shows a left-right resize cursor and calls ResizeFromGrip with the pointer's distance right of that origin, and you turn it into a width.

Note.cs
using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Drawing;

[DrawingToolMetadata("Note", Category = "Text", AnchorCount = 1,
    Description = "Text in a box whose width you drag; the text wraps inside it.")]
public sealed class Note : DrawingToolBase, IDrawingToolTextContent, IDrawingToolWidthGrip
{
    private const double Padding = 4;
    private static readonly string[] LineBreaks = { "\r\n", "\n", "\r" };
    private string _text = "Note";
    private double _wrapWidthPx;

    public Note() : base(anchorCount: 1) { }

    [DrawingToolProperty(DisplayName = "Text", Category = "Content", Order = 5, Multiline = true)]
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    [DrawingToolProperty(DisplayName = "Font Size (px)", Category = "Style", Order = 35,
        MinValue = 6.0, MaxValue = 96.0, Step = 1.0)]
    public double FontSizePx { get; set; } = 14;

    public bool Bold => false;
    public bool Italic => false;

    // 0 = no wrapping. Tagged, so it is editable and saved with the drawing.
    [DrawingToolProperty(DisplayName = "Box Width (px)", Category = "Box", Order = 80,
        MinValue = 0.0, MaxValue = 2000.0, Step = 10.0)]
    public double WrapWidthPx
    {
        get => _wrapWidthPx;
        set => _wrapWidthPx = value > 0 ? Math.Clamp(value, 20, 2000) : 0;
    }

    private double LinePitch => FontSizePx * 1.25;

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        var (x, y) = ctx.AnchorToPixel(Anchors[0]);   // baseline-left of the first line
        var lines = Wrap(ctx.Renderer);
        for (int i = 0; i < lines.Count; i++)
            ctx.Renderer.DrawText(x, y + i * LinePitch, lines[i], Color, FontSizePx);

        if (ctx.IsSelected)
        {
            ctx.DrawHandle(x, y);
            var (gx, gy) = GripPoint(ctx.Renderer, lines, x, y);
            ctx.DrawHandle(gx, gy);
        }
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        var (x, y) = ctx.AnchorToPixel(Anchors[0]);
        var lines = Wrap(ctx.Renderer);
        double right = x + BoxWidth(ctx.Renderer, lines) + Padding;
        double top = y - FontSizePx - Padding;
        double bottom = y + (lines.Count - 1) * LinePitch + Padding;
        return px >= x - Padding - tol && px <= right + tol && py >= top - tol && py <= bottom + tol;
    }

    public bool TryHitWidthGrip(double pixelX, double pixelY, IDrawingToolRenderContext ctx,
        out double originXPx, double tolerancePx = 8)
    {
        var (x, y) = ctx.AnchorToPixel(Anchors[0]);
        var (gx, gy) = GripPoint(ctx.Renderer, Wrap(ctx.Renderer), x, y);
        originXPx = x;   // the width is measured from the text's left edge
        double dx = pixelX - gx, dy = pixelY - gy;
        return dx * dx + dy * dy <= tolerancePx * tolerancePx;
    }

    // The grip sits one padding right of the text, so the text gets the rest.
    public void ResizeFromGrip(double offsetFromOriginPx)
        => WrapWidthPx = Math.Max(offsetFromOriginPx - Padding, 20);

    private (double X, double Y) GripPoint(IChartRenderer r, List<string> lines, double x, double y)
        => (x + BoxWidth(r, lines) + Padding, y - FontSizePx * 0.4);

    private double BoxWidth(IChartRenderer r, List<string> lines)
    {
        if (WrapWidthPx > 0) return WrapWidthPx;
        double widest = 0;
        foreach (var line in lines) widest = Math.Max(widest, r.MeasureText(line, FontSizePx).WidthPx);
        return widest;
    }

    // Split on every line-break form, then wrap each paragraph between words.
    // A production tool measures once per change of text, width or font, not per frame.
    private List<string> Wrap(IChartRenderer r)
    {
        var result = new List<string>();
        foreach (var paragraph in _text.Split(LineBreaks, StringSplitOptions.None))
        {
            if (WrapWidthPx <= 0) { result.Add(paragraph); continue; }
            string line = "";
            foreach (var word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && r.MeasureText(candidate, FontSizePx).WidthPx > WrapWidthPx)
                {
                    result.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            result.Add(line);
        }
        return result;
    }
}
Because WrapWidthPx carries [DrawingToolProperty], the width the trader drags is saved with the drawing and also appears as a number in the properties dialog. The Text property uses Multiline = true, so the dialog's editor accepts line breaks just like the inline caret (Shift+Enter there). Both paths can store "\r\n", "\n" or "\r", which is why Wrap splits on all three.

The metadata attribute
[DrawingToolMetadata] is what makes the tool discoverable. The catalog reads it at assembly-scan time to populate the toolbar menu, hotkeys, icons and anchor counts, with no registration boilerplate. Exactly one attribute per tool class.

Field	Meaning	Default
Name (ctor arg, required)	Display name in the toolbar and context menus. Also becomes ToolName, and it is the key a saved drawing is matched back to its tool by, so it must be unique across all loaded tools and stay stable once shipped (see Discovery).	(none)
Category	Toolbar submenu group ("Lines", "Shapes", "Fibonacci", …).	"Lines"
Hotkey	Optional global hotkey string (e.g. "Alt+Shift+L") the toolbar keymap arms. Must not repeat a hotkey another tool binds.	null
IconKey	One of the toolbar's built-in icons (see Icons). A key that is not an icon shows the default icon and writes a line to the Plugins log.	null
IconPath	The tool's own icon as path data (see Icons). Takes precedence over IconKey.	null
AnchorCount	Number of clicks the user makes to finalize (the minimum for variable tools).	2
Order	Display order within the category; equal values fall back to alphabetical.	0
Description	Tooltip text in the toolbar.	null
Icons
A tool shows one of two icons on the toolbar, in its category menu and on the favorites bar.

A built-in icon. Set IconKey to the name of the built-in tool that uses it, without spaces or punctuation: Andrews Pitchfork is "AndrewsPitchfork", Fib Retracement is "FibRetracement". If the key is not an icon, the tool shows the default square and the Plugins log names the tool and, when the key only differs in spelling, the key you meant. The keys:

AbcdPattern, AbcdProjection, Action, AnchoredVwap, AndrewsPitchfork, Arc, Arrow, ArrowMarkDown, ArrowMarkUp, ArrowMarker, Brush, Circle, ClearDrawings, CrosshairGlobal, CrosshairHidden, CrosshairVisible, Curve, CyclicLines, CypherPattern, DateRange, Default, DoubleCurve, ElliottCorrection, ElliottDoubleCombo, ElliottImpulse, ElliottTriangle, ElliottTripleCombo, Ellipse, EyeHidden, EyeVisible, FibChannel, FibCircles, FibExtension, FibRetracement, FibSpeedArcs, FibSpeedFan, FibSpiral, FibTimeZone, FibTrendBasedTime, FibWedge, FixedVolumeProfile, Folder, GannBox, GannFan, GannSquare, GannSquareFixed, HeadAndShoulders, Highlighter, HorizontalChannel, HorizontalLine, HorizontalRay, IndicatorsHidden, IndicatorsVisible, LinearRegressionChannel, Path, Pin, Pitchfan, Plus, Polyline, PositionSizing, PriceRange, Ray, Rectangle, Redo, RotatedRectangle, Ruler, SineLine, Star, TapeRange, TextLabel, ThreeDrivesPattern, TimeCycles, TrendChannel, TrendLine, Triangle, TrianglePattern, Undo, Unpin, VerticalLine, XabcdPattern

Your own icon. Set IconPath to path data, the SVG / XAML path syntax, in a 24 x 24 box. It is drawn as a line in the theme's icon colour, the same as the built-in icons, so draw strokes, not filled shapes. Path data that does not parse shows the default icon.

AcmeFork.cs
[DrawingToolMetadata("Acme Fork", Category = "Lines", AnchorCount = 3,
    IconPath = "M 2,12 L 22,12 M 9,6 L 22,6 M 9,18 L 22,18 M 9,6 L 9,18")]
public sealed class AcmeFork : DrawingToolBase { /* ... */ }
Rebuild while the app runs. When you rebuild a drawing-tool plugin into the Plugins folder, every open chart picks it up: the toolbar lists the new or changed tool and its icon, its hotkey works, and the next drawing you place runs the new code. Drawings already on a chart keep the build they were made with until the chart is reloaded.
AnchorCount appears twice: keep them consistent. The metadata's AnchorCount drives the placement gesture; the base constructor's anchorCount pre-allocates the storage. For a fixed-anchor tool they must match (both 2 here). The catalog requires a public parameterless constructor, which the trend line satisfies.
Reading anchors. Because the constructor pre-allocates two slots, both Anchors[0] and Anchors[1] are safe to read in OnRender from the very first frame (they are zero-valued until the user clicks, and the host doesn't render a tool until enough anchors are placed). Write anchors only through SetAnchor; never mutate Anchors directly, since it is exposed as read-only.
