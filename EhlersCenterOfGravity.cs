using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ehlers Center of Gravity (CG) Oscillator — John Ehlers, 2002.
/// A zero-lag rephrasing of price into the lag at which the "weighted balance" of the last
/// Length bars sits:
///   numerator   = Σ (1 + k) · price[k]   for k = 0..Length−1 (k=0 newest)
///   denominator = Σ price[k]
///   CG = − numerator / denominator + (Length + 1) / 2
/// Signal = CG[1] (one-bar delay).
/// Source = HL2. Default Length=10.
/// Plots: CG (DodgerBlue), Signal (Crimson). Zero reference line. Sub-panel.
/// </summary>
[IndicatorDescription("Ehlers Center of Gravity oscillator: a near-zero-lag indicator expressing where the weighted balance of the last Length bars (on HL2) sits. Plotted with a 1-bar Signal line for crossovers.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class EhlersCenterOfGravity : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _cg     = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _cW, _sW;

    private double[] _ring = null!;
    private int _head, _count;

    public EhlersCenterOfGravity(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("CG",     _cg,     PlotStyle.Line, NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Length", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 10;

    public ISeries<double> Output => _cW ??= new IndicatorOutputSeries(_cg,     Bars);
    public ISeries<double> Signal => _sW ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 2) throw new ArgumentOutOfRangeException(nameof(Length));
        _ring = new double[Length];
        _head = _count = 0;
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
        double price = (bar.High + bar.Low) / 2;
        _ring[_head] = price;
        _head = (_head + 1) % Length;
        if (_count < Length) _count++;

        if (_count < Length) { _cg.Append(double.NaN); _signal.Append(double.NaN); return; }

        // Walk newest→oldest: k=0 is newest sample.
        double num = 0, denom = 0;
        for (int k = 0; k < Length; k++)
        {
            int idx = (_head - 1 - k + Length) % Length;
            double p = _ring[idx];
            num   += (1 + k) * p;
            denom += p;
        }
        double cg = denom == 0 ? 0 : -num / denom + (Length + 1) / 2.0;
        _cg.Append(cg);
        // Signal = previous cg.
        _signal.Append(_cg.Count >= 2 ? _cg[1] : cg);
    }
}
