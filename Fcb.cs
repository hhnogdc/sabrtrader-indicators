using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Fractal Chaos Bands (FCB) — 1-on-1 port of Skender Fcb.
/// Tracks the most recent Williams Fractal pivot for the upper (bearish pivot = price ceiling)
/// and lower (bullish pivot = price floor) bands, held forward until a new pivot fires.
/// Defaults WindowSpan=2.
/// Plots: UpperBand (Red), LowerBand (LimeGreen). Overlay.
/// </summary>
[IndicatorDescription("Fractal Chaos Bands: holds the most recent Williams Fractal high as the upper band and the most recent fractal low as the lower band, extending each level forward until a new pivot fires.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Fcb : IndicatorBase
{
    private readonly ChunkedSeries<double> _upper = new();
    private readonly ChunkedSeries<double> _lower = new();
    private IndicatorOutputSeries? _uW, _lW;
    private Fractal _fractal = null!;
    private double _upperLine;
    private double _lowerLine;
    private bool _hasUpper, _hasLower;
    private int _barIdx;

    public Fcb(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("FCB Upper", _upper, PlotStyle.Line, NTColors.Red,       1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("FCB Lower", _lower, PlotStyle.Line, NTColors.LimeGreen, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "WindowSpan", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int WindowSpan { get; set; } = 2;

    public ISeries<double> Upper => _uW ??= new IndicatorOutputSeries(_upper, Bars);
    public ISeries<double> Lower => _lW ??= new IndicatorOutputSeries(_lower, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (WindowSpan < 1) throw new ArgumentOutOfRangeException(nameof(WindowSpan));
        // Fractal as child of `this` (it reads Bars directly — safe under cascade).
        _fractal = new Fractal(this, bars: Bars) { LeftSpan = WindowSpan, RightSpan = WindowSpan, EndType = FractalEndType.HighLow };
        _hasUpper = _hasLower = false;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Child Fractal already backfilled by the cascade — walk forward and lift pivot values.
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++) Step(bars.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Bars.Count == 0) return;
        Step(0);
    }

    private void Step(int barsAgoOfCurrent)
    {
        // Skender uses fractals[i - windowSpan]; we mirror by reading the fractal value at WindowSpan bars-ago
        // (the actual pivot bar). Skender's bandwidth condition: i >= 2*WindowSpan ensures both fractal
        // detection lag and lookback are satisfied.
        if (_barIdx >= 2 * WindowSpan)
        {
            int pivotBarsAgo = barsAgoOfCurrent + WindowSpan;
            if (pivotBarsAgo < _fractal.FractalBear.Count)
            {
                double bear = _fractal.FractalBear[pivotBarsAgo];
                double bull = _fractal.FractalBull[pivotBarsAgo];
                if (!double.IsNaN(bear)) { _upperLine = bear; _hasUpper = true; }
                if (!double.IsNaN(bull)) { _lowerLine = bull; _hasLower = true; }
            }
        }
        _upper.Append(_hasUpper ? _upperLine : double.NaN);
        _lower.Append(_hasLower ? _lowerLine : double.NaN);
        _barIdx++;
    }
}
