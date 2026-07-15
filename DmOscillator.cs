using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Directional Movement Oscillator — the net directional bias
/// <c>+DI − −DI</c> of the DMI, collapsed to a single zero-centred
/// line. Above zero = bulls in control (+DI dominant), below zero =
/// bears; the magnitude is the strength of the imbalance. Zero
/// crossings replace the +DI/−DI crossover of the full DMI panel.
///
/// Composed from the existing <see cref="Dm"/> built-in (reads its
/// Wilder-smoothed +DI / −DI) — no math duplication.
///
/// Defaults Period=14. Single plot (DarkCyan), zero centerline,
/// range −100…100, sub-panel.
/// </summary>
[IndicatorDescription("Directional Movement Oscillator: the DMI's +DI minus -DI as a single zero-centred line. Above zero = bullish directional dominance, below = bearish; zero crossings mark the DI crossover.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class DmOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Dm _dm = null!;

    public DmOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("DM Osc", _output, PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, -100, 100, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Wilder period of the underlying DMI.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _dm = new Dm(this, bars: Bars) { Period = Period };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _dm.DiPlus.Count;
        for (int i = 0; i < n; i++) _output.Append(_dm.DiPlus[n - 1 - i] - _dm.DiMinus[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_dm.DiPlus.Count == 0) return;
        _output.Append(_dm.DiPlus[0] - _dm.DiMinus[0]);
    }
}
