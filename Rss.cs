using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Spread Strength.
/// spread = EMA(in, EMA1) − EMA(in, EMA2).
/// rs     = RSI(spread, Length, Smooth=1).
/// Output = SMA(rs, 5).
/// Defaults EMA1=10, EMA2=40, Length=5. DarkCyan plot, 20/80 dark-gray lines.
///
/// EMA1/EMA2 are children of `this` (their Input is the source series so
/// the runtime's pre-parent auto-dispatch produces correct, fresh values
/// before Rss.OnBarUpdate reads them). RSI and SMA depend on
/// parent-filled series — they are constructed with parent=null and
/// driven manually to avoid the post-order dispatch hazard.
/// </summary>
[IndicatorDescription("Relative Spread Strength (TASC 2006): RSI applied to the EMA spread (EMA1 - EMA2), then smoothed with a 5-period SMA. Identifies trend strength while filtering noise.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Rss : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _spread = new();
    private readonly ChunkedSeries<double> _rsSeries = new();
    private IndicatorOutputSeries? _wrap, _spreadW, _rsW;
    private Ema _ema1 = null!, _ema2 = null!;
    private Rsi _rsi = null!;
    private Sma _sma = null!;

    public Rss(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("RSS", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "overbought/oversold") });
        AddLine("Lower", 20, PlotColors.DarkGray);
        AddLine("Upper", 80, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "EMA1", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int EMA1 { get; set; } = 10;

    [IndicatorParameter(DisplayName = "EMA2", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int EMA2 { get; set; } = 40;

    [IndicatorParameter(DisplayName = "Length", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 5;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (EMA1 < 1 || EMA2 < 1 || Length < 1) throw new ArgumentOutOfRangeException();
        // EMA1/EMA2 read source Input -> safe as parent-child (cascade fires correctly).
        _ema1 = new Ema(this, input: Input) { Period = EMA1 };
        _ema2 = new Ema(this, input: Input) { Period = EMA2 };
        _spreadW = new IndicatorOutputSeries(_spread, Bars);
        _rsW = new IndicatorOutputSeries(_rsSeries, Bars);
        // RSI/SMA read parent-filled series; parent=null + manual dispatch.
        _rsi = new Rsi(parent: null, input: _spreadW) { Period = Length, Smooth = 1 };
        _sma = new Sma(parent: null, input: _rsW) { Period = 5 };
        _rsi.OnInit(ctx);
        _sma.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Children Ema1/Ema2 are already fully backfilled by the cascade
        // (their OnDataLoaded fired during EnsureInitialized before us).
        int n = Input.Count;
        for (int i = 0; i < n; i++) StepOnce(ctx, n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        if (Input.Count == 0) return;
        StepOnce(ctx, 0);
    }

    private void StepOnce(IIndicatorContext ctx, int barsAgo)
    {
        double e1 = _ema1.Output[barsAgo];
        double e2 = _ema2.Output[barsAgo];
        _spread.Append(e1 - e2);
        _rsi.OnBarUpdate(ctx);
        double rsiVal = _rsi.Default[0];
        // The original's Series<double>[0] returns 0 for NaN-initialised slots in
        // chart usage; here Rsi emits NaN for bar 0 — the original's RSI does too,
        // but the original's SMA implicitly treats NaN as 0 in the rolling sum.
        // Coerce to 0 for parity.
        if (double.IsNaN(rsiVal)) rsiVal = 0;
        _rsSeries.Append(rsiVal);
        _sma.OnBarUpdate(ctx);
        _output.Append(_sma.Output[0]);
    }
}
