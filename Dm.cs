using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Directional Movement (DM) — 1-on-1 port of NT @DM.
/// Combines ADX + DI+ + DI- in one indicator (three plots).
/// Bar 0 → ADX=50. Then Wilder-smoothed TR/DM+/DM- sums, derived
/// DI+ = 100·sumDM+/sumTR, DI- = 100·sumDM-/sumTR, and
/// ADX = ((P-1)·prev + 100·|DI+-DI-|/(DI++DI-)) / P.
/// Defaults Period=14. Plots: ADX (DarkSeaGreen 2px), +DI (DodgerBlue), −DI (Crimson).
/// Lines 25 / 75 DarkCyan. Sub-panel.
/// </summary>
[IndicatorDescription("Directional Movement: ADX with +DI and −DI on one panel. Wilder-smoothed TR and DM±; trend strength via ADX, direction via DI± crossovers.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class Dm : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _adx     = new();
    private readonly ChunkedSeries<double> _plusDi  = new();
    private readonly ChunkedSeries<double> _minusDi = new();
    private IndicatorOutputSeries? _adxW, _plW, _miW;
    private double _sumTr, _sumDmPlus, _sumDmMinus, _adxValue;
    private double? _prevHigh, _prevLow, _prevClose;
    private int _barIdx;

    public Dm(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("ADX",  _adx,     PlotStyle.Line, new ChartColor(0x8F, 0xBC, 0x8F), 2.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.None, "25 strong / 75 very strong trend") });  // DarkSeaGreen
        AddPlot(new Plot("+DI",  _plusDi,  PlotStyle.Line, NTColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, null) });
        AddPlot(new Plot("-DI",  _minusDi, PlotStyle.Line, NTColors.Crimson,    1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Down, null) });
        // ADX-family (unbounded): keep the 25/75 references off the auto-scale
        // so a typical 10-30 ADX reading isn't compressed into a sliver.
        AddLine("Strong Trend",      25, NTColors.DarkCyan, participatesInAutoScale: false);
        AddLine("Very Strong Trend", 75, NTColors.DarkCyan, participatesInAutoScale: false);
    }

    [IndicatorParameter(DisplayName = "Period", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    public ISeries<double> ADXPlot => _adxW ??= new IndicatorOutputSeries(_adx,     Bars);
    public ISeries<double> DiPlus  => _plW  ??= new IndicatorOutputSeries(_plusDi,  Bars);
    public ISeries<double> DiMinus => _miW  ??= new IndicatorOutputSeries(_minusDi, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        _sumTr = _sumDmPlus = _sumDmMinus = 0;
        _adxValue = 50;
        _prevHigh = _prevLow = _prevClose = null;
        _barIdx = 0;
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
        if (_prevHigh is null)
        {
            _sumTr = bar.High - bar.Low;
            _sumDmPlus = _sumDmMinus = 0;
            _adxValue = 50;
            _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
            _adx.Append(_adxValue); _plusDi.Append(0); _minusDi.Append(0);
            _barIdx = 1;
            return;
        }
        double pH = _prevHigh!.Value, pL = _prevLow!.Value, pC = _prevClose!.Value;
        double tr = Math.Max(Math.Abs(bar.Low - pC),
                    Math.Max(bar.High - bar.Low, Math.Abs(bar.High - pC)));
        double dmP = (bar.High - pH > pL - bar.Low) ? Math.Max(bar.High - pH, 0) : 0;
        double dmM = (pL - bar.Low > bar.High - pH) ? Math.Max(pL - bar.Low, 0) : 0;
        if (_barIdx < Period)
        {
            _sumTr += tr; _sumDmPlus += dmP; _sumDmMinus += dmM;
        }
        else
        {
            _sumTr      = _sumTr      - _sumTr      / Period + tr;
            _sumDmPlus  = _sumDmPlus  - _sumDmPlus  / Period + dmP;
            _sumDmMinus = _sumDmMinus - _sumDmMinus / Period + dmM;
        }
        double diP = _sumTr == 0 ? 0 : 100 * _sumDmPlus  / _sumTr;
        double diM = _sumTr == 0 ? 0 : 100 * _sumDmMinus / _sumTr;
        double diff = Math.Abs(diP - diM);
        double sum = diP + diM;
        _adxValue = sum == 0 ? 50 : ((Period - 1) * _adxValue + 100 * diff / sum) / Period;
        _adx.Append(_adxValue); _plusDi.Append(diP); _minusDi.Append(diM);
        _prevHigh = bar.High; _prevLow = bar.Low; _prevClose = bar.Close;
        _barIdx++;
    }
}
