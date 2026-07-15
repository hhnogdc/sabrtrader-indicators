namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Where a session-anchored indicator takes its session times from. The shared
/// "Session Times" dropdown for every session-anchored built-in (CumulativeDelta,
/// the pivots family, CurrentDayOhl / PriorDayOhlc,
/// NetChangeDisplay, InitialBalance) — replaces the earlier confusing
/// Session Anchored + Use Instrument Trading Hours checkbox pair.
/// </summary>
public enum SessionTimesSource
{
    /// <summary>The data series' Trading Hours template (holiday/DST-correct).</summary>
    InstrumentTradingHours,

    /// <summary>
    /// A manual session window entered on the indicator (Session Start / Session End,
    /// chart-display timezone). Set Start == End for a 24/7 window that rolls over at
    /// calendar midnight — the old "calendar day" behaviour.
    /// </summary>
    Custom,
}
