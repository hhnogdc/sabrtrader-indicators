using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ultimate Oscillator (Williams).
/// Defaults Fast=7, Intermediate=14, Slow=28.
/// BuyingPressure = Close − min(Low, prevClose).
/// TrueRange = max(H−L, H−prevClose, prevClose−L).
/// Value = (sumBP_Fast/sumTR_Fast · c1 + sumBP_Int/sumTR_Int · c2 + sumBP_Slow/sumTR_Slow) / c3 · 100
/// with c1=Slow/Fast, c2=Slow/Intermediate, c3=c1+c2+1.
/// Singularity sumTR_x == 0 → previous value.
/// Plots DodgerBlue. Lines 30, 50, 70 (DarkGray). Sub-panel.
/// </summary>
[IndicatorDescription("Larry Williams' Ultimate Oscillator. Combines buying-pressure / true-range ratios across three timeframes (Fast/Intermediate/Slow) into a single 0-100 momentum oscillator that flags divergences.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class UltimateOscillator : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingSum _bpFast = null!, _bpInt = null!, _bpSlow = null!;
    private RollingSum _trFast = null!, _trInt = null!, _trSlow = null!;
    private double _c1, _c2, _c3;
    private double? _prevClose;
    private double _prev;

    public UltimateOscillator(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("UO", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "30/50/70") });
        AddLine("Oversold",   30, PlotColors.DarkGray);
        AddLine("Neutral",    50, PlotColors.DarkGray);
        AddLine("Overbought", 70, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast",         Description = "Fast period.",         MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 7;

    [IndicatorParameter(DisplayName = "Intermediate", Description = "Intermediate period.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Intermediate { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Slow",         Description = "Slow period.",         MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 28;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1)         throw new ArgumentOutOfRangeException(nameof(Fast));
        if (Intermediate < 1) throw new ArgumentOutOfRangeException(nameof(Intermediate));
        if (Slow < 1)         throw new ArgumentOutOfRangeException(nameof(Slow));
        _bpFast = new RollingSum(Fast);
        _bpInt  = new RollingSum(Intermediate);
        _bpSlow = new RollingSum(Slow);
        _trFast = new RollingSum(Fast);
        _trInt  = new RollingSum(Intermediate);
        _trSlow = new RollingSum(Slow);
        _c1 = (double)Slow / Fast;
        _c2 = (double)Slow / Intermediate;
        _c3 = _c1 + _c2 + 1;
        _prevClose = null;
        _prev = 0;
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
        if (_prevClose is null) { _prevClose = bar.Close; _output.Append(0); return; }
        double pc = _prevClose.Value;
        double bp = bar.Close - Math.Min(bar.Low, pc);
        double tr = Math.Max(Math.Max(bar.High - bar.Low, bar.High - pc), pc - bar.Low);
        _bpFast.Push(bp); _bpInt.Push(bp); _bpSlow.Push(bp);
        _trFast.Push(tr); _trInt.Push(tr); _trSlow.Push(tr);
        _prevClose = bar.Close;

        if (_trFast.Sum == 0 || _trInt.Sum == 0 || _trSlow.Sum == 0) { _output.Append(_prev); return; }
        double v = (_bpFast.Sum / _trFast.Sum * _c1
                  + _bpInt.Sum  / _trInt.Sum  * _c2
                  + _bpSlow.Sum / _trSlow.Sum) / _c3 * 100;
        _output.Append(v);
        _prev = v;
    }
}
