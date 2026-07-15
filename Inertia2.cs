using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Inertia (alternative formulation) — Dorsey's later Inertia, which
/// inserts an EMA pre-smoothing stage on the RVI before the
/// least-squares fit. The extra smoothing reduces the false flips the
/// raw <see cref="Inertia"/> produces in choppy conditions at the cost
/// of a little more lag.
///
/// Pipeline:
///   <c>Inertia2 = LinReg( EMA( RVI(RviPeriod), Smooth ), LinRegPeriod )</c>,
/// composed from the existing <see cref="Rvi"/>, <see cref="Ema"/> and
/// <see cref="LinReg"/> built-ins.
///
/// Defaults RviPeriod=10, Smooth=14, LinRegPeriod=14. Single plot
/// (Goldenrod), 50 reference line, range ~0–100, sub-panel.
/// </summary>
[IndicatorDescription("Inertia (alternative): Dorsey's smoothed Inertia — LinReg of an EMA-pre-smoothed RVI. Slower and steadier than the raw Inertia, fewer false trend flips. Above 50 = bullish, below = bearish.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Inertia2 : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Rvi _rvi = null!;
    private Ema _ema = null!;
    private LinReg _lin = null!;

    public Inertia2(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Inertia2", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "50 centerline") });
        AddLine("Centerline", 50, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "RviPeriod", Description = "Relative Volatility Index period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RviPeriod { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Smooth", Description = "EMA pre-smoothing period over the RVI.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 14;

    [IndicatorParameter(DisplayName = "LinRegPeriod", Description = "Linear-regression window over the smoothed RVI.", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int LinRegPeriod { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RviPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RviPeriod));
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        if (LinRegPeriod < 2) throw new ArgumentOutOfRangeException(nameof(LinRegPeriod));
        _rvi = new Rvi(this, bars: Bars) { Period = RviPeriod };
        _ema = new Ema(this, input: _rvi.Output) { Period = Smooth };
        _lin = new LinReg(this, input: _ema.Output) { Period = LinRegPeriod };
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
