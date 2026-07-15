using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Coppock Curve (E.S.C. Coppock, 1962) — long-term momentum indicator originally for monthly
/// S&amp;P 500 charts. Crosses above zero historically marked major bull-market starts.
///   Coppock = WMA( ROC(Close, LongRoc) + ROC(Close, ShortRoc), WmaLength )
///   ROC[i]  = 100·(C[i] − C[i−n]) / C[i−n]
/// Defaults LongRoc=14, ShortRoc=11, WmaLength=10 (classical setup).
/// Plot: DodgerBlue line, zero reference line, sub-panel. NaN until LongRoc + WmaLength − 1 bars.
/// </summary>
[IndicatorDescription("Coppock Curve (E.S.C. Coppock, 1962). Long-term momentum oscillator: weighted MA of the sum of two rate-of-change values (default 14 and 11). Designed for monthly charts where a zero-line cross historically marks major bull-market starts.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class CoppockCurve : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _output = new();
    private IndicatorOutputSeries? _wrap;

    private double[] _closeRing = null!;     // for ROC lookbacks
    private int _closeHead, _closeCount;
    private double[] _sumRing = null!;       // for WMA over (roc_long + roc_short)
    private int _sumHead, _sumCount;
    private int _barIdx;

    public CoppockCurve(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        AddPlot(new Plot("Coppock", _output, PlotStyle.Line, NTColors.DodgerBlue, 1.5) { Semantics = new PlotSemantics(PlotKind.Oscillator, null, null, PlotValueUnit.Ratio, PlotBullishDirection.Up, "0 centerline") });
        AddLine("Zero line", 0, NTColors.DarkGray);
    }

    [IndicatorParameter(DisplayName = "LongRoc",   MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int LongRoc   { get; set; } = 14;

    [IndicatorParameter(DisplayName = "ShortRoc",  MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int ShortRoc  { get; set; } = 11;

    [IndicatorParameter(DisplayName = "WmaLength", MinValue = 1, MaxValue = int.MaxValue, Step = 1)]
    public int WmaLength { get; set; } = 10;

    public ISeries<double> Output => _wrap ??= new IndicatorOutputSeries(_output, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        if (LongRoc < 1 || ShortRoc < 1 || WmaLength < 1) throw new ArgumentOutOfRangeException();
        _closeRing = new double[Math.Max(LongRoc, ShortRoc) + 1];
        _sumRing   = new double[WmaLength];
        _closeHead = _closeCount = 0;
        _sumHead = _sumCount = 0;
        _barIdx = 0;
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        var src = Input;
        for (int i = 0; i < src.Count; i++) Consume(src[src.Count - 1 - i]);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        if (Input.Count == 0) return;
        Consume(Input[0]);
    }

    private void Consume(double close)
    {
        _closeRing[_closeHead] = close;
        _closeHead = (_closeHead + 1) % _closeRing.Length;
        if (_closeCount < _closeRing.Length) _closeCount++;

        // Need at least LongRoc+1 closes to compute the long ROC.
        if (_barIdx < LongRoc) { _output.Append(double.NaN); _barIdx++; return; }

        double closeLong  = ReadCloseBarsAgo(LongRoc);
        double closeShort = ReadCloseBarsAgo(ShortRoc);
        double rocLong  = closeLong  == 0 ? 0 : 100 * (close - closeLong)  / closeLong;
        double rocShort = closeShort == 0 ? 0 : 100 * (close - closeShort) / closeShort;
        double sum = rocLong + rocShort;

        _sumRing[_sumHead] = sum;
        _sumHead = (_sumHead + 1) % WmaLength;
        if (_sumCount < WmaLength) _sumCount++;

        if (_sumCount < WmaLength) { _output.Append(double.NaN); _barIdx++; return; }

        // Linear-weighted MA: weights 1..N with N being the newest.
        // Oldest sits at _sumHead (the slot we'd next overwrite).
        double num = 0, denom = 0;
        for (int k = 0; k < WmaLength; k++)
        {
            int idx = (_sumHead + k) % WmaLength;
            double w = k + 1;       // oldest=1, newest=WmaLength
            num += w * _sumRing[idx];
            denom += w;
        }
        _output.Append(num / denom);
        _barIdx++;
    }

    private double ReadCloseBarsAgo(int barsAgo)
    {
        int idx = (_closeHead - 1 - barsAgo + _closeRing.Length) % _closeRing.Length;
        return _closeRing[idx];
    }
}
