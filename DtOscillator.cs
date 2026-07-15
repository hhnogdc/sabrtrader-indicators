using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// DT Oscillator (DiNapoli / Robert Miner) — a double-smoothed
/// Stochastic-of-RSI. Sharper and less noisy than a plain Stochastic
/// RSI thanks to the extra SMA smoothing on both the %K and %D legs.
///
/// Math:
///   <code>
///   rsi       = RSI(Input, RsiPeriod)            // raw Wilder RSI
///   stochRsi  = 100·(rsi − LL(rsi, StochPeriod)) / (HH(rsi, StochPeriod) − LL(rsi, StochPeriod))
///   DTosc     = SK = SMA(stochRsi, SmoothK)
///   Signal    = SD = SMA(SK, SmoothD)
///   </code>
/// Flat-window singularity (HH == LL): stochRsi is 50 on its first
/// valid bar, otherwise carries the previous value.
///
/// Defaults RsiPeriod=13, StochPeriod=8, SmoothK=5, SmoothD=3 (Miner's
/// canonical DTosc). Two plots SK (DodgerBlue) / SD (Goldenrod),
/// 25 / 75 reference lines, range 0–100, sub-panel.
/// </summary>
[IndicatorDescription("DT Oscillator (DiNapoli): a Stochastic computed over RSI then double-SMA smoothed into a fast SK line and a slower SD signal. Crossovers and the 25/75 bands flag momentum turns with less noise than a plain Stochastic RSI.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class DtOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _sk = new();
    private readonly ChunkedSeries<double> _sd = new();
    private IndicatorOutputSeries? _skWrap;
    private IndicatorOutputSeries? _sdWrap;

    private Rsi _rsi = null!;
    private RollingMinMax _hh = null!;
    private RollingMinMax _ll = null!;
    private RollingSum _skSum = null!;
    private RollingSum _sdSum = null!;
    private int _stochCount;
    private int _skCount;
    private double _prevStoch;
    private bool _firstStoch;

    public DtOscillator(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var osc = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "25 oversold / 75 overbought");
        AddPlot(new Plot("DTosc",  _sk, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = osc });
        AddPlot(new Plot("Signal", _sd, PlotStyle.Line, NTColors.Goldenrod,  1.0) { Semantics = osc });
        AddLine("Oversold",   25, NTColors.DarkCyan);
        AddLine("Overbought", 75, NTColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "RsiPeriod", Description = "Wilder RSI period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiPeriod { get; set; } = 13;

    [IndicatorParameter(DisplayName = "StochPeriod", Description = "Stochastic lookback over the RSI.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int StochPeriod { get; set; } = 8;

    [IndicatorParameter(DisplayName = "SmoothK", Description = "SMA period over the stochastic-RSI (gives SK).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothK { get; set; } = 5;

    [IndicatorParameter(DisplayName = "SmoothD", Description = "SMA period over SK (gives SD signal).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SmoothD { get; set; } = 3;

    /// <summary>SK — fast DT-oscillator line.</summary>
    public ISeries<double> Output => _skWrap ??= new IndicatorOutputSeries(_sk, Bars);

    /// <summary>SD — signal line (SMA of SK).</summary>
    public ISeries<double> Signal => _sdWrap ??= new IndicatorOutputSeries(_sd, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RsiPeriod < 1 || StochPeriod < 1 || SmoothK < 1 || SmoothD < 1)
            throw new ArgumentOutOfRangeException(nameof(RsiPeriod), "All periods must be at least 1.");
        _rsi = new Rsi(this, input: Input) { Period = RsiPeriod, Smooth = 1 };
        _hh = new RollingMinMax(StochPeriod, trackMax: true);
        _ll = new RollingMinMax(StochPeriod, trackMax: false);
        _skSum = new RollingSum(SmoothK);
        _sdSum = new RollingSum(SmoothD);
        _stochCount = 0;
        _skCount = 0;
        _prevStoch = 50;
        _firstStoch = true;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _rsi.Default.Count;
        for (int i = 0; i < n; i++) Step(n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_rsi.Default.Count == 0) return;
        Step(0);
    }

    private void Step(int barsAgo)
    {
        double rsi = _rsi.Default[barsAgo];
        if (double.IsNaN(rsi)) { _sk.Append(double.NaN); _sd.Append(double.NaN); return; }

        _hh.Push(rsi);
        _ll.Push(rsi);
        double hh = _hh.Value, ll = _ll.Value;
        double range = hh - ll;
        double stoch = range > 0
            ? Math.Min(100, Math.Max(0, 100.0 * (rsi - ll) / range))
            : (_firstStoch ? 50.0 : _prevStoch);
        _prevStoch = stoch;
        _firstStoch = false;

        _skSum.Push(stoch);
        _stochCount++;
        double sk = _skSum.Sum / Math.Min(_stochCount, SmoothK);
        _sk.Append(sk);

        _sdSum.Push(sk);
        _skCount++;
        _sd.Append(_sdSum.Sum / Math.Min(_skCount, SmoothD));
    }
}
