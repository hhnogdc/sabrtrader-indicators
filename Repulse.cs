using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Repulse — a bull-vs-bear pressure oscillator (Eric Lefort). It
/// contrasts how strongly the close sits above the window's lows
/// (buying repulsion) against how strongly it sits below the window's
/// highs (selling repulsion), normalised by price so it is comparable
/// across instruments.
///
/// Math (over the trailing <c>Period</c> bars):
///   <code>
///   BullPower = 100 · (3·Close − 2·LowestLow  − OpenOldest) / Close
///   BearPower = 100 · (2·HighestHigh − 3·Close + OpenOldest) / Close
///   Repulse   = EMA(BullPower − BearPower, Smooth)
///   </code>
/// where <c>OpenOldest</c> is the open of the oldest bar in the window.
/// Positive = net buying pressure, negative = net selling pressure.
///
/// Defaults Period=5, Smooth=5. Single plot (DodgerBlue), zero
/// centerline, sub-panel.
/// </summary>
[IndicatorDescription("Repulse (Lefort): contrasts the close's distance above the window lows (bull power) against its distance below the window highs (bear power), price-normalised and EMA-smoothed. Above zero = net buying pressure, below = net selling pressure.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Repulse : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private RollingMinMax _hh = null!;
    private RollingMinMax _ll = null!;
    private double[] _opens = [];
    private int _count, _writeIdx;
    private double _ema, _alpha;
    private bool _hasEma;

    public Repulse(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Repulse", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bull/bear pressure lookback window.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 5;

    [IndicatorParameter(DisplayName = "Smooth", Description = "EMA smoothing applied to the raw pressure.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Smooth { get; set; } = 5;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Smooth < 1) throw new ArgumentOutOfRangeException(nameof(Smooth));
        _hh = new RollingMinMax(Period, trackMax: true);
        _ll = new RollingMinMax(Period, trackMax: false);
        _opens = new double[Period];
        _count = 0;
        _writeIdx = 0;
        _alpha = 2.0 / (Smooth + 1);
        _ema = 0;
        _hasEma = false;
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
        _hh.Push(bar.High);
        _ll.Push(bar.Low);
        _opens[_writeIdx] = bar.Open;
        _writeIdx = (_writeIdx + 1) % Period;
        if (_count < Period) _count++;
        // After advancing, _writeIdx points at the oldest slot once the window is full.
        double oldestOpen = _count < Period ? _opens[0] : _opens[_writeIdx];

        double close = bar.Close;
        double raw;
        if (close == 0)
        {
            raw = 0;
        }
        else
        {
            double bull = 100.0 * (3 * close - 2 * _ll.Value - oldestOpen) / close;
            double bear = 100.0 * (2 * _hh.Value - 3 * close + oldestOpen) / close;
            raw = bull - bear;
        }

        if (!_hasEma) { _ema = raw; _hasEma = true; }
        else           _ema = _alpha * raw + (1 - _alpha) * _ema;
        _output.Append(_ema);
    }
}
