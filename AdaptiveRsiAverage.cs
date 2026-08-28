using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Adaptive RSI Moving Average. An EMA-like
/// smoother whose step size is driven by how far RSI is from its 50
/// mid-line: near 50 (no momentum) the line barely moves; near 0 or 100
/// (strong momentum) it tracks price aggressively.
///
/// <code>
///   price'  = SmoothPrice ? EMA(price, PriceSmoothPeriod) : price
///   rsi     = RSI(price', RsiPeriod)        (optionally EMA-smoothed)
///   factor  = 2 · ScaleFactor · |rsi/100 − 0.5|
///   out     = bar ≤ RsiPeriod ? price'
///                             : (price' − prevOut) · factor + prevOut
/// </code>
///
/// Reuses the engine's <see cref="Rsi"/> as a child for the (non-trivial)
/// RSI; the two optional EMA smoothers are inlined as small NaN-safe steps
/// (a child EMA over RSI's NaN warm-up would be NaN-poisoned). Default
/// RsiPeriod=14, both smoothers on at period 10, ScaleFactor=0.5, overlay.
/// </summary>
[IndicatorDescription("Adaptive RSI Moving Average: an EMA-like smoother whose step size scales with |RSI-50| — flat when momentum is neutral, fast when RSI nears 0/100. Optional EMA smoothing of price and RSI; ScaleFactor caps the adaptation.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class AdaptiveRsiAverage : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _priceChunk = new();   // price fed to the child RSI
    private IndicatorOutputSeries? _wrap;
    private IndicatorOutputSeries? _priceWrap;

    private Rsi _rsi = null!;          // detached child — core RSI

    private double _priceEma; private bool _priceEmaHas; private double _alphaPrice;
    private double _rsiEma;   private bool _rsiEmaHas;   private double _alphaRsi;
    private double _value;            // previous output
    private int _barIdx;

    public AdaptiveRsiAverage(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("ARSI", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "RsiPeriod", Description = "RSI look-back driving the adaptation.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiPeriod { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Smooth RSI", Description = "EMA-smooth the RSI before deriving the factor.")]
    public bool SmoothRsi { get; set; } = true;

    [IndicatorParameter(DisplayName = "RsiSmoothPeriod", Description = "EMA period for the RSI smoother.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiSmoothPeriod { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Smooth Price", Description = "EMA-smooth the input price before the RSI.")]
    public bool SmoothPrice { get; set; } = true;

    [IndicatorParameter(DisplayName = "PriceSmoothPeriod", Description = "EMA period for the price smoother.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int PriceSmoothPeriod { get; set; } = 10;

    [IndicatorParameter(DisplayName = "ScaleFactor", Description = "Caps the adaptation: factor peaks at 2*ScaleFactor.",
        MinValue = 0.00000001, MaxValue = 2, Step = 0.1)]
    public double ScaleFactor { get; set; } = 0.5;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RsiPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RsiPeriod), "RsiPeriod must be at least 1.");
        if (RsiSmoothPeriod < 1) throw new ArgumentOutOfRangeException(nameof(RsiSmoothPeriod));
        if (PriceSmoothPeriod < 1) throw new ArgumentOutOfRangeException(nameof(PriceSmoothPeriod));
        if (ScaleFactor <= 0 || ScaleFactor > 2) throw new ArgumentOutOfRangeException(nameof(ScaleFactor), "ScaleFactor must be in (0, 2].");

        _alphaPrice = 2.0 / (PriceSmoothPeriod + 1.0);
        _alphaRsi = 2.0 / (RsiSmoothPeriod + 1.0);
        _priceEma = 0; _priceEmaHas = false;
        _rsiEma = 0; _rsiEmaHas = false;
        _value = 0;
        _barIdx = 0;

        // _priceChunk is an internal (non-plot) feed series, so the base
        // OnReset plot-clear does NOT touch it. Clear it here so a
        // parameter-edit recalc (ChartIndicatorRegistry.ResetForRecalc →
        // OnInit → OnDataLoaded) replays into an empty series instead of
        // appending a second copy on top of the first.
        _priceChunk.Clear();
        _priceWrap = new IndicatorOutputSeries(_priceChunk, Bars);
        _rsi = new Rsi(parent: null, input: _priceWrap) { Period = RsiPeriod };
        _rsi.OnInit(ctx);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        int n = src.Count;
        // Pass 1: build the (optionally smoothed) price series the RSI reads.
        for (int absIdx = 0; absIdx < n; absIdx++)
            _priceChunk.Append(SmoothPrice ? PriceEma(src[n - 1 - absIdx]) : src[n - 1 - absIdx]);
        // Backfill the child RSI over the full price series at once.
        _rsi.OnDataLoaded(ctx);
        // Pass 2: derive the adaptive output bar by bar.
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;
            Emit(_priceChunk[barsAgo], _rsi.Default[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var src = Input;
        if (src.Count == 0) return;
        _priceChunk.Append(SmoothPrice ? PriceEma(src[0]) : src[0]);
        _rsi.OnBarUpdate(ctx);
        Emit(_priceChunk[0], _rsi.Default[0]);
    }

    private void Emit(double calcValue, double rsiRaw)
    {
        double rsiValue = SmoothRsi ? RsiEma(rsiRaw) : rsiRaw;
        double factor = 2.0 * ScaleFactor * Math.Abs(rsiValue / 100.0 - 0.5);
        _value = _barIdx <= RsiPeriod ? calcValue : (calcValue - _value) * factor + _value;
        _output.Append(_value);
        _barIdx++;
    }

    private double PriceEma(double x)
    {
        if (!_priceEmaHas) { _priceEma = x; _priceEmaHas = true; return _priceEma; }
        _priceEma = _alphaPrice * x + (1.0 - _alphaPrice) * _priceEma;
        return _priceEma;
    }

    // NaN-safe: stays unseeded over the RSI's NaN warm-up, then seeds on
    // the first finite RSI — so no NaN ever poisons the smoother.
    private double RsiEma(double x)
    {
        if (double.IsNaN(x)) return _rsiEmaHas ? _rsiEma : double.NaN;
        if (!_rsiEmaHas) { _rsiEma = x; _rsiEmaHas = true; return _rsiEma; }
        _rsiEma = _alphaRsi * x + (1.0 - _alphaRsi) * _rsiEma;
        return _rsiEma;
    }
}
