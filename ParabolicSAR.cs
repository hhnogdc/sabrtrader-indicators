using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Parabolic SAR (Wilder).
/// Defaults Acceleration=0.02, AccelerationStep=0.02, AccelerationMax=0.2.
/// Plot Goldenrod 2px Dots, overlay. CurrentBar &lt; 3 skips.
/// </summary>
[IndicatorDescription("Wilder Parabolic SAR: trailing stop-and-reverse dots that accelerate toward price as a trend extends. Dots below price = up-trend, above = down-trend; a flip marks reversal.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class ParabolicSAR : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _af, _xp, _todaySar, _prevSar, _reverseValue;
    private bool _longPosition, _afIncreased;
    private int _reverseBar = -1;
    private int _barIdx;
    private double? _prevHigh1, _prevHigh2, _prevLow1, _prevLow2;

    public ParabolicSAR(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Parabolic SAR", _output, PlotStyle.Dots, PlotColors.Goldenrod, 2.0) { Semantics = new PlotSemantics(PlotKind.Signal, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Acceleration",     MinValue = 0,     MaxValue = double.MaxValue, Step = 0.01)]
    public double Acceleration { get; set; } = 0.02;

    [IndicatorParameter(DisplayName = "AccelerationMax",  MinValue = 0.001, MaxValue = double.MaxValue, Step = 0.01)]
    public double AccelerationMax { get; set; } = 0.2;

    [IndicatorParameter(DisplayName = "AccelerationStep", MinValue = 0.001, MaxValue = double.MaxValue, Step = 0.01)]
    public double AccelerationStep { get; set; } = 0.02;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _af = _xp = _todaySar = _prevSar = _reverseValue = 0;
        _longPosition = _afIncreased = false;
        _reverseBar = -1;
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
        double h0 = bar.High, l0 = bar.Low;
        if (_barIdx < 3)
        {
            // Track prior 2 bars for initialisation; emit 0 until we have enough.
            _prevHigh2 = _prevHigh1; _prevLow2 = _prevLow1;
            _prevHigh1 = h0; _prevLow1 = l0;
            _output.Append(0);
            if (_barIdx == 2)
            {
                // Initialise on bar 3 (using H[0] vs H[1] convention from the original).
                _longPosition = (_prevHigh1 ?? 0) > (_prevHigh2 ?? 0);
                _xp = _longPosition ? Math.Max(_prevHigh1 ?? 0, _prevHigh2 ?? 0)
                                    : Math.Min(_prevLow1 ?? 0,  _prevLow2 ?? 0);
                _af = Acceleration;
            }
            _barIdx++;
            return;
        }

        // Reset af-increased on new bar.
        if (_afIncreased) _afIncreased = false;
        double prevVal = _output[0];

        if (_reverseBar != _barIdx)
        {
            double sar = prevVal + _af * (_xp - prevVal);
            // Today SAR not inside previous 2 bars' range.
            sar = ClampSar(sar);
            for (int x = 1; x <= 2; x++)
            {
                double l = x == 1 ? (_prevLow1 ?? l0) : (_prevLow2 ?? l0);
                double h = x == 1 ? (_prevHigh1 ?? h0) : (_prevHigh2 ?? h0);
                if (_longPosition && sar > l) sar = l;
                if (!_longPosition && sar < h) sar = h;
            }
            _todaySar = sar;
            _output.Append(sar);
            if (_longPosition && h0 > _xp) { _xp = h0; AfIncrease(); }
            else if (!_longPosition && l0 < _xp) { _xp = l0; AfIncrease(); }
            _prevSar = sar;
        }
        else
        {
            if (_longPosition && h0 > _xp) _xp = h0;
            else if (!_longPosition && l0 < _xp) _xp = l0;
            _output.Append(_prevSar);
            _todaySar = _longPosition ? Math.Min(_reverseValue, l0) : Math.Max(_reverseValue, h0);
        }

        // Reverse position when penetrated.
        double prevL = _prevLow1 ?? l0, prevH = _prevHigh1 ?? h0;
        if ((_longPosition && (l0 < _todaySar || prevL < _todaySar))
            || (!_longPosition && (h0 > _todaySar || prevH > _todaySar)))
        {
            _longPosition = !_longPosition;
            _reverseBar = _barIdx;
            _reverseValue = _xp;
            _af = Acceleration;
            _xp = _longPosition ? h0 : l0;
            // Override last emit with reversed SAR.
            // (the original keeps Value[0] = previous xp, which is _prevSar from before the reverse.)
        }

        _prevHigh2 = _prevHigh1; _prevLow2 = _prevLow1;
        _prevHigh1 = h0; _prevLow1 = l0;
        _barIdx++;
    }

    private void AfIncrease()
    {
        if (!_afIncreased)
        {
            _af = Math.Min(AccelerationMax, _af + AccelerationStep);
            _afIncreased = true;
        }
    }

    private double ClampSar(double tSar)
    {
        double h0 = _prevHigh1 ?? tSar, l0 = _prevLow1 ?? tSar;
        if (_longPosition)
        {
            double lo = Math.Min(Math.Min(tSar, l0), _prevLow2 ?? l0);
            if (l0 > lo) tSar = lo;
        }
        else
        {
            double hi = Math.Max(Math.Max(tSar, h0), _prevHigh2 ?? h0);
            if (h0 < hi) tSar = hi;
        }
        return tSar;
    }
}
