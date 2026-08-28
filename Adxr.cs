using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// ADXR.
/// Value = (ADX[0] + ADX[Interval bars ago]) / 2, where the ADX child uses
/// <see cref="Period"/> and the lookback uses a SEPARATE <see cref="Interval"/>
/// (the original defaults Period=14, Interval=10). During warm-up (bar &lt; Interval) the
/// lookback clamps to the oldest available ADX, matching the original's
/// <c>adx[CurrentBar]</c>. Single plot DarkCyan + 25/75 threshold lines, sub-panel.
/// </summary>
[IndicatorDescription("ADX Rating. Smoothed momentum gauge equal to (ADX[now] + ADX[Interval bars ago]) / 2 that filters out short-term ADX whipsaws and confirms longer-term trend strength.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Adxr : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private Adx _adx = null!;
    private int _barIdx;

    public Adxr(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("ADXR", _output, PlotStyle.Line, PlotColors.DarkCyan, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, "25 strong / 75 very strong trend") });
        // The original @ADXR.cs:34-35 — same threshold lines as ADX; unbounded, so they
        // must not drive the panel scale.
        AddLine("Strong Trend",      25, new ChartColor(0x6A, 0x5A, 0xCD), participatesInAutoScale: false);  // SlateBlue
        AddLine("Very Strong Trend", 75, PlotColors.Goldenrod,               participatesInAutoScale: false);
    }

    [IndicatorParameter(DisplayName = "Interval", MinValue = 1, MaxValue = int.MaxValue, Step = 1,
        Description = "Lookback distance (bars) for the second ADX term. The original default 10.")]
    public int Interval { get; set; } = 10;

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1,
        Description = "ADX smoothing period. The original default 14.")]
    public int Period { get; set; } = 14;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Interval < 1) throw new ArgumentOutOfRangeException(nameof(Interval));
        _adx = new Adx(this, bars: Bars) { Period = Period };
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _adx.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            // The original: bar < Interval → adx[CurrentBar] (oldest); else adx[Interval].
            int lookback = _barIdx < Interval ? _barIdx : Interval;
            _output.Append((_adx.Output[b] + _adx.Output[b + lookback]) / 2.0);
            _barIdx++;
        }
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance child ADX on the live bar (else flat line)
        if (_adx.Output.Count == 0) return;
        int lookback = _barIdx < Interval ? _barIdx : Interval;
        _output.Append((_adx.Output[0] + _adx.Output[lookback]) / 2.0);
        _barIdx++;
    }
}
