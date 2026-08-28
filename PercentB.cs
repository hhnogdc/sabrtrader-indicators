using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// %B for Bollinger Bands.
/// %B = (close − Lower) / (Upper − Lower). Above 1 = above upper band,
/// below 0 = below lower band.
/// Defaults Period=14, NumStdDev=2, DodgerBlue, sub-panel,
/// 0 / 1 reference lines DarkGray.
/// </summary>
[IndicatorDescription("Bollinger %B: position of price inside its Bollinger Bands as (Close - Lower) / (Upper - Lower). Above 1 = above upper band, below 0 = below lower band.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class PercentB : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private BollingerBands _bb = null!;

    public PercentB(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("%B", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 oversold / 1 overbought") });
        AddLine("Upper", 1, PlotColors.DarkGray);
        AddLine("Lower", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Bollinger window.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "NumStdDev", Description = "Bollinger stddev multiplier.",
        MinValue = 0, MaxValue = int.MaxValue, Step = 0.5)]
    public double NumStdDev { get; set; } = 2.0;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _bb = new BollingerBands(this, input: Input) { Period = Period, NumStdDev = NumStdDev };
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _bb.Middle.Count;
        var src = Input;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double u = _bb.Upper[b], l = _bb.Lower[b];
            double denom = u - l;
            _output.Append(denom == 0 ? 0 : (src[b] - l) / denom);
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child on the live bar
        if (_bb.Middle.Count == 0) return;
        double u = _bb.Upper[0], l = _bb.Lower[0];
        double denom = u - l;
        _output.Append(denom == 0 ? 0 : (Input[0] - l) / denom);
    }
}
