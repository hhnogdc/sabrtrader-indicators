using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Inertia (Donald Dorsey) — the linear-regression value of the
/// Relative Volatility Index. Dorsey's premise: a market in motion
/// stays in motion, and volatility-direction (RVI) smoothed by a
/// least-squares line measures that "inertia." Above 50 = bullish
/// inertia (uptrend intact), below 50 = bearish.
///
/// Pipeline: <c>Inertia = LinReg(RVI(RviPeriod), LinRegPeriod)</c>,
/// composed from the existing <see cref="Rvi"/> and <see cref="LinReg"/>
/// built-ins (no math duplication).
///
/// Defaults RviPeriod=14, LinRegPeriod=20. Single plot (Goldenrod),
/// 50 reference line, range ~0–100, sub-panel.
/// </summary>
[IndicatorDescription("Inertia (Dorsey): the linear-regression value of the Relative Volatility Index, a slow trend filter. Above 50 = bullish inertia, below 50 = bearish. Composed from the RVI and LinReg built-ins.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Inertia : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Rvi _rvi = null!;
    private LinReg _lin = null!;

    public Inertia(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Inertia", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "50 centerline") });
        AddLine("Centerline", 50, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "RviPeriod", Description = "Relative Volatility Index period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RviPeriod { get; set; } = 14;

    [IndicatorParameter(DisplayName = "LinRegPeriod", Description = "Linear-regression window over the RVI.", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int LinRegPeriod { get; set; } = 20;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RviPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RviPeriod));
        if (LinRegPeriod < 2) throw new ArgumentOutOfRangeException(nameof(LinRegPeriod));
        _rvi = new Rvi(this, bars: Bars) { Period = RviPeriod };
        _lin = new LinReg(this, input: _rvi.Output) { Period = LinRegPeriod };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _lin.Output.Count;
        for (int i = 0; i < n; i++) _output.Append(_lin.Output[n - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_lin.Output.Count == 0) return;
        _output.Append(_lin.Output[0]);
    }
}
