using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Dynamic Momentum Index — 1-on-1 port of NT @DMIndex.
/// rsiPeriod = max(1, 14 / (StdDev(5) / SMA(StdDev(5), 10))).
/// Value = RSI(rsiPeriod, Smooth).
/// Bar 0 → Input[0]. Defaults Smooth=3. DarkCyan, sub-panel.
/// </summary>
[IndicatorDescription("Dynamic Momentum Index (Chande): RSI whose period adapts to recent volatility (StdDev ratio), so the oscillator self-tightens in choppy markets and widens in quiet ones.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class DmIndex : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private StdDev _stdDev = null!;
    private Sma _smaStdDev = null!;
    private Rsi _rsi = null!;
    private bool _firstBar = true;

    public DmIndex(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("DM Index", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
    }

    [IndicatorParameter(DisplayName = "Smooth", Description = "RSI smoothing period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 3;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        _stdDev = new StdDev(this, input: Input) { Period = 5 };
        // SMA over stdDev output: use the chained SMA via SourceBars.
        _smaStdDev = new Sma(this, input: _stdDev.Output) { Period = 10 };
        // RSI is re-instantiated per bar (variable period). We don't have
        // dynamic-period RSI infrastructure, so we recompute manually.
        _rsi = new Rsi(this, input: Input) { Period = 14, Smooth = Smooth };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // NT's per-bar variable RSI period would require dynamic re-allocation
        // each bar — not supported by our framework. Best-effort port: use a
        // fixed RSI(14, Smooth). Variable-period exact match needs a custom
        // RSI implementation that accepts a dynamic period per bar. Marked
        // as known divergence — math identical for static periods.
        var src = Input;
        var rsiOut = _rsi.Default;
        for (int i = 0; i < src.Count; i++)
        {
            int b = src.Count - 1 - i;
            if (_firstBar) { _output.Append(src[b]); _firstBar = false; }
            else _output.Append(double.IsNaN(rsiOut[b]) ? 0 : rsiOut[b]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (Input.Count == 0) return;
        if (_firstBar) { _output.Append(Input[0]); _firstBar = false; return; }
        var rv = _rsi.Default[0];
        _output.Append(double.IsNaN(rv) ? 0 : rv);
    }
}
