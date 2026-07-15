using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// DiNapoli Preferred Stochastic — Joe DiNapoli's preferred %K/%D pair.
/// Differs from the classic slow Stochastic by smoothing with an
/// EXPONENTIAL step (a recursive "DiNapoli detrend" of factor
/// α = 2/(Slowing+1)) instead of a simple moving average — it tracks
/// turns a touch faster and lags less at the extremes.
///
/// Math:
///   <code>
///   FastK = 100·(Close − LL(Low, PeriodK)) / (HH(High, PeriodK) − LL(Low, PeriodK))
///   SlowK = SlowK[1] + αK·(FastK − SlowK[1])      αK = 2/(SlowingK+1)
///   SlowD = SlowD[1] + αD·(SlowK − SlowD[1])       αD = 2/(SlowingD+1)
///   </code>
/// Seeds SlowK = FastK and SlowD = FastK on bar 0. Flat-window
/// singularity (HH == LL): FastK is 50 on bar 0, else carries previous.
///
/// Defaults PeriodK=8, SlowingK=3, SlowingD=3 (α = 0.5). Plots
/// SlowK (Goldenrod) / SlowD (DodgerBlue), 30 / 70 lines, sub-panel.
/// </summary>
[IndicatorDescription("DiNapoli Preferred Stochastic: raw %K exponentially smoothed twice (factor 2/(Slowing+1)) into SlowK and SlowD, DiNapoli's lower-lag alternative to the SMA-smoothed slow Stochastic.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class StochasticDiNapoli : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _slowK = new();
    private readonly ChunkedSeries<double> _slowD = new();
    private IndicatorOutputSeries? _kWrap, _dWrap;

    private RollingMinMax _hh = null!;
    private RollingMinMax _ll = null!;
    private double _kVal, _dVal, _prevFastK;
    private double _alphaK, _alphaD;
    private bool _seeded;
    private int _barIdx;

    public StochasticDiNapoli(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        var osc = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "30/70");
        AddPlot(new Plot("SlowK", _slowK, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = osc });
        AddPlot(new Plot("SlowD", _slowD, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = osc });
        AddLine("Oversold",   30, NTColors.DarkCyan);
        AddLine("Overbought", 70, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "PeriodK", Description = "Fast %K high/low lookback.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PeriodK { get; set; } = 8;

    [IndicatorParameter(DisplayName = "SlowingK", Description = "Exponential smoothing period for SlowK.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SlowingK { get; set; } = 3;

    [IndicatorParameter(DisplayName = "SlowingD", Description = "Exponential smoothing period for SlowD.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SlowingD { get; set; } = 3;

    public ISeries<double> K => _kWrap ??= new IndicatorOutputSeries(_slowK, Bars);
    public ISeries<double> D => _dWrap ??= new IndicatorOutputSeries(_slowD, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (PeriodK < 1 || SlowingK < 1 || SlowingD < 1)
            throw new ArgumentOutOfRangeException(nameof(PeriodK), "All periods must be at least 1.");
        _hh = new RollingMinMax(PeriodK, trackMax: true);
        _ll = new RollingMinMax(PeriodK, trackMax: false);
        _alphaK = 2.0 / (SlowingK + 1);
        _alphaD = 2.0 / (SlowingD + 1);
        _kVal = _dVal = 0;
        _prevFastK = 50;
        _seeded = false;
        _barIdx = 0;
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

    private void Consume(Bar bar)
    {
        _hh.Push(bar.High);
        _ll.Push(bar.Low);
        double hh = _hh.Value, ll = _ll.Value;
        double range = hh - ll;
        double fastK = range > 0
            ? Math.Min(100, Math.Max(0, 100.0 * (bar.Close - ll) / range))
            : (_barIdx == 0 ? 50.0 : _prevFastK);
        _prevFastK = fastK;

        if (!_seeded)
        {
            _kVal = fastK;
            _dVal = fastK;
            _seeded = true;
        }
        else
        {
            _kVal += _alphaK * (fastK - _kVal);
            _dVal += _alphaD * (_kVal - _dVal);
        }
        _slowK.Append(_kVal);
        _slowD.Append(_dVal);
        _barIdx++;
    }
}
