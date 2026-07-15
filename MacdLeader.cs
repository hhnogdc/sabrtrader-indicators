using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// MACD Leader (Giorgos Siligardos, 2014) — a low-lag MACD variant
/// that adds, to each EMA leg, an EMA of that leg's own error
/// (Close − EMA). The correction term pulls the average toward price
/// faster, so the Leader turns ahead of the standard MACD while
/// keeping the same zero-line interpretation.
///
/// Math:
///   <code>
///   I1     = EMA(Close, Fast)            I2 = EMA(Close, Slow)
///   macd   = I1 − I2
///   Leader = macd + EMA(Close − I1, Fast) − EMA(Close − I2, Slow)
///   </code>
///
/// Defaults Fast=12, Slow=26. Plots MACD (DarkCyan) / Leader (Crimson),
/// zero centerline, sub-panel.
/// </summary>
[IndicatorDescription("MACD Leader (Siligardos): a low-lag MACD that adds an EMA of each leg's price-error so the line leads the classic MACD into turns. Plots the standard MACD alongside the leading Leader line around a zero centerline.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MacdLeader : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _macd = new();
    private readonly ChunkedSeries<double> _leader = new();
    private IndicatorOutputSeries? _macdWrap, _leaderWrap;

    private double _aFast, _aSlow;
    private double _i1, _i2, _diffFast, _diffSlow;
    private bool _hasValue;

    public MacdLeader(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        var sem = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline");
        AddPlot(new Plot("MACD",   _macd,   PlotStyle.Line, NTColors.DarkCyan, 1.0) { Semantics = sem });
        AddPlot(new Plot("Leader", _leader, PlotStyle.Line, NTColors.Crimson,  1.0) { Semantics = sem });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast", Description = "Fast EMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 12;

    [IndicatorParameter(DisplayName = "Slow", Description = "Slow EMA period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 26;

    public ISeries<double> Output => _macdWrap ??= new IndicatorOutputSeries(_macd, Bars);
    public ISeries<double> Leader => _leaderWrap ??= new IndicatorOutputSeries(_leader, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1) throw new ArgumentOutOfRangeException(nameof(Fast), "Periods must be at least 1.");
        _aFast = 2.0 / (1 + Fast);
        _aSlow = 2.0 / (1 + Slow);
        _i1 = _i2 = _diffFast = _diffSlow = 0;
        _hasValue = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double close)
    {
        if (!_hasValue)
        {
            _i1 = _i2 = close;
            _diffFast = _diffSlow = 0;
            _hasValue = true;
            _macd.Append(0);
            _leader.Append(0);
            return;
        }
        _i1 = _aFast * close + (1 - _aFast) * _i1;
        _i2 = _aSlow * close + (1 - _aSlow) * _i2;
        double macd = _i1 - _i2;
        _diffFast = _aFast * (close - _i1) + (1 - _aFast) * _diffFast;
        _diffSlow = _aSlow * (close - _i2) + (1 - _aSlow) * _diffSlow;
        double leader = macd + _diffFast - _diffSlow;
        _macd.Append(macd);
        _leader.Append(leader);
    }
}
