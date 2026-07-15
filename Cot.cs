using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum CotReportType  { Futures = 0, Options = 1, FuturesAndOptions = 2 }
public enum CotReportField { NoncommercialNet, CommercialNet, NonreportablePositionsNet, OpenInterest, TotalNet }
public enum LegendLocation { Disabled, TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class CotReport
{
    public CotReportType ReportType { get; set; } = CotReportType.Futures;
    public CotReportField Field { get; set; } = CotReportField.NoncommercialNet;
}

/// <summary>
/// Commitment of Traders (COT) — 1-on-1 port of NT @COT.
/// Plots up to 5 series of CFTC-published COT data per instrument. The pipeline does NOT
/// download CFTC data — the host MUST supply a value-resolver callback that returns the
/// per-bar value of a given (ReportType, Field) tuple for the current bar's date.
/// Set <see cref="ValueResolver"/> before OnInit. Default: returns NaN per slot.
/// Defaults Number=3, LegendLocation=TopLeft, 5 plots (CornflowerBlue/Red/LimeGreen/Goldenrod/BlueViolet).
/// IChartCustomRender draws the legend labels in the chosen corner.
/// </summary>
[IndicatorDescription("Commitment of Traders. Plots up to five CFTC COT report fields (noncommercial net, commercial net, nonreportable net, open interest, total net) per instrument; the host must supply a value-resolver for the underlying CFTC data.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Cot : IndicatorBase, IIndicatorPanelHint, IChartCustomRender
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _c1 = new();
    private readonly ChunkedSeries<double> _c2 = new();
    private readonly ChunkedSeries<double> _c3 = new();
    private readonly ChunkedSeries<double> _c4 = new();
    private readonly ChunkedSeries<double> _c5 = new();
    private IndicatorOutputSeries? _w1, _w2, _w3, _w4, _w5;

    public Cot(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var cotLine = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Raw, PlotBullishDirection.None, null);
        AddPlot(new Plot("COT1", _c1, PlotStyle.Line, NTColors.CornflowerBlue, 1.0) { Semantics = cotLine });
        AddPlot(new Plot("COT2", _c2, PlotStyle.Line, NTColors.Red,            1.0) { Semantics = cotLine });
        AddPlot(new Plot("COT3", _c3, PlotStyle.Line, NTColors.LimeGreen,      1.0) { Semantics = cotLine });
        AddPlot(new Plot("COT4", _c4, PlotStyle.Line, NTColors.Goldenrod,      1.0) { Semantics = cotLine });
        AddPlot(new Plot("COT5", _c5, PlotStyle.Line, NTColors.BlueViolet,     1.0) { Semantics = cotLine });
        CotReport1 = new CotReport { ReportType = CotReportType.Futures, Field = CotReportField.NoncommercialNet };
        CotReport2 = new CotReport { ReportType = CotReportType.Futures, Field = CotReportField.CommercialNet };
        CotReport3 = new CotReport { ReportType = CotReportType.Futures, Field = CotReportField.NonreportablePositionsNet };
        CotReport4 = new CotReport { ReportType = CotReportType.Futures, Field = CotReportField.OpenInterest };
        CotReport5 = new CotReport { ReportType = CotReportType.Futures, Field = CotReportField.TotalNet };
    }

    [IndicatorParameter(DisplayName = "Number", MinValue = 1, MaxValue = 5, Step = 1)]
    public int Number { get; set; } = 3;

    [IndicatorParameter(DisplayName = "LegendLocation")]
    public LegendLocation LegendLocation { get; set; } = LegendLocation.TopLeft;

    public CotReport CotReport1 { get; set; } = null!;
    public CotReport CotReport2 { get; set; } = null!;
    public CotReport CotReport3 { get; set; } = null!;
    public CotReport CotReport4 { get; set; } = null!;
    public CotReport CotReport5 { get; set; } = null!;

    /// <summary>
    /// Host-supplied resolver: given the report config + the bar's date, return the COT value
    /// (or NaN when not available). Replaces NT's <c>CotData.GetCotReportNames</c> +
    /// <c>CotReport.Calculate</c> chain.
    /// </summary>
    public Func<CotReport, DateTime, double> ValueResolver { get; set; } = null!;

    public ISeries<double> Cot1 => _w1 ??= new IndicatorOutputSeries(_c1, Bars);
    public ISeries<double> Cot2 => _w2 ??= new IndicatorOutputSeries(_c2, Bars);
    public ISeries<double> Cot3 => _w3 ??= new IndicatorOutputSeries(_c3, Bars);
    public ISeries<double> Cot4 => _w4 ??= new IndicatorOutputSeries(_c4, Bars);
    public ISeries<double> Cot5 => _w5 ??= new IndicatorOutputSeries(_c5, Bars);

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

    private void Consume(Bar bar)
    {
        var reports = new[] { CotReport1, CotReport2, CotReport3, CotReport4, CotReport5 };
        var series  = new[] { _c1, _c2, _c3, _c4, _c5 };
        var time = bar.StartUtc;
        for (int i = 0; i < 5; i++)
        {
            double v = double.NaN;
            if (i < Number && ValueResolver != null && reports[i] != null)
                v = ValueResolver(reports[i], time);
            series[i].Append(double.IsNaN(v) ? 0 : v);
        }
    }

    public void OnCustomRender(IIndicatorRenderContext ctx)
    {
        if (LegendLocation == LegendLocation.Disabled) return;
        var renderer = ctx.Renderer;
        double sizePx = 11;
        var (lineH, _) = (sizePx + 4, 0.0);
        for (int i = 0; i < Number; i++)
        {
            string label = i switch
            {
                0 => CotReport1?.Field.ToString() ?? "",
                1 => CotReport2?.Field.ToString() ?? "",
                2 => CotReport3?.Field.ToString() ?? "",
                3 => CotReport4?.Field.ToString() ?? "",
                _ => CotReport5?.Field.ToString() ?? "",
            };
            var (wPx, hPx) = renderer.MeasureText(label, sizePx);
            double x = 0, y = 0;
            switch (LegendLocation)
            {
                case LegendLocation.TopLeft:     x = ctx.PlotArea.X + 8;                         y = ctx.PlotArea.Y + 8 + (i + 1) * hPx; break;
                case LegendLocation.TopRight:    x = ctx.PlotArea.Right - 8 - wPx;               y = ctx.PlotArea.Y + 8 + (i + 1) * hPx; break;
                case LegendLocation.BottomLeft:  x = ctx.PlotArea.X + 8;                         y = ctx.PlotArea.Bottom - (Number + 1 - i) * hPx; break;
                case LegendLocation.BottomRight: x = ctx.PlotArea.Right - 8 - wPx;               y = ctx.PlotArea.Bottom - (Number + 1 - i) * hPx; break;
            }
            renderer.DrawText(x, y, label, Plots[i].Color, sizePx);
        }
    }
}
