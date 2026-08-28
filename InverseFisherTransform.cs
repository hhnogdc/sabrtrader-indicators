using System;
using SabrTrader.Pipeline.Indicators.Builtin.Incremental;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Inverse Fisher Transform (Ehlers, 2004) — sharpens the turning points of an oscillator
/// by mapping it through <c>IFT(x) = (e^(2x) − 1) / (e^(2x) + 1)</c>, output bounded in [−1, 1].
///
/// Typically applied to RSI:
///   y = 0.1·(RSI(Period) − 50)
///   y = WMA(y, WmaLength)
///   IFT(y) → output
/// Output crosses ±0.5 = buy / sell signal in Ehlers' framework.
///
/// Defaults RsiPeriod=5, WmaLength=9. Goldenrod line, ±0.5 SlateBlue reference lines, sub-panel.
/// </summary>
[IndicatorDescription("Ehlers Inverse Fisher Transform of RSI: maps a WMA-smoothed RSI through (e^2x-1)/(e^2x+1) to bound output in [-1,1] and sharpen turning points; cross +/-0.5 = signal.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class InverseFisherTransform : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    private Rsi _rsi = null!;
    private double[] _wmaRing = null!;
    private int _wmaHead, _wmaCount;

    public InverseFisherTransform(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("IFT", _output, PlotStyle.Line, PlotColors.Goldenrod, 1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, -1, 1, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0") });
        AddLine("Bull threshold",  0.5, PlotColors.SlateBlue);
        AddLine("Zero line",       0,   PlotColors.DarkGray);
        AddLine("Bear threshold", -0.5, PlotColors.SlateBlue);
    }

    [IndicatorParameter(DisplayName = "RsiPeriod", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int RsiPeriod { get; set; } = 5;

    [IndicatorParameter(DisplayName = "WmaLength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int WmaLength { get; set; } = 9;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (RsiPeriod < 1 || WmaLength < 1) throw new ArgumentOutOfRangeException();
        // Child Rsi reads Input directly → safe under post-order auto-dispatch.
        _rsi = new Rsi(this, input: Input) { Period = RsiPeriod, Smooth = 1 };
        _wmaRing = new double[WmaLength];
        _wmaHead = _wmaCount = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // Cascade has already backfilled _rsi. Walk forward.
        int n = _rsi.Default.Count;
        for (int i = 0; i < n; i++) Step(n - 1 - i);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        AdvanceChildren(ctx);
        if (_rsi.Default.Count == 0) return;
        Step(0);
    }

    private void Step(int barsAgo)
    {
        double rsi = _rsi.Default[barsAgo];
        if (double.IsNaN(rsi)) { _output.Append(double.NaN); return; }
        double y = 0.1 * (rsi - 50);
        _wmaRing[_wmaHead] = y;
        _wmaHead = (_wmaHead + 1) % WmaLength;
        if (_wmaCount < WmaLength) _wmaCount++;

        if (_wmaCount < WmaLength) { _output.Append(double.NaN); return; }

        // Linear-weighted MA: oldest=weight 1, newest=weight WmaLength.
        double num = 0, denom = 0;
        for (int k = 0; k < WmaLength; k++)
        {
            int idx = (_wmaHead + k) % WmaLength;
            double w = k + 1;
            num += w * _wmaRing[idx];
            denom += w;
        }
        double yy = num / denom;
        double e2 = Math.Exp(2 * yy);
        double ift = (e2 - 1) / (e2 + 1);
        _output.Append(ift);
    }
}
