using System;
using System.Collections.Generic;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Swing — 1-on-1 port of NT @Swing.
/// Detects swing highs/lows where the candidate is the unique extreme over a window
/// of <c>2·Strength+1</c> bars (left bars strictly less, right bars strictly less-or-equal).
/// Maintains:
///   • Two plot series (SwingHigh, SwingLow) — Dot 2px, DarkCyan / Goldenrod, overlay.
///     NaN when the swing has been invalidated (price exceeded last high / fell under last low).
///   • Two sub-series (SwingHighSeries / SwingLowSeries) — carry the LAST detected swing
///     level forward indefinitely (never NaN once a swing has been seen).
///   • Two pivot-occurrence sub-series (SwingHighSwings / SwingLowSwings) — value at the
///     Strength-bar-back slot when a swing is detected, else 0. Used by SwingHighBar /
///     SwingLowBar lookups.
/// Default Strength=5.
/// </summary>
[IndicatorDescription("Detects swing-high and swing-low pivots where a bar is the unique extreme across a 2*Strength+1 window. Plots pivot dots and exposes the last-seen swing level forward.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Swing : IndicatorBase
{
    private readonly ChunkedSeries<double> _swingHighPlot = new();
    private readonly ChunkedSeries<double> _swingLowPlot  = new();
    private readonly ChunkedSeries<double> _swingHighSeries = new();
    private readonly ChunkedSeries<double> _swingLowSeries  = new();
    private readonly ChunkedSeries<double> _swingHighSwings = new();
    private readonly ChunkedSeries<double> _swingLowSwings  = new();
    private IndicatorOutputSeries? _shPlotW, _slPlotW, _shW, _slW, _shSwW, _slSwW;

    private readonly List<double> _highCache = new();
    private readonly List<double> _lowCache  = new();
    private double _currentSwingHigh;
    private double _currentSwingLow;
    private double _lastSwingHighValue;
    private double _lastSwingLowValue;
    private int _constant;
    private int _barIdx;

    public Swing(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Swing High", _swingHighPlot, PlotStyle.Dots, NTColors.DarkCyan,   2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Swing Low",  _swingLowPlot,  PlotStyle.Dots, NTColors.Goldenrod, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Strength", Description = "Bars on each side of the pivot.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Strength { get; set; } = 5;

    public ISeries<double> SwingHighPlot   => _shPlotW ??= new IndicatorOutputSeries(_swingHighPlot, Bars);
    public ISeries<double> SwingLowPlot    => _slPlotW ??= new IndicatorOutputSeries(_swingLowPlot,  Bars);
    public ISeries<double> SwingHighSeries => _shW     ??= new IndicatorOutputSeries(_swingHighSeries, Bars);
    public ISeries<double> SwingLowSeries  => _slW     ??= new IndicatorOutputSeries(_swingLowSeries,  Bars);
    public ISeries<double> SwingHighSwings => _shSwW   ??= new IndicatorOutputSeries(_swingHighSwings, Bars);
    public ISeries<double> SwingLowSwings  => _slSwW   ??= new IndicatorOutputSeries(_swingLowSwings,  Bars);

    /// <summary>
    /// Returns the number of bars ago a swing high occurred (1-based instance), or -1 if not found
    /// within <paramref name="lookBackPeriod"/>. Mirrors NT Swing.SwingHighBar.
    /// </summary>
    public int SwingHighBar(int barsAgo, int instance, int lookBackPeriod)
    {
        if (instance < 1) throw new ArgumentException("instance must be ≥ 1", nameof(instance));
        if (barsAgo < 0)  throw new ArgumentException("barsAgo must be ≥ 0", nameof(barsAgo));
        int start = barsAgo + Strength;
        int end   = barsAgo + Strength + lookBackPeriod;
        for (int b = start; b <= end; b++)
        {
            if (b >= _swingHighSwings.Count) return -1;
            if (_swingHighSwings[b] == 0) continue;
            if (instance == 1) return b;
            instance--;
        }
        return -1;
    }

    /// <summary>Mirrors NT Swing.SwingLowBar.</summary>
    public int SwingLowBar(int barsAgo, int instance, int lookBackPeriod)
    {
        if (instance < 1) throw new ArgumentException("instance must be ≥ 1", nameof(instance));
        if (barsAgo < 0)  throw new ArgumentException("barsAgo must be ≥ 0", nameof(barsAgo));
        int start = barsAgo + Strength;
        int end   = barsAgo + Strength + lookBackPeriod;
        for (int b = start; b <= end; b++)
        {
            if (b >= _swingLowSwings.Count) return -1;
            if (_swingLowSwings[b] == 0) continue;
            if (instance == 1) return b;
            instance--;
        }
        return -1;
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Strength < 1) throw new ArgumentOutOfRangeException(nameof(Strength));
        _constant = 2 * Strength + 1;
        _currentSwingHigh = 0;
        _currentSwingLow = 0;
        _lastSwingHighValue = 0;
        _lastSwingLowValue = 0;
        _barIdx = 0;
        _highCache.Clear();
        _lowCache.Clear();
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
        double h0 = bar.High, l0 = bar.Low, c0 = bar.Close;

        // Default-append all sub-series so the indexers (with barsAgo offsets) work.
        _swingHighSwings.Append(0);
        _swingLowSwings.Append(0);
        _swingHighSeries.Append(_lastSwingHighValue);
        _swingLowSeries.Append(_lastSwingLowValue);
        _swingHighPlot.Append(double.NaN);
        _swingLowPlot.Append(double.NaN);

        // Push into rolling caches.
        _highCache.Add(h0);
        if (_highCache.Count > _constant) _highCache.RemoveAt(0);
        _lowCache.Add(l0);
        if (_lowCache.Count > _constant) _lowCache.RemoveAt(0);

        if (_highCache.Count == _constant)
        {
            double candidateHigh = _highCache[Strength];
            bool isSwingHigh = true;
            for (int i = 0; i < Strength; i++)
                if (_highCache[i] >= candidateHigh) { isSwingHigh = false; break; }
            if (isSwingHigh)
                for (int i = Strength + 1; i < _highCache.Count; i++)
                    if (_highCache[i] > candidateHigh) { isSwingHigh = false; break; }

            // The candidate sits Strength bars back from _barIdx.
            int candidateBarsAgo = Strength;
            if (isSwingHigh)
            {
                _swingHighSwings.Set(candidateBarsAgo, candidateHigh);
                _lastSwingHighValue = candidateHigh;
                _currentSwingHigh = candidateHigh;
                // Back-fill plot + sub-series for the last Strength+1 bars to the swing level.
                for (int i = 0; i <= Strength; i++)
                {
                    _swingHighPlot.Set(i, candidateHigh);
                    _swingHighSeries.Set(i, candidateHigh);
                }
            }
            else if (h0 > _currentSwingHigh || _currentSwingHigh == 0)
            {
                _currentSwingHigh = 0;
                _swingHighPlot.Set(0, double.NaN);
            }
            else
            {
                _swingHighPlot.Set(0, _currentSwingHigh);
            }
        }

        if (_lowCache.Count == _constant)
        {
            double candidateLow = _lowCache[Strength];
            bool isSwingLow = true;
            for (int i = 0; i < Strength; i++)
                if (_lowCache[i] <= candidateLow) { isSwingLow = false; break; }
            if (isSwingLow)
                for (int i = Strength + 1; i < _lowCache.Count; i++)
                    if (_lowCache[i] < candidateLow) { isSwingLow = false; break; }

            int candidateBarsAgo = Strength;
            if (isSwingLow)
            {
                _swingLowSwings.Set(candidateBarsAgo, candidateLow);
                _lastSwingLowValue = candidateLow;
                _currentSwingLow = candidateLow;
                for (int i = 0; i <= Strength; i++)
                {
                    _swingLowPlot.Set(i, candidateLow);
                    _swingLowSeries.Set(i, candidateLow);
                }
            }
            else if (l0 < _currentSwingLow || _currentSwingLow == 0)
            {
                _currentSwingLow = 0;
                _swingLowPlot.Set(0, double.NaN);
            }
            else
            {
                _swingLowPlot.Set(0, _currentSwingLow);
            }
        }

        _barIdx++;
    }
}
