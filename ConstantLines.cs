using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Constant Lines.
/// Adds up to four user-set horizontal lines on the price panel via
/// the <see cref="IIndicator.Lines"/> mechanism. The original defaults: lines
/// at 0 (Goldenrod). Each enabled toggle controls whether the line
/// is rendered; the values are user-configurable.
/// </summary>
[IndicatorDescription("Constant Lines. Draws up to four horizontal reference lines at user-defined price levels, useful for marking targets, key levels, or fixed stop zones on the chart.")]
[IndicatorCategory(IndicatorCategory.Custom)]
[IndicatorInput(IndicatorInputKind.None)]
public sealed class ConstantLines : IndicatorBase
{
    public ConstantLines(IIndicator? parent = null) : base(parent)
    {
        // Value + Visible are re-stamped from the LineNValue / LineNEnabled
        // parameters on every OnInit, so the settings dialog must not offer a
        // second level editor for them (isParameterDriven: true).
        AddLine("Line 1", 0, PlotColors.Goldenrod, 1.0, PlotStyle.Line,
                participatesInAutoScale: true, isParameterDriven: true);
        AddLine("Line 2", 0, PlotColors.Goldenrod, 1.0, PlotStyle.Line,
                participatesInAutoScale: true, isParameterDriven: true);
        AddLine("Line 3", 0, PlotColors.Goldenrod, 1.0, PlotStyle.Line,
                participatesInAutoScale: true, isParameterDriven: true);
        AddLine("Line 4", 0, PlotColors.Goldenrod, 1.0, PlotStyle.Line,
                participatesInAutoScale: true, isParameterDriven: true);
    }

    [IndicatorParameter(DisplayName = "Line1Enabled")] public bool Line1Enabled { get; set; } = true;
    [IndicatorParameter(DisplayName = "Line2Enabled")] public bool Line2Enabled { get; set; }
    [IndicatorParameter(DisplayName = "Line3Enabled")] public bool Line3Enabled { get; set; }
    [IndicatorParameter(DisplayName = "Line4Enabled")] public bool Line4Enabled { get; set; }

    [IndicatorParameter(DisplayName = "Line1Value")] public double Line1Value { get; set; }
    [IndicatorParameter(DisplayName = "Line2Value")] public double Line2Value { get; set; }
    [IndicatorParameter(DisplayName = "Line3Value")] public double Line3Value { get; set; }
    [IndicatorParameter(DisplayName = "Line4Value")] public double Line4Value { get; set; }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        Lines[0].Value = Line1Value; Lines[0].Visible = Line1Enabled;
        Lines[1].Value = Line2Value; Lines[1].Visible = Line2Enabled;
        Lines[2].Value = Line3Value; Lines[2].Visible = Line3Enabled;
        Lines[3].Value = Line4Value; Lines[3].Visible = Line4Enabled;
    }
}
