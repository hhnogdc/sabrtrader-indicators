using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>MA flavour for <see cref="MovingAverageRibbon"/>. Matches NT's RibbonMAType.</summary>
public enum RibbonMAType { Exponential = 0, Hull = 1, Simple = 2, Weighted = 3 }

/// <summary>
/// Moving Average Ribbon — 1-on-1 port of NT @MovingAverageRibbon.
/// 8 MAs at periods BasePeriod, BasePeriod+IncrementalPeriod, …, gradient
/// Yellow → Red. Defaults MovingAverage=Exponential, BasePeriod=10,
/// IncrementalPeriod=10. Overlay.
/// </summary>
[IndicatorDescription("Moving Average Ribbon: eight MAs (EMA/HMA/SMA/WMA) at increasing periods, coloured yellow to red. Spread shows trend strength; compression flags consolidation.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class MovingAverageRibbon : IndicatorBase
{
    private const int Count = 8;
    private readonly ChunkedSeries<double>[] _outs = new ChunkedSeries<double>[Count];
    private IndicatorBase[] _mas = null!;
    private static readonly ChartColor[] _colors =
    {
        NTColors.Yellow, NTColors.Gold, NTColors.Goldenrod, NTColors.Orange,
        NTColors.DarkOrange, NTColors.Chocolate, NTColors.OrangeRed, NTColors.Red
    };

    public MovingAverageRibbon(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        for (int i = 0; i < Count; i++)
        {
            _outs[i] = new ChunkedSeries<double>();
            AddPlot(new Plot($"MA{i + 1}", _outs[i], PlotStyle.Line, _colors[i], 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        }
    }

    [IndicatorParameter(DisplayName = "MovingAverage")]
    public RibbonMAType MovingAverage { get; set; } = RibbonMAType.Exponential;

    [IndicatorParameter(DisplayName = "BasePeriod",        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int BasePeriod { get; set; } = 10;

    [IndicatorParameter(DisplayName = "IncrementalPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int IncrementalPeriod { get; set; } = 10;

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (BasePeriod < 1 || IncrementalPeriod < 1) throw new ArgumentOutOfRangeException();
        _mas = new IndicatorBase[Count];
        for (int i = 0; i < Count; i++)
        {
            int p = BasePeriod + IncrementalPeriod * i;
            _mas[i] = MovingAverage switch
            {
                RibbonMAType.Exponential => new Ema(this, input: Input) { Period = p },
                RibbonMAType.Hull        => new Hma(this, input: Input) { Period = Math.Max(2, p) },
                RibbonMAType.Simple      => new Sma(this, input: Input) { Period = p },
                RibbonMAType.Weighted    => new Wma(this, input: Input) { Period = p },
                _ => throw new ArgumentOutOfRangeException(nameof(MovingAverage)),
            };
        }
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        for (int i = 0; i < Count; i++) MirrorOutputs(i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        for (int i = 0; i < Count; i++)
        {
            var src = MaOutput(i);
            if (src.Count > 0) _outs[i].Append(src[0]);
        }
    }

    private void MirrorOutputs(int i)
    {
        var src = MaOutput(i);
        int n = src.Count;
        for (int j = 0; j < n; j++) _outs[i].Append(src[n - 1 - j]);
    }

    private ISeries<double> MaOutput(int i) => MovingAverage switch
    {
        RibbonMAType.Exponential => ((Ema)_mas[i]).Output,
        RibbonMAType.Hull        => ((Hma)_mas[i]).Output,
        RibbonMAType.Simple      => ((Sma)_mas[i]).Output,
        RibbonMAType.Weighted    => ((Wma)_mas[i]).Output,
        _ => throw new ArgumentOutOfRangeException(nameof(MovingAverage)),
    };
}
