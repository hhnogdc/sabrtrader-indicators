Reference
News
The public surface of SabrTrader.Pipeline.News (in SabrTrader.Pipeline.Contracts) and the venue news port in SabrTrader.Pipeline.Venues.News (in SabrTrader.Pipeline.Venues.Contracts). Guide: News.

On this page
The hub
The script client
Filters & queries
Headlines & results
Stories & statuses
Source info
Supplying news from a venue plugin
The hub
The platform's one news service: past headlines, story text and live headlines from every connected source. Add-ons get it as IAddOnHost.News; other plugin code reads NewsAmbient.Hub.

public interface INewsHub
{
    IReadOnlyList<NewsSourceInfo> Sources { get; }              // snapshot; safe to enumerate
    event Action? SourcesChanged;                               // any thread
    IDisposable Subscribe(NewsFilter filter, Action<NewsHeadline> onHeadline);
    Task<NewsHeadlinesResult> GetHeadlinesAsync(NewsQuery query, CancellationToken ct);
    Task<NewsStoryResult> GetStoryAsync(NewsHeadline headline, CancellationToken ct);
    NewsFilter FilterForInstrument(string venue, string instrumentId);
}

public static class NewsAmbient
{
    public static INewsHub Hub { get; }                         // never null
    public static void SetHub(INewsHub? hub);                   // host: install (null restores the empty default)
    public static IDisposable Use(INewsHub hub);                // tests: scoped install
}

public sealed class NullNewsHub : INewsHub
{
    public static NullNewsHub Instance { get; }
}
Subscribe: live headlines matching the filter, in order, on the subscription's own pool thread, so a slow handler only delays itself. Works before any source is connected; sources join as they connect and rejoin after a reconnect. Dispose to stop.
GetHeadlinesAsync: past headlines from every source the query names, merged, newest first, duplicates removed, up to Limit. The hub pages each venue itself. Each source's outcome is in the result. Only cancellation throws.
GetStoryAsync: the story, whether the venue sent it with the headline, serves it by id, or has only the link (NotAvailable). Only cancellation throws.
FilterForInstrument: the filter for one instrument of one venue, with the news symbol worked out by that venue. instrumentId is the venue's own instrument id, as on BarSpecification.InstrumentId. An instrument nobody can resolve gives an empty symbol list, which matches nothing.
NullNewsHub: no sources, a subscription that never delivers, an empty result with no source outcomes for headlines, NotAvailable for a story, a filter that matches nothing for an instrument. NewsAmbient.Hub returns it until the host installs a hub.
The script client
How an indicator or strategy reads news, through its News property. The same three verbs as the hub, with callbacks instead of Tasks, because script code must never wait on its own thread. Guide: News.

public interface INewsClient
{
    bool IsAvailable { get; }
    IReadOnlyList<NewsSourceInfo> Sources { get; }
    NewsFilter ThisInstrument { get; }
    IDisposable Subscribe(NewsFilter filter, Action<NewsHeadline> onHeadline);
    bool RequestHeadlines(NewsQuery query, Action<NewsHeadlinesResult> onResult);
    bool RequestStory(NewsHeadline headline, Action<NewsStoryResult> onResult);
}

public static class NewsClient
{
    public static INewsClient None { get; }
}

// Where scripts and add-ons reach it
IIndicatorContext:  INewsClient News { get; }                   // default: NewsClient.None
IndicatorBase:      protected INewsClient News { get; }         // Context.News
IStrategyContext:   INewsClient News { get; }                   // default: NewsClient.None
Strategy:           protected INewsClient News { get; }         // Context.News
IAddOnHost:         INewsHub? News { get; }                     // default: null
Every method returns at once; none blocks and none touches the network.
Every handler runs on the script's own thread, never inside OnBarUpdate or OnBar. On a chart, with the chart's other callbacks on the next bar event; in a strategy, on the strategy's own thread.
Subscribe ends by itself when the script stops; dispose the result to end it sooner.
RequestHeadlines and RequestStory return false, and never call back, when news is not available here. Otherwise the handler runs exactly once, later.
NewsClient.None: IsAvailable false, no sources, ThisInstrument matches nothing, Subscribe never delivers, Request* return false. Backtests, the optimizer, the Strategy Analyzer and the Market Analyzer use it, so a past bar can never see a later headline.
Filters & queries
public sealed record NewsFilter(
    IReadOnlyList<string>? Sources = null,      // source keys; null = all, empty = none
    IReadOnlyList<string>? Providers = null,    // provider codes; null = all, empty = none
    IReadOnlyList<string>? Symbols = null)      // normalized symbols; null = all, empty = none
{
    public static NewsFilter All { get; }
}

public sealed record NewsQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    int Limit,                                  // over all sources together; at least 1
    IReadOnlyList<string>? Sources = null,      // null = every connected source, empty = none
    IReadOnlyList<string>? Providers = null,
    IReadOnlyList<string>? Symbols = null);

public static class NewsSymbols
{
    public static string? Normalize(string? symbol);   // trimmed, upper-case; null for null or blank
}
A headline matches a filter when it passes every list. Null means all; an empty list means none. To page further back, ask again with ToUtc set to the oldest headline received. There are no cursors for consumers. Keyword matching is left to each consumer.

Headlines & results
public sealed record NewsHeadline(
    NewsArticleId Id,
    DateTime TimeUtc,
    string Title,
    string? Summary,
    IReadOnlyList<string> Symbols,              // normalized; empty when untagged
    NewsProvider Provider,
    string SourceDisplayName,
    string? Url,
    NewsStory? InlineStory);                    // set only when the venue sends the body with the headline

public readonly record struct NewsArticleId(string SourceKey, string VenueArticleId);

public sealed record NewsProvider(string Code, string DisplayName);

public sealed record NewsHeadlinesResult(
    IReadOnlyList<NewsHeadline> Items,          // newest first, duplicates removed, at most Limit
    IReadOnlyList<NewsSourceOutcome> Sources);  // one per source asked; empty when none was asked

public sealed record NewsSourceOutcome(string SourceKey, NewsStatus Status);
A headline is shared by reference between every consumer. Its identity is Id: compare by id, not by record equality, because the symbol list compares by reference. Two headlines with the same id are the same article, however they arrived. TimeUtc is the venue's publish time in UTC.

Stories & statuses
public sealed record NewsStory(string Body, NewsBodyFormat Format, string? Url);

public sealed record NewsStoryResult(NewsStatus Status, NewsStory? Story);   // Story set only when Ok

public enum NewsBodyFormat { Text = 0, Html = 1, Markdown = 2 }

public enum NewsStatus { Ok = 0, NotAvailable = 1, NotEntitled = 2, Offline = 3, Failed = 4 }
NewsStatus	Meaning
Ok	The request was served.
NotAvailable	Nothing to serve it from: no news hub, no matching source, or no story for the headline (link only).
NotEntitled	The venue refused because the account is not entitled to it.
Offline	The source is not connected.
Failed	The venue answered with an error, or the request could not be completed.
Html and Markdown bodies stay as the venue sent them; the host converts them for display.

Source info
public sealed record NewsSourceInfo(
    string SourceKey,
    string DisplayName,
    IReadOnlyList<NewsProvider> Providers,
    TimeSpan HistoryDepth,
    bool RequiresSymbols,
    int? MaxSymbolsPerQuery,
    TimeSpan PollInterval)
{
    public TimeSpan PollLookback { get; init; }
}
Field	Meaning
SourceKey	The venue type id. There is one active source per venue type, so this names the source in filters, queries and results.
DisplayName	The name shown to the user.
Providers	The providers this session may serve; empty when the source is not split by provider.
HistoryDepth	How far back past headlines can be asked for.
RequiresSymbols	True when past headlines can only be asked per symbol, so "all symbols" is not offered for this source.
MaxSymbolsPerQuery	The most symbols one request may name; null means no limit.
PollInterval	How often the platform may poll a source that has no live stream; ignored for a source that streams.
PollLookback	How long after its publish time an article can still first appear at the venue (a feed updated hourly files articles up to an hour late). Every poll and gap fill asks again from this far before the newest headline already seen, so a late article is still picked up; what comes back twice goes out once. Zero leaves the platform's short default overlap.
Complete when the source joins, and fixed for its session.

Supplying news from a venue plugin
A venue serves news by handing out up to three role interfaces from its session. They live in SabrTrader.Pipeline.Venues.News, in SabrTrader.Pipeline.Venues.Contracts. Implement only the roles your venue really has. Venue plugin guide: The session.

namespace SabrTrader.Pipeline.Venues.News;

public interface INewsSource                    // required: past headlines
{
    NewsSourceInfo Info { get; }
    Task<NewsSourcePage> GetHeadlinesAsync(NewsSourceQuery query, CancellationToken ct);
}

public interface INewsLiveSource                // optional: a live stream
{
    void SetInterest(NewsLiveInterest interest);
    event Action<NewsHeadline>? Received;       // any thread; must not block
}

public interface INewsStorySource               // optional: story text by article id
{
    Task<NewsStoryResult> GetStoryAsync(string venueArticleId, CancellationToken ct);
}
public sealed record NewsSourceQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    int Limit,                                  // the most headlines wanted on this page
    IReadOnlyList<string>? Symbols = null,      // normalized; null = all
    IReadOnlyList<string>? Providers = null,    // null = all
    string? Cursor = null);                     // the previous page's NextCursor; null for the first page

public sealed record NewsSourcePage(
    NewsStatus Status,                          // not Ok: Items is empty
    IReadOnlyList<NewsHeadline> Items,          // newest first
    string? NextCursor = null);                 // the venue's token for the next, older page; null = last page

public sealed class NewsLiveInterest : IEquatable<NewsLiveInterest>
{
    public static NewsLiveInterest None { get; }        // stream nothing
    public static NewsLiveInterest All { get; }         // stream everything the session may serve
    public static NewsLiveInterest ForSymbols(IEnumerable<string> symbols);
    public bool IsAll { get; }
    public bool IsNone { get; }
    public IReadOnlySet<string> Symbols { get; }        // normalized; empty for All and None
    public bool Equals(NewsLiveInterest? other);        // value equality, also == and !=
    public override int GetHashCode();
}
The roles
Role	When to implement it
INewsSource	Required. Past headlines, one page per call. The platform also uses it to fill the gap after a reconnect, and polls it every PollInterval when there is no live stream.
INewsLiveSource	Optional, next to INewsSource, not instead of it. Only when the venue really streams news.
INewsStorySource	Optional. Only when the venue serves story text by article id. Without it, put the body on NewsHeadline.InlineStory when the venue sends it with the headline, or leave it null when there is only the link.
Handing out the roles
There is no manifest flag and no capability to set. When a session connects, the platform asks it for INewsSource through AsPort<T>(), then for the two optional roles. A session without INewsSource has no news. Implement the roles on the session itself, or return a separate object from GetPort<T>():

C#
// In your venue session. The roles you return are the news your venue offers.
public T? GetPort<T>() where T : class
    => _news as T;          // created in ConnectAsync; null before connect and after disconnect
Info is complete when the session hands the port out (read the venue's provider list during your own connect), and fixed for the session.
SourceKey is your venue type id. When two sessions of the same venue type are connected, the first one serves and the other waits; when the serving one leaves, the next takes over and the platform fills the gap.
A source without INewsLiveSource needs a positive PollInterval. Without one it is not registered, and the platform writes a log line. Registration problems never fail the connect.
After your session raises StreamsRestored with VenueStreams.MarketData, the platform sends the live interest again and fills the gap from GetHeadlinesAsync.
Rules for every venue
Time: every timestamp is UTC, converted at your edge.
Symbols: pass every symbol you receive through NewsSymbols.Normalize, and skip a tag it returns null for. The platform compares plain ordinal strings.
Story text keeps the venue's own format (NewsBodyFormat.Text, Html or Markdown); the host converts it for display.
Failures: an expected failure (offline, not entitled, a venue error) comes back as a NewsStatus on the page or story result, never as an exception. Only cancellation throws. A denied entitlement also posts the VenueNoticeCodes.EntitlementDenied notice through VenueSessionContext.Notices, once per session.
Rate limits and page sizes are enforced in your venue, as the venue's official documentation sets them.
Concurrency: the platform may call GetHeadlinesAsync concurrently (a window load, a poll and a gap fill at once), and GetStoryAsync alongside it. Your venue serialises or limits its own calls. The platform never queues one venue behind another.
Live: Received may be raised on any thread. A handler must not block, and you must not hold a lock the platform could wait on while you raise it.
Past headlines
Return one page, newest first. Clamp the query to what you serve (history depth, page size, symbol count), and page with your own NextCursor. The cursor stays between the platform and your venue; it never reaches a consumer.
The platform splits a long symbol list by MaxSymbolsPerQuery, asks the parts at the same time, and follows your cursor until it has enough headlines or you return no cursor.
The platform applies the query's provider and symbol filter again to what you return, so a venue that cannot filter on a field may return more.
When RequiresSymbols is true, the platform never sends you a query without symbols.
Live interest
SetInterest is called only when the interest changes. NewsLiveInterest has value equality: two interests with the same symbols are equal, whatever their order or spelling.
None means stop streaming. All means everything the session may serve. ForSymbols normalizes each symbol and skips blanks; no symbol left gives None.
A venue that can only stream everything streams everything. The platform filters per subscriber.
The rules above (UTC, normalized symbols, the venue's own story format) apply to every headline you raise.
