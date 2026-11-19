Reference
Market Analyzer
The surface for a custom Market Analyzer column: the column definition and its async value source, the cell-publishing sink, the per-row context and market-data feed, and the metadata attributes that list it in the picker.

Market Analyzer: SabrTrader.Pipeline.MarketAnalyzer
A column is an IColumnDefinition. For each row it creates an IColumnValueSource, whose StartAsync wires up to the row's data and pushes CellValues into the supplied IColumnValueSink until the source is disposed. The host renders the latest value according to the column's ColumnDataType. The host discovers every public, concrete definition with a zero-arg-callable constructor; [MarketAnalyzerColumn] sets its picker name, category and description. Guide: Market Analyzer columns.

LastPriceColumn.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using SabrTrader.Pipeline.MarketAnalyzer;

[MarketAnalyzerColumn("My last", Category = "My plugin", Description = "Most recent trade price")]
public sealed class LastPriceColumn : IColumnDefinition
{
    public string Id => "myplugin.last-price";
    public string Header => "Last";
    public ColumnDataType DataType => ColumnDataType.Price;

    public IColumnValueSource CreateSource(InstrumentRef instrument, IColumnContext context)
        => new Source(context);

    private sealed class Source : IColumnValueSource
    {
        private readonly IColumnContext _context;
        private IDisposable? _sub;

        public Source(IColumnContext context) => _context = context;

        public Task StartAsync(IColumnValueSink sink, CancellationToken cancellationToken)
        {
            var feed = _context.GetService<IInstrumentFeed>();
            if (feed is null) { sink.Publish(CellValue.Error("No market data for this row.")); return Task.CompletedTask; }

            sink.Publish(CellValue.Initializing);
            _sub = feed.OnQuote(c =>
            {
                if ((c.Changed & RowQuoteFields.Last) != 0 && c.Quote.HasLast)
                    sink.Publish(CellValue.Ready(c.Quote.Last));
            });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _sub?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
Type	Members
IColumnDefinition	string Id, string Header, ColumnDataType DataType, IColumnValueSource CreateSource(InstrumentRef instrument, IColumnContext context). Stateless; one instance serves every row.
IColumnValueSource : IAsyncDisposable	Task StartAsync(IColumnValueSink sink, CancellationToken cancellationToken). Hooks run on the row's actor thread, never concurrently.
IColumnValueSink	void Publish(CellValue value). Safe from any thread; queues and returns.
CellValue	readonly record struct: CellState State, object? RawValue, string? ErrorMessage; static Ready(object?), Initializing, Error(string).
CellState	enum (byte): Initializing = 0, Ready = 1, Error = 2.
IColumnContext	IMarketAnalyzerClock Clock, IDataProvider? ResolveDataProvider(InstrumentRef), TimeZoneInfo SessionTimeZone, ISessionResolver SessionResolver, object? GetService(Type) (default null), string ResolveBackfillSymbol(InstrumentRef) (default identity), IDisposable? ObserveProviderAvailability(InstrumentRef instrument, Action onAvailable) (default null).
ColumnContextExtensions	static: T? GetService<T>(this IColumnContext) where T : class; string NativeSymbolFor(this IColumnContext, InstrumentRef) (the row feed's NativeSymbol, else ResolveBackfillSymbol).
IInstrumentFeed	The row's single market-data binding, shared by every column on it, resolved with GetService<IInstrumentFeed>(). string NativeSymbol, IDataProvider? Provider, RowBinding Binding, RowQuote Quote, event Action? Rebound; IDisposable OnQuote(Action<RowQuoteChange>), IDisposable OnTicks(Action<Tick>), IDisposable OnSummary(Action<MarketSummaryUpdate>), Task<IReadOnlyList<Bar>> LoadBarsAsync(BarSpecification spec, TimeSpan? lookback, CancellationToken ct). One provider resolution and one symbol translation per row, so two columns always agree on the contract. Handlers run on the feed thread. Narrative: Producing values.
RowQuote	readonly record struct (double Last, long LastSize, DateTime LastUtc, QuoteSource LastSource, double Bid, long BidSize, double Ask, long AskSize, double PreviousClose, QuoteSource PreviousCloseSource); HasLast, HasBid, HasAsk, HasPreviousClose, RowQuoteFields Present; static Empty. One snapshot, so a cell never mixes a fresh bid with a stale last; unknown fields are double.NaN.
RowQuoteChange · RowQuoteFields	readonly record struct (RowQuote Quote, RowQuoteFields Changed); [Flags] enum None, Last, Bid, Ask, PreviousClose. React only to the field you render.
QuoteSource	enum: None, Trade (an actual print), Statement (the venue stated it: a settlement, a daily summary). Carried separately for Last and PreviousClose.
RowBinding · RowBindingKind	readonly record struct (RowBindingKind Kind, string? ProviderKey, string? Reason); IsBound; statics BoundTo(providerKey), Unbound(reason); enum Unbound, Bound. An unbound row carries the user-facing reason, so a column can show it.
IMarketAnalyzerClock	DateTime UtcNow { get; }.
ISessionResolver	DateTime GetSessionStartUtc(InstrumentRef instrument, DateTime momentUtc). A pure function.
ColumnDataType	enum: Text, Integer, Number, Price, Percent, Timestamp, Boolean, ConditionStatus, Sparkline. ConditionStatus and Sparkline carry host payload types used by built-in columns.
InstrumentRef	readonly record struct: string SymbolId, string? ProviderKey; ToString() gives "ProviderKey:SymbolId" (or SymbolId); static Validated(string symbolId, string? providerKey = null).
MarketAnalyzerColumnAttribute	MarketAnalyzerColumnAttribute(string displayName); DisplayName, init Category, init Description.
ColumnParameterAttribute	init string DisplayName, string Description, double MinValue, double MaxValue (NaN = unbounded). On a public read/write property of type bool, int, long, double, decimal, string, TimeSpan, DateTime or an enum.
IInstrumentDescriptionSource	string? GetDescription(string nativeSymbol): optional service resolved via IColumnContext.GetService (file IInstrumentInfoSources.cs).
