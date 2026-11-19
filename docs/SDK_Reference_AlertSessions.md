Reference
Alerts & sessions
Two small surfaces: alert severity and the cooldown gate; and the trading-hours model (templates, sessions, holidays, the resolver/provider seams and the session iterator).

On this page
Alerts
Trading hours
Sessions
Alerts: SabrTrader.Pipeline.Alerts
A plugin raises alerts through IIndicatorContext.Alert, passing an AlertSeverity. The host applies the user's routing and delivers each alert to the channels the user has enabled (log, sound, email, Telegram). AlertCooldownGate suppresses repeats per key. Guide: Alerts & channels.

Type	Members
AlertSeverity	enum: Info, Warning, Critical. Routing threshold and presentation hint.
AlertCooldownGate	AlertCooldownGate(int cooldownSeconds); int CooldownSeconds, bool TryFire(string key, DateTime now), bool TryFire(string key, DateTime now, double cooldownSeconds), void Reset(). Not thread-safe; call it from one owner thread.
Trading hours: SabrTrader.Pipeline.Indicators.TradingHours
A TradingHoursTemplate is a named set of weekly TradingSessions plus holiday rules in an exchange time zone. Resolve one for an instrument through ITradingHoursResolver (or the ambient holder) and iterate sessions with ISessionIterator. Guide: Trading hours & sessions.

Type	Members
TradingHoursTemplate	TradingHoursTemplate(string name, IReadOnlyList<TradingSession> sessions, IReadOnlyList<HolidayRule> holidays, TimeZoneInfo exchangeTimeZone); Name, Sessions, Holidays, ExchangeTimeZone; bool TryGetHoliday(DateOnly exchangeTradingDate, out HolidayRule rule); static Create(name, sessions, holidays, string windowsTimeZoneId).
TradingHoursTemplates	static seeds, by name and as templates: const string TwentyFourSevenName ("24 / 7"), const string UsEquitiesRthName ("US Equities RTH"), TwentyFourSeven(TimeZoneInfo? timeZone = null), TwentyFourFive(TimeZoneInfo? timeZone = null), CmeEth(), CmeRth(), UsEquitiesRth(). The host's resolver falls back to UsEquitiesRthName for a symbol its catalog does not list but that looks like a US equity, so such a symbol gets US equity hours rather than 24/7.
TradingSession	readonly record struct (DayOfWeek BeginDay, TradingTime BeginTime, DayOfWeek EndDay, TradingTime EndTime, DayOfWeek TradingDay); BeginOffsetMinutes, EndOffsetMinutes, DurationMinutes.
TradingTime	readonly record struct; TradingTime(int hhmm); Hhmm, Hour, Minute, MinutesFromMidnight, IsEndOfDaySentinel; static FromMinutes(int minutesFromMidnight), FromTimeOnly(TimeOnly); TimeOnly ToTimeOnly().
HolidayRule	readonly record struct (DateOnly TradingDate, HolidayKind Kind, TradingSession? Constraint, string? Description).
HolidayKind	enum: FullDay, Replace, EarlyClose, LateOpen, Modify.
ITradingHoursResolver	TradingHoursTemplate Resolve(string instrumentId), TradingHoursTemplate Resolve(string instrumentId, string? templateName), bool TryResolveName(string instrumentId, out string templateName), IReadOnlyList<string> ListTemplateNames(). NullTradingHoursResolver.Instance is the empty implementation.
ITradingHoursProvider	TradingHoursTemplate? GetTemplate().
ITradingHoursSchedule · AlwaysOpenTradingHoursSchedule	bool IsInSession(TradingHoursTemplate? template, DateTime utc): the narrow "is the market open at this instant" question, kept apart from the iterator so a caller that needs only that takes only that. AlwaysOpenTradingHoursSchedule.Instance answers true for everything: the 24/7 default, and the right stub in a test.
TradingHoursAmbient	static: ITradingHoursResolver Resolver, void SetResolver(ITradingHoursResolver?), IDisposable Use(ITradingHoursResolver); ITradingHoursSchedule Schedule, void SetSchedule(ITradingHoursSchedule?), IDisposable UseSchedule(ITradingHoursSchedule).
Sessions
The session iterator (namespace SabrTrader.Pipeline.Indicators.TradingHours) walks trading days; the SessionWindow helpers (namespace SabrTrader.Pipeline.Indicators.Sessions) are static in-session tests for a simple start/end time pair.

Type	Members
ISessionIterator	DateTime GetTradingDay(DateTime time), bool IsNewSession(DateTime time, bool includesEndTimestamp), void GetNextSession(DateTime time, bool includesEndTimestamp), DateTime CalculateTradingDay(DateTime time, bool includesEndTimestamp), bool IsInSession(DateTime time, bool includesEndTimestamp, bool isIntraday), bool IsTradingDayDefined(DateTime date), DateTime GetTradingDayBeginLocal(DateTime tradingDayExchange), DateTime GetTradingDayEndLocal(DateTime tradingDayExchange), void Reset(); ActualSessionBegin, ActualSessionEnd, ActualTradingDayExchange, ActualTradingDayEndLocal. Times are display-as-Utc-kind: pass Bar.Start / Bar.End on a chart iterator.
SessionBoundary	static: bool IsSessionOpen(ISessionIterator iterator, DateTime currentStart, bool hasPreviousBar, DateTime previousStart).
SessionResetTracker	SessionResetTracker Create(ISessionIterator? instrumentIterator, bool sessionAware, TimeOnly start, TimeOnly end); bool InSession, DateTime SessionBegin, DateTime SessionEnd (display-as-Utc-kind), DateTime TradingDay (date-only key of the current session: the exchange trading day in instrument mode, so a Sunday-evening open belongs to Monday), bool OnBar(DateTime barStart), bool OnBar(DateTime barStart, DateTime barEnd) (pass bar.Start / bar.End on a chart iterator), void Reset(); static readonly TimeSpan SettlementFoldWindow.
SessionWindow	static: bool IsInSession(DateTime barTime, TimeOnly start, TimeOnly end), DateOnly SessionDate(DateTime barTime, TimeOnly start, TimeOnly end), bool IsSessionOpenBar(DateTime currentTime, DateTime previousTime, TimeOnly start, TimeOnly end).
