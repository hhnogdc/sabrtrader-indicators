Reference
Add-ons
The add-on SDK surface: the plugin export and its manifest, window kinds and windows, workspace state, the host services, and the declared view. Narrative: Add-ons.

On this page
The add-on
Contributions
Windows
Host services
The view
The add-on: SabrTrader.Pipeline.AddOns
One plugin DLL exports IAddOn. Narrative: Add-ons.

Type	Members
IAddOn	AddOnManifest Manifest { get; } (pure, read at discovery); AddOnContributions Initialize(IAddOnHost host) (once, on the UI thread); void Shutdown() (default: nothing).
AddOnManifest	record; constructor (string id, string name, string vendor, string version), read-only Id, Name, Vendor, Version; string? Description { get; init; }. Every value is required and trimmed; Id roots the settings folder and keys workspace entries.
Contributions
Type	Members
AddOnContributions	(IReadOnlyList<AddOnWindowKind>? windows = null, IReadOnlyList<AddOnCommand>? commands = null); Windows; Commands; static None for a background-only add-on. Both lists are non-null after construction.
AddOnCommand	(string id, string title, Action invoke); Id; Title; string? Description { get; init; }; AddOnMenuSection MenuSection { get; init; } (default Settings); void Invoke().
AddOnMenuSection	enum None, New, Settings.
Windows
Type	Members
AddOnWindowKind	(string id, Func<IAddOnWindow> create); Id; Create; string? MenuTitle { get; init; } (defaults to the window's Title); string? Description { get; init; }; AddOnMenuSection MenuSection { get; init; } (default None); bool RestoreWithWorkspace { get; init; } (default true).
IAddOnWindow	string Title { get; }; AddOnView CreateView() (called once); int DefaultWidth { get; } (default 520); int DefaultHeight { get; } (default 680); void OnOpened(); void OnClosed(). Every member is called on the window's own UI thread.
IAddOnWindowState<TState>	where TState : class. TState Save(); void Restore(TState state), called after CreateView and before OnOpened. The host serialises TState into the workspace.
IAddOnWindowService	void Open(string windowId) (opens, or activates the open one); void Close(string windowId); bool IsOpen(string windowId). Scoped to the owning add-on; safe from any thread.
Host services
Type	Members
IAddOnHost	IAddOnWindowService Windows; IAddOnLog Log; IAddOnStorage Storage; ITradingService? Trading; IHistoricalDataService? History; ITickSource? Ticks; IInstrumentDirectory? Instruments; INewsHub? News (News; headline callbacks on the subscription's own pool thread). A null member means this installation does not provide that service.
IAddOnLog	void Info(string); void Warning(string); void Error(string message, Exception? exception = null). Filed under the add-on's name; safe from any thread.
IAddOnStorage	TValue? Load<TValue>(string key); void Save<TValue>(string key, TValue value); void Delete(string key), all where TValue : class. Rooted at the add-on's manifest id; Save writes through to disk.
The view: SabrTrader.Pipeline.Panels
An add-on window's body is a PanelSurface, built from the same elements as a chart trader tab. Narrative: Windows & views; element members: Reference › Indicators.

Type	Members
AddOnView	AddOnView(params ChartPanelElement[] elements), deriving PanelSurface. The element tree is fixed at construction.
PanelSurface	IReadOnlyList<ChartPanelElement> Elements { get; }; event Action<ChartPanelElement>? ElementChanged (the host subscribes); void Release() (the host calls it once the view has left the screen; it frees the elements so the add-on can reuse the same instances in its next view).
Elements	ChartPanelButton, ChartPanelToggle, ChartPanelCheckBox, ChartPanelChoice, ChartPanelNumber, ChartPanelText, ChartPanelLabel, ChartPanelRow, ChartPanelGroup, ChartPanelSeparator. Each carries IsEnabled, IsVisible and Accent, and may be written from any thread. The grouping ones derive from ChartPanelContainer, an abstract ChartPanelElement exposing IReadOnlyList<ChartPanelElement> Children. The host renders exactly these element kinds; build views from them rather than from your own subclasses.
