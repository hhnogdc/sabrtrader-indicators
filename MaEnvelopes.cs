using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>MA flavour used by <see cref="MaEnvelopes"/>. Matches the original's integer mapping
/// (1=EMA, 2=HMA, 3=SMA, 4=TMA, 5=TEMA, 6=WMA).</summary>
public enum MaEnvelopesType
{
    Ema = 1, Hma = 2, Sma = 3, Tma = 4, Tema = 5, Wma = 6,
}

/// <summary>
/// Moving Average Envelopes.
/// Middle = MA(Input, Period) using one of 6 MA flavours.
/// Upper = Middle · (1 + EnvelopePercentage/100).
/// Lower = Middle · (1 − EnvelopePercentage/100).
/// Defaults Period=14, MAType=Sma, EnvelopePercentage=1.5.
/// Plots: Upper (DodgerBlue), Middle (DodgerBlue Dashed), Lower (DodgerBlue), overlay.
/// </summary>
[IndicatorDescription("Moving Average Envelopes: an MA midline (EMA/HMA/SMA/TMA/TEMA/WMA) with upper and lower bands at +/- a fixed percentage. Identifies stretched price relative to its average.")]
[IndicatorCategory(IndicatorCategory.Volatility)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MaEnvelopes : IndicatorBase
{
    private readonly ChunkedSeries<double> _upper  = new();
    private readonly ChunkedSeries<double> _middle = new();
    private readonly ChunkedSeries<double> _lower  = new();
    private IndicatorOutputSeries? _upW, _midW, _loW;
    private IndicatorBase _ma = null!;
    private ISeries<double> _maOut = null!;

    public MaEnvelopes(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Upper",  _upper,  PlotStyle.Line,   PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Middle", _middle, PlotStyle.Dashed, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        AddPlot(new Plot("Lower",  _lower,  PlotStyle.Line,   PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Band, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
    }

    [IndicatorParameter(DisplayName = "EnvelopePercentage", Description = "Band width as % of MA.",
        MinValue = 0.01, MaxValue = int.MaxValue, Step = 0.1)]
    public double EnvelopePercentage { get; set; } = 1.5;

    [IndicatorParameter(DisplayName = "MAType", Description = "MA flavour: Ema, Hma, Sma, Tma, Tema, Wma.")]
    public MaEnvelopesType MAType { get; set; } = MaEnvelopesType.Sma;

    [IndicatorParameter(DisplayName = "Period", Description = "MA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Upper  => _upW  ??= new IndicatorOutputSeries(_upper,  Bars);
    public ISeries<double> Middle => _midW ??= new IndicatorOutputSeries(_middle, Bars);
    public ISeries<double> Lower  => _loW  ??= new IndicatorOutputSeries(_lower,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        // HMA requires Period ≥ 2; the original clamps. Match.
        int p = MAType == MaEnvelopesType.Hma ? Math.Max(2, Period) : Period;
        _ma = MAType switch
        {
            MaEnvelopesType.Ema  => new Ema(this, input: Input)  { Period = p },
            MaEnvelopesType.Hma  => new Hma(this, input: Input)  { Period = p },
            MaEnvelopesType.Sma  => new Sma(this, input: Input)  { Period = p },
            MaEnvelopesType.Tma  => new Tma(this, input: Input)  { Period = p },
            MaEnvelopesType.Tema => new Tema(this, input: Input) { Period = p },
            MaEnvelopesType.Wma  => new Wma(this, input: Input)  { Period = p },
            _ => throw new ArgumentOutOfRangeException(nameof(MAType)),
        };
        _maOut = MAType switch
        {
            MaEnvelopesType.Ema  => ((Ema)_ma).Output,
            MaEnvelopesType.Hma  => ((Hma)_ma).Output,
            MaEnvelopesType.Sma  => ((Sma)_ma).Output,
            MaEnvelopesType.Tma  => ((Tma)_ma).Output,
            MaEnvelopesType.Tema => ((Tema)_ma).Output,
            MaEnvelopesType.Wma  => ((Wma)_ma).Output,
            _ => throw new ArgumentOutOfRangeException(nameof(MAType)),
        };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _maOut.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double m = _maOut[b];
            _middle.Append(m);
            _upper.Append(m + m * EnvelopePercentage / 100);
            _lower.Append(m - m * EnvelopePercentage / 100);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_maOut.Count == 0) return;
        double m = _maOut[0];
        _middle.Append(m);
        _upper.Append(m + m * EnvelopePercentage / 100);
        _lower.Append(m - m * EnvelopePercentage / 100);
    }
}
