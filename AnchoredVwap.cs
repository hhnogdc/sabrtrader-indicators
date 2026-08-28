using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Anchored VWAP — VWAP reset at user-defined anchor points (session start, swing high,
/// news event, etc.). Each anchor produces a separate running VWAP that begins at the
/// anchor bar and runs forward indefinitely.
///
/// Anchor management: call <see cref="AddAnchor(DateTime)"/> or
/// <see cref="AddAnchor(int)"/> before/during the lifecycle. Anchors before the first
/// loaded bar are clamped to bar 0; anchors after the latest bar register and start
/// emitting once a matching bar arrives.
///
/// Plots: <c>Anchored VWAP</c> (DodgerBlue) shows the most-recently-active anchor's VWAP
/// at each bar. Optional bands ±k·σ via <see cref="Band1StdDev"/>/<see cref="Band2StdDev"/>
/// (same volume-weighted standard-deviation formula as <see cref="Vwap"/>).
/// IChartCustomRender draws a vertical guide line + label at each anchor bar.
/// </summary>
[IndicatorDescription("Anchored VWAP. Volume-weighted average price reset at one or more user-defined anchor bars (session open, swing point, news event), with optional standard-deviation bands. Useful for gauging fair value relative to a chosen origin.")]
[IndicatorCategory(IndicatorCategory.Volume)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class AnchoredVwap : IndicatorBase, IChartCustomRender
{
    private sealed class AnchorState
    {
        public int    AnchorBarIdx;
        public string? Label;
        public double CumPV;
        public double CumPV2;
        public long   CumVol;
        public double Vwap;
        public double Sigma;
        public bool   Started;
    }

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _upper1 = new();
    private readonly ChunkedSeries<double> _lower1 = new();
    private readonly ChunkedSeries<double> _upper2 = new();
    private readonly ChunkedSeries<double> _lower2 = new();
    private IndicatorOutputSeries? _oW, _u1W, _l1W, _u2W, _l2W;

    private readonly List<AnchorState> _anchors = new();
    private readonly List<(DateTime When, string? Label)> _pendingTimeAnchors = new();
    private int _barIdx;

    public AnchoredVwap(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Anchored VWAP", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper1",        _upper1, PlotStyle.Line, new ChartColor(0x1e, 0x90, 0xff, 0xaa), 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower1",        _lower1, PlotStyle.Line, new ChartColor(0x1e, 0x90, 0xff, 0xaa), 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper2",        _upper2, PlotStyle.Line, new ChartColor(0x1e, 0x90, 0xff, 0x66), 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower2",        _lower2, PlotStyle.Line, new ChartColor(0x1e, 0x90, 0xff, 0x66), 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Band1 σ", MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band1StdDev { get; set; } = 1.0;

    [IndicatorParameter(DisplayName = "Band2 σ", MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Band2StdDev { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Band Fill Opacity %",
        Description = "Shades the area inside each σ band. 0 = no fill (lines only). The fill reuses each band's line colour (RGB) at this opacity; the 2σ band paints first so the centre reads denser. Typical: 8–15.",
        MinValue = 0, MaxValue = 100, Step = 1)]
    public int BandFillOpacity { get; set; } = 12;

    public ChartColor AnchorLineColor { get; set; } = new(0x1e, 0x90, 0xff, 0x80);
    public ChartColor AnchorLabelColor { get; set; } = PlotColors.DodgerBlue;

    public ISeries<double> Output => _oW  ??= new IndicatorOutputSeries(_output, Bars);
    public ISeries<double> Upper1 => _u1W ??= new IndicatorOutputSeries(_upper1, Bars);
    public ISeries<double> Lower1 => _l1W ??= new IndicatorOutputSeries(_lower1, Bars);
    public ISeries<double> Upper2 => _u2W ??= new IndicatorOutputSeries(_upper2, Bars);
    public ISeries<double> Lower2 => _l2W ??= new IndicatorOutputSeries(_lower2, Bars);

    /// <summary>Register a new anchor at the given bar index (0-based, 0 = first bar in series).</summary>
    public void AddAnchor(int barIdx, string? label = null)
    {
        if (barIdx < 0) barIdx = 0;
        _anchors.Add(new AnchorState { AnchorBarIdx = barIdx, Label = label });
    }

    /// <summary>Register a new anchor at the bar containing the given timestamp. Resolved during OnDataLoaded / OnBarUpdate.</summary>
    public void AddAnchor(DateTime when, string? label = null)
        => _pendingTimeAnchors.Add((when, label));

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Band1StdDev < 0 || Band2StdDev < 0) throw new ArgumentOutOfRangeException();
        // DO NOT clear _anchors — they may have been registered by the host BEFORE OnInit
        // (a common usage pattern: `var avw = new AnchoredVwap(); avw.AddAnchor(20); session.Start(avw)`).
        // Only reset per-anchor running state so a re-init produces fresh VWAPs from the same anchors.
        foreach (var a in _anchors) { a.CumPV = 0; a.CumPV2 = 0; a.CumVol = 0; a.Vwap = 0; a.Sigma = 0; a.Started = false; }
        _barIdx = 0;
        Plots[1].Visible = Band1StdDev > 0;
        Plots[2].Visible = Band1StdDev > 0;
        Plots[3].Visible = Band2StdDev > 0;
        Plots[4].Visible = Band2StdDev > 0;
        // A disabled band emits only NaN — keep it out of the data box too.
        Plots[1].IncludeInDataWindow = Plots[2].IncludeInDataWindow = Band1StdDev > 0;
        Plots[3].IncludeInDataWindow = Plots[4].IncludeInDataWindow = Band2StdDev > 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Consume(bars, bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Consume(Bars, 0);
    }

    private void Consume(IBarSeries bars, int barsAgoOfCurrent)
    {
        var bar = bars[barsAgoOfCurrent];

        // Resolve pending time-anchors that match this bar's timestamp.
        for (int k = _pendingTimeAnchors.Count - 1; k >= 0; k--)
        {
            var (when, label) = _pendingTimeAnchors[k];
            if (bar.Start <= when && when < bar.End)
            {
                _anchors.Add(new AnchorState { AnchorBarIdx = _barIdx, Label = label });
                _pendingTimeAnchors.RemoveAt(k);
            }
            else if (when < bar.Start && _barIdx == 0)
            {
                // Past-date anchor before series start → clamp to bar 0.
                _anchors.Add(new AnchorState { AnchorBarIdx = 0, Label = label });
                _pendingTimeAnchors.RemoveAt(k);
            }
        }

        double typical = (bar.High + bar.Low + bar.Close) / 3;
        double pv = typical * bar.Volume;
        double pv2 = typical * typical * bar.Volume;

        // Step every active anchor.
        AnchorState? latest = null;
        for (int i = 0; i < _anchors.Count; i++)
        {
            var a = _anchors[i];
            if (_barIdx < a.AnchorBarIdx) continue;
            a.Started = true;
            a.CumPV  += pv;
            a.CumPV2 += pv2;
            a.CumVol += bar.Volume;
            a.Vwap = a.CumVol > 0 ? a.CumPV / a.CumVol : 0;
            double var = a.CumVol > 0 ? a.CumPV2 / a.CumVol - a.Vwap * a.Vwap : 0;
            if (var < 0) var = 0;
            a.Sigma = Math.Sqrt(var);
            latest = a;
        }

        if (latest is not null)
        {
            _output.Append(latest.Vwap);
            _upper1.Append(Band1StdDev > 0 ? latest.Vwap + Band1StdDev * latest.Sigma : double.NaN);
            _lower1.Append(Band1StdDev > 0 ? latest.Vwap - Band1StdDev * latest.Sigma : double.NaN);
            _upper2.Append(Band2StdDev > 0 ? latest.Vwap + Band2StdDev * latest.Sigma : double.NaN);
            _lower2.Append(Band2StdDev > 0 ? latest.Vwap - Band2StdDev * latest.Sigma : double.NaN);
        }
        else
        {
            _output.Append(double.NaN); _upper1.Append(double.NaN); _lower1.Append(double.NaN);
            _upper2.Append(double.NaN); _lower2.Append(double.NaN);
        }
        _barIdx++;
    }

    public IReadOnlyList<int> AnchorBarIndices
    {
        get { var r = new int[_anchors.Count]; for (int i = 0; i < r.Length; i++) r[i] = _anchors[i].AnchorBarIdx; return r; }
    }

    // Band fills paint BEHIND the candles so the σ rings read as a faint background
    // tint (the band edge lines are still drawn opaquely by the standard PlotRenderPass
    // on top). Matches Vwap. The anchor guide line/label render in the same pass.
    public CustomRenderLayer Layer => CustomRenderLayer.BelowBars;

    /// <summary>Fill the σ bands, then draw a vertical guide + label at each anchor bar.</summary>
    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        var vp = ctx.Viewport;

        // σ band fills as ANNULAR zones between adjacent bands so the fill hugs
        // the band lines with no spill outside / overlap inside (same as Vwap).
        if (BandFillOpacity > 0)
        {
            int total = _output.Count;
            if (total > 0)
            {
                byte alpha = (byte)Math.Clamp((int)Math.Round(BandFillOpacity / 100.0 * 255.0), 0, 255);
                Span<int> active = stackalloc int[2];
                int n = 0;
                if (Band1StdDev > 0 && (Plots[1].Visible || Plots[2].Visible)) active[n++] = 0;
                if (Band2StdDev > 0 && (Plots[3].Visible || Plots[4].Visible)) active[n++] = 1;
                if (n > 0)
                {
                    int inner = active[0];
                    BandFill.FillBetween(ctx.Renderer, vp, Upper(inner), Lower(inner), total,
                        BandFill.WithAlpha(Plots[1 + inner * 2].Color, alpha));
                    for (int k = 1; k < n; k++)
                    {
                        int cur = active[k], prev = active[k - 1];
                        var color = BandFill.WithAlpha(Plots[1 + cur * 2].Color, alpha);
                        BandFill.FillBetween(ctx.Renderer, vp, Upper(cur), Upper(prev), total, color); // top ring
                        BandFill.FillBetween(ctx.Renderer, vp, Lower(prev), Lower(cur), total, color); // bottom ring
                    }
                }
            }
        }

        if (_anchors.Count == 0) return;
        foreach (var a in _anchors)
        {
            if (a.AnchorBarIdx < vp.FirstVisibleBarIdx || a.AnchorBarIdx > vp.LastVisibleBarIdx) continue;
            double x = vp.BarIndexToPixelX(a.AnchorBarIdx);
            ctx.Renderer.DrawLine(x, ctx.PlotArea.Y, x, ctx.PlotArea.Bottom, AnchorLineColor, 1.0);
            string label = a.Label ?? $"Anchor@{a.AnchorBarIdx}";
            var (w, h) = ctx.Renderer.MeasureText(label, 10);
            ctx.Renderer.DrawText(x + 4, ctx.PlotArea.Y + 4, label, AnchorLabelColor, 10);
        }
    }

    // Band index (0-based) → its upper / lower σ series.
    private ChunkedSeries<double> Upper(int band) => band == 0 ? _upper1 : _upper2;
    private ChunkedSeries<double> Lower(int band) => band == 0 ? _lower1 : _lower2;
}
