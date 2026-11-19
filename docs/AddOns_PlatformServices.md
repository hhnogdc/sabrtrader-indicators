Add-ons
Platform services
What IAddOnHost gives you: accounts and orders, historical bars, the live tape, instruments and the activity log.

On this page
What the host hands you
Reading the optional members
Accounts, positions and orders
Historical bars and ticks
The live tape
Instruments
The activity log
What the host hands you
IAddOnHost arrives in Initialize and is valid until Shutdown. Everything on it is scoped to your add-on: your windows, your log category, your settings folder.

Member	What it gives you
Windows	IAddOnWindowService: open, close and query your own windows.
Log	IAddOnLog: the platform activity log, filed under your add-on's name.
Storage	IAddOnStorage: your private typed settings store.
Trading	ITradingService?: accounts, positions, working orders, and order placement across every connected broker.
History	IHistoricalDataService?: historical bars and ticks from the platform cache and the connected venues.
Ticks	ITickSource?: live ticks from whichever venues are connected, as one source.
Instruments	IInstrumentDirectory?: the instruments the connected venues can serve.
News	INewsHub?: live and past headlines and story text from every connected news source. Headline callbacks arrive on your subscription's own pool thread, so a slow add-on only delays itself. See News and Reference › News.
These are the platform's own contracts, not add-on-specific wrappers. You read accounts through exactly the ITradingService a strategy and a venue plugin use, so the behaviour you learn in one chapter carries into this one, and the Reference › Trading pages apply here unchanged.

Reading the optional members
Four members are nullable, and null means "this installation does not provide it". The full desktop application provides all of them. A cut-down host may not. Check rather than assume, and say so in your window instead of throwing at startup, so an add-on that needs trading stays usable on a data-only install.

handling a missing service
var trading = _host.Trading;
if (trading is null)
{
    _status.Text   = "This installation has no trading connection.";
    _status.Accent = ChartPanelAccent.Warning;
    return;
}
Accounts, positions and orders
ITradingService aggregates every connected broker. Accounts, Positions and OpenOrders are live snapshot lists you can read at any time, and EventEmitted fires as things change.

reading account state
var trading = _host.Trading;
if (trading is null) return;

foreach (var account in trading.Accounts)
    _host.Log.Info($"{account.DisplayName}: {account.CashValue:N2} {account.Currency}");

foreach (var position in trading.Positions)
{
    if (position.Quantity == 0) continue;
    _host.Log.Info($"{position.Instrument} {position.Quantity} @ {position.AverageEntryPrice}");
}
An add-on can place orders. PlaceAsync, CancelAsync, ModifyAsync and FlattenAsync are all reachable from here, and they move real money on a real account. That is the same responsibility a strategy carries. See Orders & positions for the semantics before you write to this surface. Use the account, position and order members only: AddProvider, RemoveProvider, BeginDetach, Post and Dispose are the host's broker wiring, and an add-on must not call them.
Historical bars and ticks
IHistoricalDataService serves bars and ticks from the platform cache, filling gaps from the connected venue. It is the same service the charts load through, so your add-on and a chart on the same instrument see the same data.

loading history
var history = _host.History;
if (history is null) return;

// The spec carries the venue-native instrument id; venueKey picks the connection.
var spec = new TimeBarSpec("ES 03-27", TimeSpan.FromMinutes(5));

var result = await history.LoadAsync(
    venueKey: "rithmic",
    spec: spec,
    fromUtc: DateTime.UtcNow.AddDays(-5),
    toUtcExclusive: DateTime.UtcNow,
    cancellationToken: ct);

long volume = 0;
foreach (var bar in result.Bars)
    volume += bar.Volume;
LoadAsync returns a LoadResult: the bars, plus a Summary carrying the venue, instrument, window and bar count it actually served. See Reference › Bars & series for the bar and spec types.

The live tape
Ticks is one ITickSource over every connected market-data venue. Subscribe once, at start-up, even when nothing is connected yet: the subscription is queued and starts delivering the moment a venue arrives. It survives reconnects and venue swaps too, so you subscribe once and never rewire.

subscribing to live ticks
private IDisposable? _tape;

public void OnOpened()
{
    _tape = _host.Ticks?.Subscribe("ES 03-27", OnTick);
}

public void OnClosed()
{
    _tape?.Dispose();               // always unsubscribe when the window goes
    _tape = null;
}

private void OnTick(Tick tick)
{
    // Feed thread. Update your own state and write to view elements; do nothing heavy.
    _last.Text = tick.Price.ToString("F2");
}
Tick callbacks arrive on the feed thread. Do no work there beyond updating your own state and writing to elements. Writing to an element from a feed thread is safe and cheap, which is exactly why a live readout needs no dispatcher. To stop, dispose the handle Subscribe returned, never the shared ITickSource itself.
Instruments
IInstrumentDirectory.ListInstruments() gives you the instrument ids the connected venues can serve, which is what you want to populate a picker or validate a symbol the user typed before you subscribe to it.

The activity log
Log writes to the platform activity log, the one the user reads in the app and the one a support bundle collects. Every line is filed under your add-on's name, so nobody has to guess whose message they are looking at.

IAddOnLog
_host.Log.Info("Scan complete: 3 hits.");
_host.Log.Warning("Symbol CL 03-27 has no data on any connected venue.");
_host.Log.Error("Could not reach the pricing API", ex);
It is safe from any thread. This log is user-visible and travels with support bundles, so never log secrets, tokens or API keys.

