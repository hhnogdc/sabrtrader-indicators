using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

public enum ChandelierDirection { Long, Short }

/// <summary>
/// Chandelier Exit — 1-on-1 port of Skender Chandelier.
///   Long  : maxHigh(P) − Multiplier · ATR(P)
///   Short : minLow(P)  + Multiplier · ATR(P)
/// Defaults Period=22, Multiplier=3, Direction=Long. Goldenrod overlay.
/// Skender emits NaN for first Period bars.
/// </summary>
[IndicatorDescription("Chandelier Exit (Chuck LeBeau). Trailing stop hanging Multiplier x ATR below the highest high (Long) or above the lowest low (Short) over the lookback period. Designed to give trends room while exiting on sharp reversals.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Chandelier : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMinMax _max = null!, _min = null!;
    private double _atr;
    private bool _atrSeeded;
    private double _atrSeedSum;
    private int _atrSeedCount;
    private double? _prevClose;
    private int _barIdx;

    public Chandelier(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Chandelier Exit", _output, PlotStyle.Line, NTColors.Goldenrod, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period",     MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int    Period    { get; set; } = 22;

    [IndicatorParameter(DisplayName = "Multiplier", MinValue = 0.01, MaxValue = double.MaxValue, Step = 0.5)]
    public double Multiplier { get; set; } = 3;

    [IndicatorParameter(DisplayName = "Direction")]
    public ChandelierDirection Direction { get; set; } = ChandelierDirection.Long;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));
        _max = new RollingMinMax(Period, trackMax: true);
        _min = new RollingMinMax(Period, trackMax: false);
        _atrSeeded = false; _atrSeedSum = 0; _atrSeedCount = 0;
        _prevClose = null;
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
        _max.Push(bar.High);
        _min.Push(bar.Low);

        // Wilder ATR over Period.
        double tr = _prevClose is null
            ? bar.High - bar.Low
            : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _prevClose.Value), Math.Abs(bar.Low - _prevClose.Value)));
        _prevClose = bar.Close;
        if (!_atrSeeded)
        {
            _atrSeedSum += tr; _atrSeedCount++;
            if (_atrSeedCount >= Period) { _atr = _atrSeedSum / Period; _atrSeeded = true; }
        }
        else _atr = (_atr * (Period - 1) + tr) / Period;

        // Skender's `i >= lookbackPeriods` → emits from bar Period onwards.
        if (_barIdx < Period || !_atrSeeded) { _output.Append(double.NaN); _barIdx++; return; }

        double exit = Direction == ChandelierDirection.Long
            ? _max.Value - Multiplier * _atr
            : _min.Value + Multiplier * _atr;
        _output.Append(exit);
        _barIdx++;
    }
}
