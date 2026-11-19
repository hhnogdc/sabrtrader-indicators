Add-ons
Settings & workspace state
The two kinds of state an add-on keeps, which one you want, and how each survives a restart.

On this page
Two kinds of state
Workspace state
When a workspace is restored
Settings
Keeping the shape stable
Two kinds of state
An add-on has two places to keep things, and picking the right one is mostly a question of what the user expects to happen when they switch desks.

Kind	Use it for
Workspace state
IAddOnWindowState<T>	Anything that belongs to this window on this desk: the symbol it was watching, the filter that was applied, the tab that was selected. It travels with the workspace, so a different desk gets different values.
Settings
IAddOnStorage	Anything that belongs to the user: a server address, a default threshold, a column layout they set once. It follows them across every workspace.
Workspace state
Implement IAddOnWindowState<TState> alongside IAddOnWindow and your window comes back with the workspace that was saved while it was open, in the same position and size, showing what it was showing. Declare a plain record or class and you write no serialisation code at all: the host serialises it into the workspace file and hands it back.

ScannerWindow.cs (workspace state)
public sealed record ScannerState(string Symbols, long MinVolume);

public sealed class ScannerWindow : IAddOnWindow, IAddOnWindowState<ScannerState>
{
    private string _symbols = "ES, NQ";
    private long _minVolume = 5000;

    public ScannerState Save() => new(_symbols, _minVolume);

    public void Restore(ScannerState state)
    {
        _symbols   = state.Symbols;
        _minVolume = state.MinVolume;
    }

    // ... Title, CreateView, OnOpened as usual
}
Set RestoreWithWorkspace = false on the window kind for a window that should only ever open on demand, like a detail view you pop up from another window.

When a workspace is restored
The order matters, and it is the order you would want. The host builds your window, calls CreateView, calls Restore, and only then shows the window and calls OnOpened. So by the time your subscriptions start, the restored values are already in place and you can subscribe to the right symbol first time.

A workspace outlives the add-ons that were installed when it was saved. If your add-on is not installed any more, or a later release dropped that window kind, the entry is reported in the activity log and skipped. The rest of the desk restores normally.

Settings
IAddOnStorage is a small typed store, rooted at your manifest id, so two add-ons can use the same key without colliding and neither can read the other's data.

IAddOnStorage
public sealed class ScannerSettings
{
    public string Endpoint    { get; set; } = "https://api.example.com";
    public int    PollSeconds { get; set; } = 30;
}

var settings = _host.Storage.Load<ScannerSettings>("connection") ?? new ScannerSettings();
settings.PollSeconds = 15;
_host.Storage.Save("connection", settings);

_host.Storage.Delete("connection");     // forget it entirely
Load returns null when nothing was ever saved, so supply your own default rather than expecting the store to guess one. Save writes through to disk before it returns, so a value saved during shutdown is not lost. All three are safe from any thread.

Keep values small. Every save writes the whole value, so this is a settings store, not a database. If you have a lot of data, keep your own file and store only the path. Values are stored as plain JSON on the user's disk, so keep credentials out of it.

A key becomes a file name, so characters a file name cannot carry (a path separator, a colon) are replaced. Two keys differing only in those characters therefore name one value. Plain keys like "prefs" or "last-scan" keep their identity exactly, and nothing an add-on passes can reach outside its own folder.

Keeping the shape stable
Both stores round-trip your type through JSON, so the shape you declare is a compatibility surface. A property you add later reads back as its default when an older workspace or an older settings file is opened, which is usually what you want. A property you rename reads back as its default too, which usually is not, so treat renames the way you would treat a database column.

