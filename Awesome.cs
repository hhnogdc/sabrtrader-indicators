using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Awesome Oscillator (Bill Williams) — 1-on-1 port of Skender Awesome.
/// Source = HL2 (Median price). AO = SMA(HL2, Fast) − SMA(HL2, Slow).
/// Normalized = 100·AO/HL2 (NaN when HL2 = 0).
/// Defaults Fast=5, Slow=34. DarkCyan histogram, zero line, sub-panel.
/// NaN before bar Slow−1.
/// </summary>
[IndicatorDescription("Awesome Oscillator (Bill Williams). Momentum histogram equal to SMA(median price, 5) minus SMA(median price, 34). Crosses of the zero line and twin-peak patterns signal momentum shifts.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Awesome : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _normalized = new();
    private IndicatorOutputSeries? _wrap, _normW;
    private RollingSum _fastSum = null!, _slowSum = null!;

    public Awesome(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Awesome", _output, PlotStyle.Histogram, NTColors.DarkCyan, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, null, null, PlotValueUnit.Raw, PlotBullishDirection.Up, null) });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Fast", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Fast { get; set; } = 5;

    [IndicatorParameter(DisplayName = "Slow", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Slow { get; set; } = 34;

    public ISeries<double> Output     => _wrap  ??= new IndicatorOutputSeries(_output,     Bars);
    public ISeries<double> Normalized => _normW ??= new IndicatorOutputSeries(_normalized, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Fast < 1 || Slow < 1 || Fast >= Slow) throw new ArgumentOutOfRangeException();
        _fastSum = new RollingSum(Fast);
        _slowSum = new RollingSum(Slow);
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
        double hl2 = (bar.High + bar.Low) / 2.0;
        _fastSum.Push(hl2);
        _slowSum.Push(hl2);
        if (_slowSum.Count < Slow)
        {
            _output.Append(double.NaN);
            _normalized.Append(double.NaN);
            return;
        }
        double ao = _fastSum.Sum / Fast - _slowSum.Sum / Slow;
        _output.Append(ao);
        _normalized.Append(hl2 != 0 ? 100 * ao / hl2 : double.NaN);
    }
}
