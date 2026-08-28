using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Average Directional Index (ADX).
/// Single plot (DarkCyan). Two reference lines: 25 SlateBlue ("Strong
/// Trend"), 75 Goldenrod ("Very Strong Trend").
///
/// the original formula: bar 0 → ADX=50 (no math possible without a prior bar).
/// While CurrentBar &lt; Period: simple cumulative sums of TR, +DM, -DM.
/// Once at/past Period: Wilder smoothing of those sums
/// <c>sum_t = sum_{t-1} − sum_{t-1}/Period + new</c>. Then
/// <c>+DI = 100·sumDM+/sumTR</c>, <c>-DI = 100·sumDM−/sumTR</c>,
/// <c>diff = |+DI − −DI|</c>, <c>sum = +DI + −DI</c>;
/// <c>ADX = ((Period−1)·prevADX + 100·diff/sum) / Period</c>.
/// Singularity sum=0 → ADX=50.
///
/// Default Period=14. Range [1, int.MaxValue]. Sub-panel.
/// </summary>
[IndicatorDescription("Average Directional Index (Wilder). Measures trend strength regardless of direction by smoothing the difference between +DI and -DI. Values above 25 indicate a strong trend, above 75 a very strong trend.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Adx : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _adx = new();
    private IndicatorOutputSeries? _outputAsBarSourced;

    // Per-bar TR/DM+/DM- and their running sums — Series<double>
    // equivalents from the original's reference impl. We use plain doubles
    // because we only need the previous bar's values.
    private double _sumTr, _sumDmPlus, _sumDmMinus;
    private double _adxValue;
    private double? _prevHigh, _prevLow, _prevClose;
    private int _barIdx;

    public Adx(IIndicator? parent = null, IBarSeries? bars = null)
        : base(parent, bars: bars)
    {
        AddPlot(new Plot("ADX", _adx, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, "25 strong / 75 very strong trend") });
        // ADX is unbounded and usually reads 10-30; the 25/75 reference lines
        // must NOT stretch the panel scale (else a 75 line compresses the data
        // into a sliver). The original draws them without driving the scale.
        AddLine("Strong Trend",      25, new ChartColor(0x6A, 0x5A, 0xCD), participatesInAutoScale: false);  // SlateBlue
        AddLine("Very Strong Trend", 75, PlotColors.Goldenrod,               participatesInAutoScale: false);
    }

    [IndicatorParameter(DisplayName = "Period",
        Description = "Wilder period for TR / DM± smoothing and ADX recurrence.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    /// <summary>ADX as a chainable bar-sourced series.</summary>
    public ISeries<double> Output =>
        _outputAsBarSourced ??= new IndicatorOutputSeries(_adx, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1)
            throw new ArgumentOutOfRangeException(nameof(Period), "ADX period must be at least 1.");
        _sumTr = _sumDmPlus = _sumDmMinus = 0;
        _adxValue = 50;
        _prevHigh = _prevLow = _prevClose = null;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var bars = Bars;
        for (int absIdx = 0; absIdx < bars.Count; absIdx++)
        {
            int barsAgo = bars.Count - 1 - absIdx;
            ConsumeBar(bars[barsAgo]);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        var bars = Bars;
        if (bars.Count == 0) return;
        ConsumeBar(bars[0]);
    }

    private void ConsumeBar(Bar bar)
    {
        double high0 = bar.High;
        double low0  = bar.Low;
        if (_prevHigh is null)
        {
            // Bar 0: The original seeds sumTR = TR (= high - low), sumDM = 0, ADX = 50.
            _sumTr = high0 - low0;
            _sumDmPlus = 0;
            _sumDmMinus = 0;
            _adxValue = 50;
            _prevHigh = high0;
            _prevLow = low0;
            _prevClose = bar.Close;
            _barIdx = 1;
            _adx.Append(_adxValue);
            return;
        }

        double prevHigh  = _prevHigh!.Value;
        double prevLow   = _prevLow!.Value;
        double prevClose = _prevClose!.Value;

        double tr = Math.Max(Math.Abs(low0 - prevClose),
                    Math.Max(high0 - low0, Math.Abs(high0 - prevClose)));
        double dmPlus  = (high0 - prevHigh > prevLow - low0) ? Math.Max(high0 - prevHigh, 0) : 0;
        double dmMinus = (prevLow - low0  > high0 - prevHigh)  ? Math.Max(prevLow - low0,  0) : 0;

        if (_barIdx < Period)
        {
            _sumTr      += tr;
            _sumDmPlus  += dmPlus;
            _sumDmMinus += dmMinus;
        }
        else
        {
            // Wilder smoothing in sum form.
            _sumTr      = _sumTr      - _sumTr      / Period + tr;
            _sumDmPlus  = _sumDmPlus  - _sumDmPlus  / Period + dmPlus;
            _sumDmMinus = _sumDmMinus - _sumDmMinus / Period + dmMinus;
        }

        double diPlus  = _sumTr == 0 ? 0 : 100 * _sumDmPlus  / _sumTr;
        double diMinus = _sumTr == 0 ? 0 : 100 * _sumDmMinus / _sumTr;
        double diff    = Math.Abs(diPlus - diMinus);
        double sum     = diPlus + diMinus;

        _adxValue = sum == 0
            ? 50
            : ((Period - 1) * _adxValue + 100 * diff / sum) / Period;

        _prevHigh = high0;
        _prevLow  = low0;
        _prevClose = bar.Close;
        _barIdx++;
        _adx.Append(_adxValue);
    }
}
