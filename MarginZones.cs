using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Charts.Rendering;
using SabrTrader.Pipeline.Charts.Viewport;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Direction the margin zones stack from the anchor (ATAS <c>ZoneDirection</c>).</summary>
public enum MarginZoneDirection { Up = 0, Down = 1 }

/// <summary>
/// Margin Zones — 1-on-1 port of ATAS <c>MarginZones</c>. Anchors to the prior week's extreme
/// (Low for <see cref="MarginZoneDirection.Up"/>, High for <see cref="MarginZoneDirection.Down"/>)
/// and stacks margin-based zones at 25 / 50 / 75 / 100 / 150 / 200 % of one margin's worth of price
/// movement. One full (100 %) zone spans <c>(Margin / TickCost) × TickSize</c> in price; the sign of
/// <c>zoneSize</c> (positive Up, negative Down) sets the stacking direction. Verbatim ATAS arithmetic:
/// <code>
///   zoneSize = Margin / TickCost × (Up ? +1 : -1)
///   level%   = anchor + zoneSize × pct × TickSize        (pct ∈ {0.25,0.5,0.75,1.0,1.5,2.0})
/// </code>
/// <see cref="TickCost"/> (currency per tick) and tick size resolve from the instrument
/// (<c>PointValue × TickSize</c> / <c>TickSize</c>) when the host supplies them, else from the
/// overridable parameters.
/// </summary>
[IndicatorDescription("Margin Zones: anchors to the prior week's low (Up) or high (Down) and stacks margin-based price zones at 25/50/75/100/150/200% of one margin's worth of movement (Margin/TickCost ticks). Useful for gauging how far price has travelled in margin terms.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class MarginZones : IndicatorBase, IChartCustomRender
{
    private static readonly double[] Pct = { 0.0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 };
    private static readonly string[] Labels = { "Base", "25%", "50%", "75%", "100%", "150%", "200%" };
    public const int ZoneCount = 7;   // Base + 6 percentage zones

    private readonly ChunkedSeries<double>[] _line = new ChunkedSeries<double>[ZoneCount];
    private readonly IndicatorOutputSeries?[] _wrap = new IndicatorOutputSeries?[ZoneCount];

    private double _tickSize;
    private double _resolvedTickCost;

    // Weekly anchor state.
    private bool _started;
    private DateTime _curWeek;
    private double _weekLow, _weekHigh;
    private double _anchor;
    private bool _hasAnchor;

    // Render state.
    private readonly double[] _lastLevels = new double[ZoneCount];
    private double _lastZoneWidth;
    private bool _hasLevels;

    public MarginZones(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var colors = new[]
        {
            NTColors.Goldenrod, NTColors.SteelBlue, NTColors.DodgerBlue, NTColors.RoyalBlue,
            NTColors.Crimson, NTColors.DarkOrange, NTColors.Red,
        };
        for (int i = 0; i < ZoneCount; i++)
        {
            _line[i] = new ChunkedSeries<double>();
            AddPlot(new Plot(Labels[i], _line[i], PlotStyle.Step, colors[i], i == 4 ? 1.5 : 1.0)
            {
                Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None,
                    "margin zone level (percentage of one margin's worth of price movement from the weekly anchor)")
            });
        }
    }

    [IndicatorParameter(DisplayName = "Margin", Description = "Account margin in currency (ATAS default 3200).",
        Group = "01. Parameters", Order = 1, MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Margin { get; set; } = 3200;

    [IndicatorParameter(DisplayName = "Tick Cost", Description = "Currency value of one tick. 0 = resolve from the instrument (PointValue x TickSize); otherwise this overrides it (ATAS default 6.25).",
        Group = "01. Parameters", Order = 2, MinValue = 0, MaxValue = double.MaxValue, Step = 0.01)]
    public double TickCost { get; set; } = 0;   // 0 ⇒ auto from instrument; non-zero overrides

    [IndicatorParameter(DisplayName = "Direction", Description = "Up anchors to the prior week's low and stacks zones upward; Down anchors to the prior week's high and stacks downward.",
        Group = "01. Parameters", Order = 3)]
    public MarginZoneDirection Direction { get; set; } = MarginZoneDirection.Up;

    [IndicatorParameter(DisplayName = "Use Custom Price", Description = "When on, anchor the zones to Custom Price instead of the prior week's extreme.",
        Group = "01. Parameters", Order = 4)]
    public bool UseCustomPrice { get; set; } = false;

    [IndicatorParameter(DisplayName = "Custom Price", Description = "Manual anchor price (only used when Use Custom Price is on).",
        Group = "01. Parameters", Order = 5, MinValue = 0, MaxValue = double.MaxValue, Step = 0.01)]
    public double CustomPrice { get; set; } = 0;

    [IndicatorParameter(DisplayName = "Tick Size", Description = "Instrument tick size used when the host does not supply one (default 0.25).",
        Group = "01. Parameters", Order = 6, MinValue = 0.0000001, MaxValue = double.MaxValue, Step = 0.01)]
    public double TickSizeOverride { get; set; } = 0.25;

    /// <summary>Margin-zone line by index (0 = Base, 1..6 = 25/50/75/100/150/200%). Newest value at index 0.</summary>
    public ISeries<double> Zone(int index)
    {
        if ((uint)index >= ZoneCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _wrap[index] ??= new IndicatorOutputSeries(_line[index], Bars);
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Margin < 1) throw new ArgumentOutOfRangeException(nameof(Margin));
        if (TickCost < 0) throw new ArgumentOutOfRangeException(nameof(TickCost));
        if (TickSizeOverride <= 0) throw new ArgumentOutOfRangeException(nameof(TickSizeOverride));

        _tickSize = ctx.TickSize is > 0 ? ctx.TickSize!.Value : TickSizeOverride;
        // TickCost override wins; else instrument value (PointValue × TickSize); else ATAS default.
        _resolvedTickCost = TickCost > 0 ? TickCost
            : ctx.PointValue is > 0 ? ctx.PointValue!.Value * _tickSize
            : 6.25;

        _started = false;
        _hasAnchor = false;
        _hasLevels = false;
        Array.Clear(_lastLevels);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars[bars.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars[0]);
    }

    protected override void OnReset(IIndicatorContext ctx)
    {
        base.OnReset(ctx);
        _started = false;
        _hasAnchor = false;
        _hasLevels = false;
        Array.Clear(_lastLevels);
    }

    private void Consume(Bar bar)
    {
        var week = WeekStart(bar.Start);
        if (!_started)
        {
            _started = true;
            _curWeek = week;
            _weekLow = bar.Low;
            _weekHigh = bar.High;
        }
        else if (week != _curWeek)
        {
            // A new week opened → the week that just ended supplies the anchor.
            _anchor = Direction == MarginZoneDirection.Up ? _weekLow : _weekHigh;
            _hasAnchor = true;
            _curWeek = week;
            _weekLow = bar.Low;
            _weekHigh = bar.High;
        }
        else
        {
            if (bar.Low < _weekLow) _weekLow = bar.Low;
            if (bar.High > _weekHigh) _weekHigh = bar.High;
        }

        double anchor = UseCustomPrice ? CustomPrice : (_hasAnchor ? _anchor : double.NaN);
        if (double.IsNaN(anchor))
        {
            for (int i = 0; i < ZoneCount; i++) _line[i].Append(double.NaN);
            return;
        }

        double zoneSize = Margin / _resolvedTickCost * (Direction == MarginZoneDirection.Up ? 1.0 : -1.0);
        _lastZoneWidth = zoneSize * 0.1 * _tickSize;
        for (int i = 0; i < ZoneCount; i++)
        {
            double level = anchor + zoneSize * Pct[i] * _tickSize;
            _lastLevels[i] = level;
            _line[i].Append(level);
        }
        _hasLevels = true;
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (!_hasLevels) return;
        IChartRenderer r = ctx.Renderer;
        ChartViewport vp = ctx.Viewport;
        for (int i = 0; i < ZoneCount; i++)
        {
            if (!Plots[i].Visible) continue;
            double level = _lastLevels[i];
            if (double.IsNaN(level)) continue;

            // Shade the 10%-thick margin band, then label the line (NT-style edge label).
            var color = Plots[i].Color;
            double y0 = vp.PriceToPixelY(level);
            double y1 = vp.PriceToPixelY(level + _lastZoneWidth);
            double top = Math.Min(y0, y1), h = Math.Abs(y1 - y0);
            if (h >= 1)
                r.FillRect(0, top, vp.PlotWidthPx, h, new ChartColor(color.R, color.G, color.B, 0x22));
            LevelLabel.Draw(r, vp, ctx.PlotArea, Labels[i], level, color, 11);
        }
    }

    private static DateTime WeekStart(DateTime t)
    {
        // Monday-anchored ISO week start (date only).
        int diff = ((int)t.DayOfWeek + 6) % 7;   // Mon=0 … Sun=6
        return t.Date.AddDays(-diff);
    }
}
