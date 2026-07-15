using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Adaptive Binary Wave — 1-on-1 port of ATAS's <c>AdaptiveBinaryWaveMA</c>
/// (Technical/AdaptiveBinaryWaveMA.cs). A three-state trend signal built
/// on the <see cref="Ama"/>: it tracks the running high/low of the AMA and
/// emits <b>+1</b> once the AMA has risen more than a volatility band above
/// its recent low, <b>−1</b> once it has fallen more than the band below
/// its recent high, and <b>0</b> in between. The band is
/// <c>Percent% · StdDev(AMA)</c>, so the threshold widens in volatile
/// regimes and tightens in quiet ones.
///
/// Reuses <see cref="Ama"/> (child) and <see cref="RollingStdDev"/> rather
/// than re-implementing either. The AMA's warm-up bars (NaN) are treated
/// as 0 here, matching ATAS's unset-series-reads-as-0 semantics so the wave
/// stays neutral until the AMA is established. Sub-panel.
/// Defaults: Period=15, ShortPeriod=3, LongPeriod=20, Percent=30.
/// </summary>
[IndicatorDescription("Adaptive Binary Wave: a +1/0/-1 trend state derived from the Adaptive Moving Average. Emits +1 when the AMA rises more than Percent%*StdDev(AMA) above its recent low, -1 when it falls that far below its recent high, else 0.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class AdaptiveBinaryWave : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    private Ama _ama = null!;            // detached child — adaptive MA
    private RollingStdDev _std = null!;  // StdDev of the AMA
    private double _amaHigh, _amaLow, _prevAma;
    private int _barIdx;

    public AdaptiveBinaryWave(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Wave", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.0)
        {
            Semantics = new PlotSemantics(PlotKind.Oscillator, RangeMin: -1, RangeMax: 1,
                PlotValueUnit.Raw, PlotBullishDirection.Up, Reference: "+1 up / 0 neutral / -1 down")
        });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "AMA + StdDev look-back.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 15;

    [IndicatorParameter(DisplayName = "ShortPeriod", Description = "AMA fast constant (upper adaptation bound).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public double ShortPeriod { get; set; } = 3;

    [IndicatorParameter(DisplayName = "LongPeriod", Description = "AMA slow constant (lower adaptation bound).",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public double LongPeriod { get; set; } = 20;

    [IndicatorParameter(DisplayName = "Percent", Description = "Band width as a percent of StdDev(AMA).",
        MinValue = 1, MaxValue = 100, Step = 1)]
    public double Percent { get; set; } = 30;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Period must be at least 1.");
        if (ShortPeriod < 1) throw new ArgumentOutOfRangeException(nameof(ShortPeriod));
        if (LongPeriod < 1) throw new ArgumentOutOfRangeException(nameof(LongPeriod));
        if (Percent < 1) throw new ArgumentOutOfRangeException(nameof(Percent));
        _ama = new Ama(parent: null, input: Input) { Period = Period, FastConstant = ShortPeriod, SlowConstant = LongPeriod };
        _ama.OnInit(ctx);
        _std = new RollingStdDev(Period);
        _amaHigh = _amaLow = _prevAma = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        _ama.OnDataLoaded(ctx);
        var src = Input;
        int n = src.Count;
        for (int absIdx = 0; absIdx < n; absIdx++)
            Compute(_ama.Output[n - 1 - absIdx]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        _ama.OnBarUpdate(ctx);
        Compute(_ama.Output[0]);
    }

    private void Compute(double amaRaw)
    {
        // ATAS reads an unset AMA slot as 0 during warm-up.
        double a = double.IsNaN(amaRaw) ? 0.0 : amaRaw;
        _std.Push(a);

        if (_barIdx == 0)
        {
            _amaHigh = _amaLow = a;
            _output.Append(double.NaN);     // ATAS leaves bar 0 unset
            _prevAma = a;
            _barIdx++;
            return;
        }

        if (a < _prevAma) _amaLow = a;
        if (a > _prevAma) _amaHigh = a;

        double deviation = Percent * 0.01 * _std.StdDev;
        double wave = (a - _amaLow > deviation) ? 1.0
                    : (_amaHigh - a > deviation) ? -1.0
                    : 0.0;

        _output.Append(wave);
        _prevAma = a;
        _barIdx++;
    }
}
