Add-ons
Windows & views
Declare a window kind, describe its contents from panel elements, and keep it live without touching a UI framework.

On this page
Declaring a window
The window itself
Describing the contents
The element kinds
Keeping it live
Threading
Opening and closing from code
Declaring a window
A window kind is a registration: an id, a factory, and how the user reaches it. You declare it once, in Initialize, and the host calls the factory whenever the window needs to exist.

declaring a window kind
new AddOnWindowKind("main", () => new ScannerWindow(host))
{
    MenuTitle            = "Volume Scanner",   // menu label; defaults to the window's Title
    Description          = "Unusual volume across your watchlist.",
    MenuSection          = AddOnMenuSection.New,
    RestoreWithWorkspace = true,               // the default
}
The Id is stable identity again: a saved workspace references it, so renaming it in a later release loses the window from workspaces people already have.

One window per kind. The host keeps at most one open instance of each kind. Opening a kind that is already open brings the existing window to the front. If you genuinely need two windows, declare two kinds.
The window itself
IAddOnWindow is small: a caption, the view, an opening size, and two lifecycle hooks you can ignore until you need them.

ScannerWindow.cs
using SabrTrader.Pipeline.AddOns;
using SabrTrader.Pipeline.Panels;

public sealed class ScannerWindow : IAddOnWindow
{
    private readonly IAddOnHost _host;
    private readonly ChartPanelLabel _status = new("Status", "Idle");

    public ScannerWindow(IAddOnHost host) => _host = host;

    public string Title => "Volume Scanner";

    public int DefaultWidth  => 480;      // used when no workspace supplies a size
    public int DefaultHeight => 560;

    public AddOnView CreateView() => new(
        new ChartPanelRow(new ChartPanelButton("Scan", Scan)),
        new ChartPanelSeparator(),
        _status);

    public void OnOpened()  => _status.Text = "Ready.";
    public void OnClosed()  { /* release subscriptions and timers here */ }

    private void Scan() => _status.Text = "Scanning...";
}
The host builds the window, calls CreateView once, shows it, then calls OnOpened. Start subscriptions and background work in OnOpened rather than the constructor, so nothing is left running for a window that failed to open. When the window closes, OnClosed runs and the instance is never reused: the host asks the kind for a fresh one next time.

Describing the contents
Instead of building a control tree, you declare an AddOnView from panel elements and the host renders it in the user's theme, on the platform's own window chrome. It is the same vocabulary a plugin uses for a chart trader tab, so there is one set of controls to learn.

Because the view is a declaration rather than WPF or Avalonia code:

your add-on references no UI framework, and one build runs in every host the platform ships;
your code stays off the render thread, so a slow add-on cannot wedge the platform;
your window matches the user's theme, including a theme they switch to while it is open;
you never write a dispatcher, a binding or a XAML file.
The structure is fixed at construction and the visual tree is built once. What each element shows is free to change at any time, from any thread. So the view is never rebuilt, and the user never loses focus, scroll position or a half-typed value because a background update landed.

The element kinds
Element	What it is
ChartPanelButton	A push button. Its callback runs when the user presses it.
ChartPanelToggle	A two-state button that stays pressed while it is on: Run / Pause, Armed.
ChartPanelCheckBox	A labelled checkbox, for an option the user ticks rather than a button they press.
ChartPanelChoice	One of a fixed set of options.
ChartPanelNumber	A numeric field. The host clamps and rounds every edit to your declared range before your callback sees it, and the mouse wheel steps it.
ChartPanelText	A single-line text field. You get the raw string and may write a normalised value back.
ChartPanelLabel	A read-only caption and value. This is where a live status belongs.
ChartPanelRow	Lays its children side by side on one line, sharing the width evenly.
ChartPanelGroup	A captioned block, for when a window grows past a handful of controls.
ChartPanelSeparator	A horizontal rule between two stretches of the view.
Every element carries IsEnabled, IsVisible and an Accent. The accent is a semantic role, not a colour: Neutral, Positive, Negative, Warning, Info. The host resolves it against whichever theme the user is running.

a fuller view
public AddOnView CreateView() => new(
    new ChartPanelGroup("Filter",
        new ChartPanelText("Symbols", v => _symbols = v, text: "ES, NQ, CL"),
        new ChartPanelNumber("Min volume", v => _minVolume = (long)v,
            value: 5000, minimum: 1, maximum: 10_000_000, step: 500),
        new ChartPanelCheckBox("Regular hours only", on => _rthOnly = on, isChecked: true)),
    new ChartPanelSeparator(),
    new ChartPanelRow(
        new ChartPanelButton("Scan", Scan),
        new ChartPanelButton("Clear", Clear)),
    _hits,
    _status);
Keeping it live
Elements are live objects, not a snapshot you re-emit. Write to one and the window follows:

updating the window
_hits.Text     = found.Count.ToString();
_hits.Accent   = found.Count > 0 ? ChartPanelAccent.Warning : ChartPanelAccent.Neutral;
_status.Text   = $"Scanned {scanned} symbols at {DateTime.Now:HH:mm:ss}";
_scanButton.IsEnabled = !_running;
A write costs nothing: the host marks that one element dirty and refreshes it on its next UI pass. A burst of a thousand writes collapses into one repaint showing the newest value, so a per-tick readout is fine.

Keep the elements you intend to update as fields, as in the examples above. That is the natural way to write a window, and it is why CreateView reads as a layout declaration rather than a wiring pass.

Threading
Element callbacks run on your window's own UI thread, the same thread as Title, CreateView, OnOpened and OnClosed. So a button handler can read and write your window's fields directly, with no locking.

Writing to elements works from anywhere. A background worker, a tick callback or a timer can update a readout with no dispatcher of its own: the host marshals and coalesces for you.

A handler that throws is contained. The failure is logged against your add-on and the window keeps working.

Opening and closing from code
host.Windows is your handle on your own windows, scoped to your add-on: every id is one you declared, and one add-on can neither see nor touch another's windows.

IAddOnWindowService
host.Windows.Open("main");        // opens, or brings the open one to the front
host.Windows.IsOpen("main");     // true while it is open
host.Windows.Close("main");      // closes it, exactly as the user would
All three are safe to call from any thread. Open returns as soon as the request is queued and the window appears shortly after, which is why it does not hand the window back. An id you never declared is a bug in the add-on, so it throws rather than doing nothing quietly.

