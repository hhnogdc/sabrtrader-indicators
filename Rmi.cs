using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Relative Momentum Index (Roger Altman) — RSI generalised so the
/// up/down comparison spans an N-bar momentum window instead of a
/// single bar. With <c>Momentum=1</c> it reduces exactly to Wilder's
/// RSI; larger Momentum smooths the oscillator and shifts its
/// overbought/oversold crossings.
///
/// Math:
///   <code>
///   up    = max(Close − Close[Momentum], 0)
///   down  = max(Close[Momentum] − Close, 0)
///   avgUp / avgDown : SMA-seeded then Wilder-smoothed over Period
///   RMI   = 100 − 100 / (1 + avgUp/avgDown)   (100 when avgDown = 0)
///   </code>
/// NaN warmup until the first Wilder seed (Period deltas after the
/// Momentum lookback is available), matching the RSI port's behaviour.
///
/// Defaults Period=14, Momentum=5. Single plot (DodgerBlue), 30 / 70
/// reference lines, range 0–100, sub-panel.
/// </summary>
[IndicatorDescription("Relative Momentum Index (Altman): RSI generalised over an N-bar momentum lookback. Wilder-smoothed up/down momentum gives a 0-100 oscillator; Momentum=1 equals RSI. 30/70 mark oversold/overbought.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Rmi : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;
    private double _avgUp, _avgDown, _seedUp, _seedDown;
    private bool _seeded;
    private int _barIdx, _deltaCount;

    public Rmi(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("RMI", _output, PlotStyle.Line, PlotColors.DodgerBlue, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, 0, 100, PlotValueUnit.Percent, PlotBullishDirection.Up, "30 oversold / 70 overbought") });
        AddLine("Oversold",   30, PlotColors.DarkCyan);
        AddLine("Overbought", 70, PlotColors.DarkCyan);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "Wilder smoothing period for the momentum gains/losses.", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "Momentum", Description = "Bars back for the momentum comparison (1 = RSI).", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Momentum { get; set; } = 5;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (Momentum < 1) throw new ArgumentOutOfRangeException(nameof(Momentum));
        _avgUp = _avgDown = _seedUp = _seedDown = 0;
        _seeded = false;
        _barIdx = 0;
        _deltaCount = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int absIdx = 0; absIdx < src.Count; absIdx++) Consume(src, src.Count - 1 - absIdx);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input, 0);
    }

    private void Consume(ISeries<double> src, int barsAgoOfCurrent)
    {
        if (_barIdx < Momentum)
        {
            // No Momentum-bar-ago value yet → no momentum delta.
            _output.Append(double.NaN);
            _barIdx++;
            return;
        }

        double delta = src[barsAgoOfCurrent] - src[barsAgoOfCurrent + Momentum];
        double up = delta > 0 ? delta : 0;
        double down = delta < 0 ? -delta : 0;
        _deltaCount++;
        _barIdx++;

        if (!_seeded)
        {
            _seedUp += up;
            _seedDown += down;
            if (_deltaCount < Period) { _output.Append(double.NaN); return; }
            _avgUp = _seedUp / Period;
            _avgDown = _seedDown / Period;
            _seeded = true;
        }
        else
        {
            _avgUp = (_avgUp * (Period - 1) + up) / Period;
            _avgDown = (_avgDown * (Period - 1) + down) / Period;
        }
        _output.Append(_avgDown == 0 ? 100 : 100 - 100 / (1 + _avgUp / _avgDown));
    }
}
