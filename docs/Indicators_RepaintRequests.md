Indicators
Repaint requests INTERFACE
Ask the chart to repaint on your own schedule (per tick, on a timer, on demand) and let it coalesce your requests into smooth frames.

On this page
The repaint model
Example: a 1 Hz bar timer
You choose the cadence
Example: showing the connection state
Repaint vs. recalculate
The repaint model
You raise an event when your visual state changes, and the chart coalesces every request inside one frame into a single paint, capped at the display refresh rate (about 60 fps). A footprint indicator can fire on thousands of ticks per second and still render smoothly, because the work is bounded by the screen and not by the tick rate.

You opt in by implementing IRepaintNotifier: one event, RepaintRequested, and one method, RequestRepaint. Raise the event whenever something visible has changed. RequestRepaint is the inbound direction: the settings dialog calls it after writing parameter edits so render-time settings (colours, modes, geometry) take effect on the next frame without waiting for a tick. Its default implementation does nothing; implement it to raise RepaintRequested when you want that behaviour.

Example: a 1 Hz bar timer
A countdown chip showing time-to-close needs to repaint once a second, even when no tick arrives. It owns its own timer and fires RepaintRequested from it.

BarTimer.cs
using System;
using System.Timers;
using SabrTrader.Pipeline.Indicators;

[IndicatorDescription("Shows the time remaining until the current bar closes, as a chip in the corner.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class BarTimer : IndicatorBase, IChartCustomRender, IRepaintNotifier
{
    private readonly Timer _timer = new(1000) { AutoReset = true };

    public event Action? RepaintRequested;

    public BarTimer(IIndicator? parent = null) : base(parent)
    {
        _timer.Elapsed += (_, _) => RepaintRequested?.Invoke();   // timer thread; the chart marshals
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _timer.Start();                                     // start when the indicator goes live
    }

    public void RequestRepaint() => RepaintRequested?.Invoke();   // settings edits repaint at once

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var bars = ctx.Bars;
        if (bars is null || !bars.TryGet(0, out var bar)) return;

        TimeSpan remaining = bar.End - ctx.Now;             // both are the chart timeline
        string text = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";

        double x = ctx.PlotArea.Right - 60;
        double y = ctx.ContentTopPx + 14;                   // below the legend
        ctx.Renderer.DrawText(x, y, text, new ChartColor(200, 200, 200), 12);
    }

    public override void OnDispose() => _timer.Dispose();   // always dispose your timer
}
Raise it from any thread. The chart subscribes to RepaintRequested when the indicator is added, unsubscribes when it is removed, and marshals to the UI thread inside its handler, so you raise the event directly from a worker or timer thread.
You choose the cadence
The framework sets no cadence; you drive it to suit your indicator:

Tick-driven: fire RepaintRequested from OnMarketData or OnBarUpdate. 10,000 ticks per second is fine; the chart coalesces to the refresh rate.
Timer-driven: own a timer (the BarTimer above) and fire at whatever rate you need.
On demand: fire once when a static decoration is ready.
Batched: collect updates in your own buffer and fire once when the batch is complete.
Event-driven: fire from a host callback that is not a tick, such as OnDataConnectionChanged below.
Example: showing the connection state
A chart that silently stops updating looks exactly like a quiet market. An indicator can tell the two apart: ctx.DataConnection reports the state of the live data connection behind the chart, and OnDataConnectionChanged fires when it changes. Neither needs an opt-in attribute.

This indicator paints a red banner across the top of the chart while the feed is down and raises an alert when the link drops. It is a natural fit for IRepaintNotifier: while the connection is down no ticks arrive, so nothing else would trigger the redraw that shows or clears the banner.

ConnectionBanner.cs
using System;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Providers;

[IndicatorDescription("Shows a banner across the chart while its live data connection is down.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class ConnectionBanner : IndicatorBase, IChartCustomRender, IRepaintNotifier
{
    // Written on the chart's worker, read on the UI thread when painting.
    private volatile DataConnectionStatus _status = DataConnectionStatus.NotApplicable;

    public event Action? RepaintRequested;

    public ConnectionBanner(IIndicator? parent = null) : base(parent) { }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _status = ctx.DataConnection;   // the callback never fires for the starting status
    }

    public override void OnDataConnectionChanged(DataConnectionStatus status, IIndicatorContext ctx)
    {
        _status = status;
        if (status is DataConnectionStatus.ConnectionLost or DataConnectionStatus.Failed)
            ctx.Alert("data-connection", $"Chart data connection: {status}", rearmSeconds: 60);
        RepaintRequested?.Invoke();     // no ticks arrive while the feed is down
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var status = _status;
        // NotApplicable: a backtest or another host with no live feed. Nothing to warn about.
        if (status is DataConnectionStatus.Connected or DataConnectionStatus.NotApplicable) return;

        string text = status switch
        {
            DataConnectionStatus.Connecting   => "Connecting to the data feed...",
            DataConnectionStatus.Reconnecting => "Reconnecting to the data feed...",
            DataConnectionStatus.Failed       => "Data connection failed",
            _                                 => "No data connection",
        };

        var area = ctx.PlotArea;
        double top = ctx.ContentTopPx;   // below the legend
        ctx.Renderer.FillRect(area.X, top, area.Width, 22, new ChartColor(180, 40, 40, 200));
        ctx.Renderer.DrawTextCentered(area.X + area.Width / 2, top + 16, text,
            new ChartColor(255, 255, 255), 13);
    }
}
Set the field in both places. OnDataConnectionChanged does not fire for the status the indicator starts with, so OnInit reads ctx.DataConnection once.
Treat NotApplicable as "nothing to show". Backtests, optimizer passes, indicators hosted inside a strategy and other hosts without a chart data venue report it. It is not a disconnect.
Transitions are coalesced. A quick drop and reconnect can arrive as a single Connected, so draw from the current status instead of counting events.
Alerts fire from here. The callback runs with ctx.IsLive true, so ctx.Alert goes out. The fixed id with rearmSeconds: 60 keeps a flapping link from raising an alert every few seconds.
The status also drives logic: check ctx.IsDataConnected before acting on a signal in OnBarUpdate, or dim a plot by changing its Color while the feed is down. The full contract is in the indicator reference.

Repaint vs. recalculate
A repaint only redraws: it re-runs OnCustomRender with the values your output series already hold. When a change makes the computed output itself stale (typically a toolbar-menu edit to a math-affecting setting), you need a recalculation: implement IRecalculateNotifier and raise its RecalculateRequested event, from any thread. The host clears your plot output, re-runs OnInit and OnDataLoaded over the full history with the same context, then repaints. Parameter edits made in the settings dialog trigger this on their own.

IRepaintNotifier	IRecalculateNotifier
Event	RepaintRequested	RecalculateRequested
Host does	Re-runs OnCustomRender only.	Clears output, replays OnInit + OnDataLoaded, then repaints.
Fire when	A render-only thing changed (colour, orientation, hover).	A math-affecting thing changed (a profile window, a smoothing period, an enable gating which data is accumulated).
recalculate on a math-affecting edit
public sealed class MyProfile : IndicatorBase, IRecalculateNotifier
{
    public event Action? RecalculateRequested;

    private int _window = 20;
    public int Window
    {
        get => _window;
        set { if (value != _window) { _window = value; RecalculateRequested?.Invoke(); } }
    }
}
