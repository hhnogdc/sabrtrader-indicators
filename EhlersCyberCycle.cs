using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Ehlers Cyber Cycle (John Ehlers, 2002) — DSP-style oscillator that extracts the dominant
/// cyclical component from price.
///   smooth = (Price + 2·Price[1] + 2·Price[2] + Price[3]) / 6
///   α      = 2 / (Length + 1)
///   cycle  = (1 − α/2)² · (smooth − 2·smooth[1] + smooth[2])
///          + 2·(1 − α) · cycle[1] − (1 − α)² · cycle[2]
/// Bootstrap formula for bar &lt; 7 (insufficient lookback):
///   cycle  = (Price − 2·Price[1] + Price[2]) / 4
/// Signal line = cycle[1] (one-bar delay).
/// Default Length=20. Source = HL2.
/// Plots: Cycle (DodgerBlue), Signal (Crimson, lagged 1 bar). Zero line. Sub-panel.
/// </summary>
[IndicatorDescription("Ehlers Cyber Cycle: a DSP-style oscillator that extracts the dominant cyclical component from HL2 price via an alpha-weighted second-difference filter. Plotted with a 1-bar Signal line.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class EhlersCyberCycle : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _cycle  = new();
    private readonly ChunkedSeries<double> _signal = new();
    private IndicatorOutputSeries? _cW, _sW;

    private readonly double[] _priceHist  = new double[4];
    private readonly double[] _smoothHist = new double[4];
    private readonly double[] _cycleHist  = new double[3];
    private int _barIdx;

    public EhlersCyberCycle(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Cycle",  _cycle,  PlotStyle.Line, NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal", _signal, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Length", MinValue = 2, MaxValue = int.MaxValue, Step = 1)]
    public int Length { get; set; } = 20;

    public ISeries<double> Cycle  => _cW ??= new IndicatorOutputSeries(_cycle,  Bars);
    public ISeries<double> Signal => _sW ??= new IndicatorOutputSeries(_signal, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Length < 2) throw new ArgumentOutOfRangeException(nameof(Length));
        Array.Clear(_priceHist); Array.Clear(_smoothHist); Array.Clear(_cycleHist);
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
        double price = (bar.High + bar.Low) / 2;
        Shift(_priceHist); _priceHist[0] = price;

        double smooth = (_priceHist[0] + 2 * _priceHist[1] + 2 * _priceHist[2] + _priceHist[3]) / 6.0;
        Shift(_smoothHist); _smoothHist[0] = smooth;

        double alpha = 2d / (Length + 1);
        double cycle;
        if (_barIdx < 7)
        {
            // Bootstrap (Ehlers' published code uses this for the first 7 bars).
            cycle = (_priceHist[0] - 2 * _priceHist[1] + _priceHist[2]) / 4.0;
        }
        else
        {
            double k = 1 - alpha / 2;
            double k2 = k * k;
            double oneMinusAlpha = 1 - alpha;
            cycle = k2 * (_smoothHist[0] - 2 * _smoothHist[1] + _smoothHist[2])
                  + 2 * oneMinusAlpha * _cycleHist[0]
                  - oneMinusAlpha * oneMinusAlpha * _cycleHist[1];
        }
        Shift(_cycleHist); _cycleHist[0] = cycle;

        _cycle.Append(cycle);
        // Signal = cycle one bar ago.
        _signal.Append(_barIdx == 0 ? cycle : _cycleHist[1]);
        _barIdx++;
    }

    private static void Shift(double[] arr)
    {
        for (int i = arr.Length - 1; i > 0; i--) arr[i] = arr[i - 1];
    }
}
