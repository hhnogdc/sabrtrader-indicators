using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>Normalisation applied by <see cref="PriceTransform"/>.</summary>
public enum PriceTransformMode
{
    /// <summary>Input divided by its own value <see cref="PriceTransform.Period"/> bars ago — a lagged ratio (momentum as a multiple, 1.0 = unchanged).</summary>
    Ratio = 0,

    /// <summary>Input divided by the bar close — expresses the source relative to current price (1.0 when the source IS the close).</summary>
    DividedByPrice = 1,

    /// <summary>100 × Input / bar close — the source as a percentage of current price.</summary>
    PercentagePrice = 2,
}

/// <summary>
/// Price Transform — a generic series-normalisation study that rescales its
/// <see cref="IndicatorBase.Input"/> in one of three ways selected by
/// <see cref="Mode"/>:
/// <list type="bullet">
///   <item><b>Ratio</b> = <c>Input / Input[Period]</c> — a lagged ratio
///         (1.0 = unchanged over the window; &gt;1 = up, &lt;1 = down).</item>
///   <item><b>DividedByPrice</b> = <c>Input / Close</c> — the source
///         relative to current price.</item>
///   <item><b>PercentagePrice</b> = <c>100 · Input / Close</c> — the source
///         as a percentage of current price.</item>
/// </list>
///
/// <para>Because the transform reads only <see cref="IndicatorBase.Input"/>
/// and the bar close, it is fully consumer/venue agnostic and composes:
/// wire its <c>Input</c> to any other indicator's output to normalise that
/// indicator (e.g. an EMA <em>as a percentage of price</em>). With the
/// default <c>Input</c> (close) the price modes are trivially 1.0 / 100,
/// so it is intended to be fed a non-price source.</para>
///
/// <para><see cref="Period"/> is the lag used by <see cref="PriceTransformMode.Ratio"/>
/// only; the price modes ignore it. Until <see cref="Period"/> bars of
/// history exist the Ratio mode emits 1.0 (no change). Division guards
/// against a zero denominator by emitting 0.</para>
///
/// Defaults Mode=Ratio, Period=14, DodgerBlue, sub-panel.
/// </summary>
[IndicatorDescription("Price Transform: rescales the input series. Ratio = Input / Input[Period] (lagged ratio); DividedByPrice = Input / close; PercentagePrice = 100 * Input / close. Feed its Input from another indicator to normalise that indicator relative to price.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class PriceTransform : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private int _barIdx;

    public PriceTransform(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Transform", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "Ratio/DividedByPrice: 1.0 = parity; PercentagePrice: percent of price") });
    }

    [IndicatorParameter(DisplayName = "Mode", Description = "Normalisation: Ratio (Input / Input[Period]), DividedByPrice (Input / close), or PercentagePrice (100 * Input / close).")]
    public PriceTransformMode Mode { get; set; } = PriceTransformMode.Ratio;

    [IndicatorParameter(DisplayName = "Period", Description = "Lag (in bars) used by the Ratio mode; ignored by the price modes.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Compute(src, src.Count - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Compute(Input, 0);
    }

    private void Compute(ISeries<double> src, int barsAgoOfCurrent)
    {
        double value = src[barsAgoOfCurrent];
        double result;
        switch (Mode)
        {
            case PriceTransformMode.Ratio:
            {
                // Lagged value lives Period bars older than the current one.
                // Until Period bars of history exist the ratio is undefined →
                // emit 1.0 (no change).
                if (_barIdx < Period)
                {
                    result = 1.0;
                    break;
                }
                double prior = src[barsAgoOfCurrent + Period];
                result = prior == 0 ? 0 : value / prior;
                break;
            }
            case PriceTransformMode.DividedByPrice:
            {
                double close = CloseAt(barsAgoOfCurrent);
                result = close == 0 ? 0 : value / close;
                break;
            }
            case PriceTransformMode.PercentagePrice:
            {
                double close = CloseAt(barsAgoOfCurrent);
                result = close == 0 ? 0 : 100.0 * value / close;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
        _output.Append(result);
        _barIdx++;
    }

    /// <summary>
    /// Bar close aligned to the input sample at <paramref name="barsAgo"/>.
    /// <see cref="IndicatorBase.Bars"/> is resolved by the framework to the
    /// source bars of <see cref="IndicatorBase.Input"/> (or the primary
    /// series); a defensive 0 is returned if the index is out of range so a
    /// mis-aligned wiring degrades to 0 rather than throwing.
    /// </summary>
    private double CloseAt(int barsAgo)
    {
        var bars = Bars;
        if (bars == null || barsAgo < 0 || barsAgo >= bars.Count) return 0;
        return bars[barsAgo].Close;
    }
}
