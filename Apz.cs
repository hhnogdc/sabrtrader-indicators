using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Adaptive Price Zone.
/// emaEma = EMA(EMA(Input, sqrt(Period)), sqrt(Period)).
/// emaRange = EMA(Range, Period).
/// Bar &lt; Period → both bands = Input. Else:
/// Upper = emaEma + BandPct·emaRange; Lower = emaEma − BandPct·emaRange.
/// Defaults Period=20, BandPct=2. Plots Lower / Upper (Crimson), overlay.
/// </summary>
[IndicatorDescription("Adaptive Price Zone (Lee Leibfarth). Volatility-adjusted envelope around a double-smoothed EMA of price, with band width = BandPct multiplied by an EMA of bar range. Useful for spotting short-term overbought/oversold extremes.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Apz : IndicatorBase
{
    private readonly ChunkedSeries<double> _upper = new();
    private readonly ChunkedSeries<double> _lower = new();
    private readonly ChunkedSeries<double> _range = new();
    private IndicatorOutputSeries? _upW, _loW, _rW;
    private Ema _emaInner = null!;
    private Ema _emaOuter = null!;   // EMA(EMA(input, sqrt(P)), sqrt(P))
    private Ema _emaRange = null!;
    private int _barIdx;

    public Apz(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Lower", _lower, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper", _upper, PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "BandPct", Description = "Band multiplier on EMA(range).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 0.1)]
    public double BandPct { get; set; } = 2;

    [IndicatorParameter(DisplayName = "Period", Description = "Range EMA period; inner EMAs use sqrt(Period).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 20;

    public ISeries<double> Upper => _upW ??= new IndicatorOutputSeries(_upper, Bars);
    public ISeries<double> Lower => _loW ??= new IndicatorOutputSeries(_lower, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)  throw new ArgumentOutOfRangeException(nameof(Period));
        if (BandPct < 1) throw new ArgumentOutOfRangeException(nameof(BandPct));
        int sqrtP = Math.Max(1, (int)Math.Sqrt(Period));
        // Build double-EMA over close.
        _emaInner = new Ema(this, input: Bars.Close)        { Period = sqrtP };
        _emaOuter = new Ema(this, input: _emaInner.Output) { Period = sqrtP };
        _rW = new IndicatorOutputSeries(_range, Bars);
        // parent=null: parent-filled _rW; avoid auto-dispatch double-processing.
        _emaRange = new Ema(parent: null, input: _rW)      { Period = Period };
        _emaRange.OnInit(ctx);
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[bars.Count - 1 - i];
            _range.Append(bar.High - bar.Low);
        }
        _emaRange.OnDataLoaded(ctx);
        // Children _emaInner/_emaOuter already populated via cascade.
        int n = _emaOuter.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            if (_barIdx < Period)
            {
                double in0 = bars[b].Close;
                _upper.Append(in0); _lower.Append(in0);
            }
            else
            {
                double mid = _emaOuter.Output[b];
                double off = BandPct * _emaRange.Output[b];
                _upper.Append(mid + off); _lower.Append(mid - off);
            }
            _barIdx++;
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        var bars = Bars;
        if (bars.Count == 0) return;
        var bar = bars[0];
        _range.Append(bar.High - bar.Low);
        _emaRange.OnBarUpdate(ctx);
        if (_barIdx < Period)
        {
            double in0 = bar.Close;
            _upper.Append(in0); _lower.Append(in0);
        }
        else
        {
            double mid = _emaOuter.Output[0];
            double off = BandPct * _emaRange.Output[0];
            _upper.Append(mid + off); _lower.Append(mid - off);
        }
        _barIdx++;
    }
}
