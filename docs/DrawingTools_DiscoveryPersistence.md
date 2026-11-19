Drawing tools
Discovery & persistence
How a tool goes from a DLL on disk to an entry in the drawing toolbar, and what state the host saves so a placed annotation comes back exactly as the trader left it.

Discovery: the catalog
There is no manual registration. When the host scans your plugin assembly it picks up every non-abstract class that implements IDrawingTool (in practice, derives from DrawingToolBase), carries a [DrawingToolMetadata] attribute, and has a public parameterless constructor. A class with the attribute but no parameterless constructor is left out, and the platform logs a line naming it in the Plugins category of the Control Center Log tab. The other tools, yours included, load normally.

For each tool found, the host records a catalog entry: the tool type, the [DrawingToolMetadata] fields (Name, Category, Hotkey, IconKey, AnchorCount, Order, Description), and a way to construct a fresh instance through your parameterless constructor. The drawing toolbar, hotkeys and the loader of saved drawings all read this catalog.

Your tools appear in the toolbar once the host's plugin scan has loaded your assembly. The scan tolerates a partially loadable assembly: the types that do load are still discovered, so one broken dependency in an unrelated plugin leaves the rest of the toolbar intact.

The metadata Name is the tool's identity. The catalog indexes tools by Name and by Hotkey (both case-insensitive) across every loaded assembly, built-ins included, and a saved drawing is matched back to its tool by Name. Pick a name and a hotkey that no built-in or other plugin uses (a vendor prefix such as "Acme Trend Line" works well). Built-in tools are scanned first and the first tool to claim a name or hotkey keeps it: a plugin tool whose name is already taken is left out, and one whose hotkey is taken loads without a hotkey. Both cases are logged in the Plugins category with the tool's type name. Keep the name stable once you ship, because renaming it leaves saved drawings of the tool without a match.
Category ordering
The toolbar sorts by a category rank first, then by Order, then by name. The built-in categories rank in this order: Lines, Measures, Text, Arrows, Shapes, Brushes, Anchored, Fibonacci, Gann, Chart Patterns, Elliott Waves, Cycles. Use one of those names (matched case-insensitively) to put your tool next to the built-ins. A category your plugin introduces sorts after the built-ins, alphabetically among other plugin categories, so a plugin category lands in a predictable place without touching the host.

What the host saves
The host saves each placed drawing with the workspace (and, for an AllCharts drawing, in the shared store that keeps every chart of the symbol in sync). You write no serialization code. A saved drawing records:

State	Notes
Tool type	Saved as the tool's metadata Name and matched against the discovered tools by that name on load. A drawing whose tool is not installed is skipped, and the rest of the workspace still loads.
Id	Restored on load (that is why DrawingToolBase.Id has a setter), so a drawing keeps its identity across sessions and charts.
PanelId	The ChartPanelKey, so a drawing on a subpanel or on a secondary series returns there.
Anchors	(Time, Price) pairs (see the overview), which is what lets a restored tool land on the right place even after the bar grid has changed underneath it. A fixed-anchor tool gets them through SetAnchor; a variable-anchor tool through AddAnchor, up to its MaxAnchorCount.
Style and behaviour	Color, LineWidthPx, LineStyle, Scope, IsLocked, ZOrder.
Visibility	The per-timeframe rules (see Per-timeframe visibility). A drawing that shows everywhere stores nothing extra. Restored on tools that derive from DrawingToolBase, which is where the setter lives.
Your [DrawingToolProperty] values	Every annotated property of type bool, string, int, long, float, double, decimal, an enum (by member name), ChartColor, FibLevelSet or DrawingVisibility, keyed by the CLR property name. A saved name your tool no longer declares is ignored on load, and a value that does not convert to the property's type is skipped, so the rest of the drawing still loads.
Property names are the saved form. Values come back by CLR property name, so renaming a [DrawingToolProperty] property drops the saved value for drawings made before the rename (the tool then shows your default). Change DisplayName freely instead; it is display only. Restoring writes your properties after the constructor and anchors, so a setter that validates or clamps runs on load too.
Templates. A drawing template (the trader's saved defaults for a tool) copies the same annotated properties, including Visibility, and leaves out IsLocked, since a lock belongs to one drawing and is not style.
