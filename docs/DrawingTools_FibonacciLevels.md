Drawing tools
Fibonacci levels
A convention-driven way to build Fibonacci tools: expose one level-set property and get user-editable ratios, colours and visibility for free, with ready-made default sets to start from.

ADVANCED What it is
Fibonacci tools follow a convention rather than a special base class. You expose a single FibLevelSet Levels property (an ObservableCollection<FibLevel>), tag it with [DrawingToolProperty], and the editor picks it up reflectively. That one property gives the user per-level control over ratio, colour and visibility with no extra wiring.

The types live in SabrTrader.Pipeline.Drawing.Tools and are deliberately small. A FibLevel is a ratio, an optional per-level colour (where null means "inherit the tool's stroke colour"), and a visibility flag. A FibLevelSet is an observable collection of them, so the properties dialog's list editor binds to it directly.

FibLevels.cs
namespace SabrTrader.Pipeline.Drawing.Tools;

public sealed class FibLevel
{
    public double Ratio { get; set; }            // applied to the tool's anchor range
    public ChartColor? Color { get; set; }        // null = inherit the tool's Color
    public bool IsVisible { get; set; } = true;

    public FibLevel() { }
    public FibLevel(double ratio, ChartColor? color = null, bool visible = true);
    public FibLevel Clone();
}

public sealed class FibLevelSet : ObservableCollection<FibLevel>
{
    public FibLevelSet();
    public FibLevelSet(IEnumerable<FibLevel> items);
    public FibLevelSet Clone();                   // deep copy
    public void ReplaceWith(IEnumerable<FibLevel> other);
    public ReadOnlySpan<FibLevel> Snapshot { get; }   // render-thread view, see below
}
Ready-made level sets
You rarely build a level set by hand. The static FibLevels helper ships the standard ratio ladders and palettes, and factory methods that return a populated FibLevelSet. Some pair each ratio with a TradingView-style colour; others leave the colour null so every level inherits the tool's stroke colour.

FibLevels.cs
// Ratio ladders (raw arrays):
FibLevels.StandardRetracements;                // 0, .236, .382, .5, .618, .786, 1
FibLevels.StandardExtensions;                  // 1.272, 1.414, 1.618, 2, 2.618, 3.618, 4.236
FibLevels.StandardRetracementsAndExtensions;
FibLevels.StandardSpeedRatios;                 // .382, .5, .618
FibLevels.StandardTimeZones;                   // 1, 2, 3, 5, 8, 13, 21, 34, 55, 89
FibLevels.StandardGannBoxRatios;               // .25, .382, .5, .618, .75 - the Gann box inner grid
FibLevels.GannAngles;                          // (Price, Time) slope pairs for a Gann fan

// Factory methods returning a populated FibLevelSet:
FibLevels.DefaultRetracementSet();             // ladder + per-level colours
FibLevels.DefaultExtensionSet();
FibLevels.DefaultRetracementSetInheritColor(); // every level inherits the tool's Color
FibLevels.DefaultSpeedRatioSetInheritColor();
FibLevels.DefaultCircleRatioSetInheritColor();
FibLevels.DefaultGannBoxSetInheritColor();     // Gann box grid - use one set for price, one for time
The Gann box keeps two independent sets. Its price lines and its time lines are separate FibLevelSets, each with its own ratios, per-level visibility and colour, so a trader can show a price grid without a time grid (or give each a different ladder). Seed both from DefaultGannBoxSetInheritColor(): the box outline already marks 0 and 1, so StandardGannBoxRatios carries only the inner grid.
Rendering the levels
Read Snapshot on the render path. A FibLevelSet is an ObservableCollection the properties editor mutates on the UI thread, so iterating it while a trader edits a level can throw. set.Snapshot hands back the levels as of the last structural change as a ReadOnlySpan<FibLevel>, safe from any thread and allocation-free to walk. Enumerate the collection itself only in editor code.
In OnRender you derive each level's price from the two anchors (the ratio interpolates between anchor 0's price and anchor 1's price), convert it to a pixel Y with PriceToPixelY, and draw a horizontal line out to the plot area's right edge. Each level uses its own colour, falling back to the tool's Color when none is set.

AcmeFibRetracement.cs
using System;
using SabrTrader.Pipeline.Drawing;
using SabrTrader.Pipeline.Drawing.Tools;
using SabrTrader.Pipeline.Plots;

[DrawingToolMetadata("Acme Fib Retracement", Category = "Fibonacci", AnchorCount = 2)]
public sealed class AcmeFibRetracement : DrawingToolBase
{
    public AcmeFibRetracement() : base(anchorCount: 2) { }

    [DrawingToolProperty(DisplayName = "Levels", Category = "Levels", Order = 10,
        Description = "Configurable ratios with per-level visibility and colour.")]
    public FibLevelSet Levels { get; set; } = FibLevels.DefaultRetracementSet();

    public override void OnRender(IDrawingToolRenderContext ctx)
    {
        double p0 = Anchors[0].Price, p1 = Anchors[1].Price;
        double xL = Math.Min(ctx.AnchorToPixel(Anchors[0]).X, ctx.AnchorToPixel(Anchors[1]).X);
        double xR = ctx.PlotArea.Right;

        foreach (var lvl in Levels.Snapshot)                  // never the live collection here
        {
            if (!lvl.IsVisible) continue;
            double price = p0 + lvl.Ratio * (p1 - p0);
            double y = ctx.Viewport.PriceToPixelY(price);
            ChartColor stroke = lvl.Color ?? Color;            // per-level, else tool colour
            DrawLine(ctx, xL, y, xR, y, stroke, LineWidthPx);   // keeps the tool's LineStyle
            ctx.Renderer.DrawText(xL + 4, y - 4, $"{lvl.Ratio:0.000} ({price:0.##})", stroke, 11);
        }

        if (ctx.IsSelected)
        {
            ctx.DrawHandle(ctx.AnchorToPixel(Anchors[0]).X, ctx.AnchorToPixel(Anchors[0]).Y);
            ctx.DrawHandle(ctx.AnchorToPixel(Anchors[1]).X, ctx.AnchorToPixel(Anchors[1]).Y);
        }
    }

    public override bool HitTest(double px, double py, IDrawingToolRenderContext ctx, double tol = 6)
    {
        double p0 = Anchors[0].Price, p1 = Anchors[1].Price;
        double xL = Math.Min(ctx.AnchorToPixel(Anchors[0]).X, ctx.AnchorToPixel(Anchors[1]).X);
        double xR = ctx.PlotArea.Right;
        foreach (var lvl in Levels.Snapshot)
        {
            if (!lvl.IsVisible) continue;
            double price = p0 + lvl.Ratio * (p1 - p0);
            double y = ctx.Viewport.PriceToPixelY(price);
            if (px >= xL && px <= xR && DrawingGeometry.DistanceToHorizontalLine(py, y) <= tol)
                return true;
        }
        return false;
    }
}
Clone before you mutate. Both FibLevel and FibLevelSet offer a deep Clone(). FibLevelSet.ReplaceWith swaps in clones of the given levels in one step and raises a single Reset change notification, so a painting thread never sees the set half-empty and a bound editor rebuilds once. Editing the live set while you iterate it confuses the bound editor: clone, change the copy, then ReplaceWith.
