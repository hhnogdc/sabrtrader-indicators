using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Volatility basis for the <see cref="Tma"/> upper/lower bands.
/// </summary>
public enum TmaBandBasis
{
    /// <summary>Band width = Multiplier · ATR(BandPeriod). The classic
    /// "TMA Bands" / "TMA Centered Bands" convention (and the basis used
    /// by the NT TDUTMABands product).</summary>
    Atr,

    /// <summary>Band width = Multiplier · population stddev of the input
    /// over BandPeriod. Bollinger-style bands placed around the TMA.</summary>
    StdDev
}

/// <summary>
/// Triangular Moving Average with bands. The middle line is a 1-on-1
/// port of NT @TMA (SMA of SMA): even Period → SMA(SMA(P/2), P/2+1),
/// odd Period → SMA(SMA((P+1)/2), (P+1)/2). Default Period=15, DodgerBlue.
///
/// Upper/Lower bands surround the middle line at
/// <c>Multiplier · volatility</c>, where volatility is either
/// ATR(BandPeriod) (default, the standard TMA-bands convention) or the
/// population standard deviation of the input over BandPeriod
/// (Bollinger-style), selected via <see cref="BandBasis"/>. Set
/// <see cref="Multiplier"/> to 0 to collapse the bands onto the middle.
/// </summary>
[IndicatorDescription("Triangular Moving Average with bands. A double-smoothed SMA (SMA of an SMA) that places maximum weight on the middle of the lookback window, flanked by upper/lower bands at Multiplier times ATR (default) or standard deviation. Bands expand in volatile regimes and contract in quiet ones, used for trend, breakout, and mean-reversion setups.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Tma : IndicatorBase
{
    private readonly ChunkedSeries<double> _output = new();
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _wrap;
    private IndicatorOutputSeries? _upperWrap;
    private IndicatorOutputSeries? _lowerWrap;
    private Sma _smaInner = null!;
    private Sma _smaOuter = null!;
    private Atr? _atr;                 // populated when BandBasis == Atr
    private RollingStdDev? _stdDev;    // populated when BandBasis == StdDev

    public Tma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("TMA",   _output, PlotStyle.Line, NTColors.DodgerBlue,     1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Upper", _upper,  PlotStyle.Line, NTColors.CornflowerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower", _lower,  PlotStyle.Line, NTColors.CornflowerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Total smoothing window for the middle line.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 15;

    [IndicatorParameter(DisplayName = "Band basis",
        Description = "Volatility measure for the bands: ATR (default) or population StdDev.")]
    public TmaBandBasis BandBasis { get; set; } = TmaBandBasis.Atr;

    [IndicatorParameter(DisplayName = "Multiplier",
        Description = "Band width multiplier on the volatility measure. 0 collapses the bands onto the middle line.",
        MinValue = 0, MaxValue = double.MaxValue, Step = 0.5)]
    public double Multiplier { get; set; } = 2.0;

    [IndicatorParameter(DisplayName = "Band period",
        Description = "Lookback for the band volatility measure (ATR or StdDev).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int BandPeriod { get; set; } = 14;

    public ISeries<double> Output => _wrap      ??= new IndicatorOutputSeries(_output, Bars);
    public ISeries<double> Upper  => _upperWrap ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Lower  => _lowerWrap ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (BandPeriod < 1) throw new ArgumentOutOfRangeException(nameof(BandPeriod));
        if (Multiplier < 0) throw new ArgumentOutOfRangeException(nameof(Multiplier));

        int p1, p2;
        if ((Period & 1) == 0) { p1 = Period / 2; p2 = p1 + 1; }
        else                   { p1 = (Period + 1) / 2; p2 = p1; }
        _smaInner = new Sma(this, input: Input) { Period = p1 };
        _smaOuter = new Sma(this, input: _smaInner.Output) { Period = p2 };

        // Volatility source for the bands. ATR rides as a child so the
        // framework cascades + backfills it before our OnDataLoaded /
        // OnBarUpdate read its output (same pattern as the SMA children).
        // StdDev is incremental and driven by hand in lockstep with bars.
        if (BandBasis == TmaBandBasis.Atr)
        {
            _atr = new Atr(this, bars: Bars) { Period = BandPeriod };
            _stdDev = null;
        }
        else
        {
            _stdDev = new RollingStdDev(BandPeriod);
            _atr = null;
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _smaOuter.Output.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
        {
            int barsAgo = n - 1 - absIdx;   // n-1 = oldest bar, 0 = newest
            double mid = _smaOuter.Output[barsAgo];

            double vol;
            if (BandBasis == TmaBandBasis.Atr)
            {
                vol = _atr!.Output[barsAgo];
            }
            else
            {
                // Push chronologically (oldest first) so the rolling
                // window matches the bar we're emitting.
                _stdDev!.Push(Input[barsAgo]);
                vol = _stdDev.StdDev;
            }

            EmitBands(mid, vol);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_smaOuter.Output.Count == 0) return;
        double mid = _smaOuter.Output[0];

        double vol;
        if (BandBasis == TmaBandBasis.Atr)
        {
            vol = _atr!.Output.Count > 0 ? _atr.Output[0] : 0.0;
        }
        else
        {
            if (Input.Count > 0) _stdDev!.Push(Input[0]);
            vol = _stdDev!.StdDev;
        }

        EmitBands(mid, vol);
    }

    private void EmitBands(double mid, double volatility)
    {
        double off = Multiplier * volatility;
        _output.Append(mid);
        _upper.Append(mid + off);
        _lower.Append(mid - off);
    }
}
