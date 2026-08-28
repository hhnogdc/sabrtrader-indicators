using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// TRIX.
/// Triple EMA + 1-bar % rate-of-change of that triple EMA, plus a
/// Signal EMA of the result.
/// Defaults Period=14, SignalPeriod=3.
/// Plot 1 = Default (DimGray), Plot 2 = Signal (Crimson). Zero line.
/// </summary>
[IndicatorDescription("TRIX momentum oscillator. The 1-bar percentage rate-of-change of a triple-EMA-smoothed price, plotted with a signal-line EMA for crossover entries.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Trix : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _default = new();
    private readonly ChunkedSeries<double> _signal  = new();
    private IndicatorOutputSeries? _defWrap, _sigWrap, _defAsBar;
    private Ema _ema1 = null!, _ema2 = null!, _ema3 = null!;
    private Ema _sigEma = null!;
    private double _prevTriple;
    private bool _hasPrev;

    public Trix(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Default", _default, PlotStyle.Line, PlotColors.DimGray, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddPlot(new Plot("Signal",  _signal,  PlotStyle.Line, PlotColors.Crimson, 1.0) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, PlotColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "Period", Description = "EMA period applied 3x.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int Period { get; set; } = 14;

    [IndicatorParameter(DisplayName = "SignalPeriod", Description = "Signal EMA period.",
        MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int SignalPeriod { get; set; } = 3;

    public ISeries<double> Default => _defWrap ??= new IndicatorOutputSeries(_default, Bars);
    public ISeries<double> Signal  => _sigWrap ??= new IndicatorOutputSeries(_signal,  Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period));
        if (SignalPeriod < 1) throw new ArgumentOutOfRangeException(nameof(SignalPeriod));
        _ema1 = new Ema(this, input: Input)        { Period = Period };
        _ema2 = new Ema(this, input: _ema1.Output) { Period = Period };
        _ema3 = new Ema(this, input: _ema2.Output) { Period = Period };
        _defAsBar = new IndicatorOutputSeries(_default, Bars);
        // parent=null: parent-filled _defAsBar; avoid auto-dispatch double-processing.
        _sigEma = new Ema(parent: null, input: _defAsBar)  { Period = SignalPeriod };
        _sigEma.OnInit(ctx);
        _hasPrev = false;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        int n = _ema3.Output.Count;
        for (int i = 0; i < n; i++)
        {
            int b = n - 1 - i;
            double triple = _ema3.Output[b];
            if (!_hasPrev)
            {
                // The original bar 0: Value = Input[0]; we just emit Input here.
                // But our Input may not have a known value at this point —
                // use 0 as conservative seed; Signal stays 0 too.
                _default.Append(0);
                _hasPrev = true;
                _prevTriple = triple;
            }
            else
            {
                _default.Append(triple == 0 ? 0 : 100.0 * (triple - _prevTriple) / triple);
                _prevTriple = triple;
            }
        }
        _sigEma.OnDataLoaded(ctx);
        var s = _sigEma.Output;
        for (int i = 0; i < s.Count; i++) _signal.Append(s[s.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);   // advance attached child MAs on the live bar
        if (_ema3.Output.Count == 0) return;
        double triple = _ema3.Output[0];
        if (!_hasPrev) { _default.Append(0); _hasPrev = true; _prevTriple = triple; }
        else
        {
            _default.Append(triple == 0 ? 0 : 100.0 * (triple - _prevTriple) / triple);
            _prevTriple = triple;
        }
        _sigEma.OnBarUpdate(ctx);
        _signal.Append(_sigEma.Output[0]);
    }
}
