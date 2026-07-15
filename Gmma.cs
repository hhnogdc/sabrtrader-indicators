using System;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Guppy Multiple Moving Average (GMMA) — Daryl Guppy.
/// Two sets of EMAs over the input series:
///   Short ribbon (trader sentiment): EMA 3, 5, 8, 10, 12, 15  → cool color group (DodgerBlue family)
///   Long  ribbon (investor sentiment): EMA 30, 35, 40, 45, 50, 60 → warm color group (Crimson family)
/// When the short ribbon trades above the long ribbon (and spreads), short-term traders are
/// trending up over long-term holders. Compression of long-ribbon often precedes major moves.
///
/// 12 plots (S3..S15, L30..L60). Defaults match the original Guppy specification.
/// All 12 EMAs are seeded with SMA at their own period.
/// </summary>
[IndicatorDescription("Guppy Multiple Moving Average: two ribbons of 6 EMAs each — short (3/5/8/10/12/15) for traders, long (30/35/40/45/50/60) for investors. Crossovers and ribbon compression mark regime shifts.")]
[IndicatorCategory(IndicatorCategory.Trend)]
[IndicatorInput(IndicatorInputKind.Scalar)]
public sealed class Gmma : IndicatorBase
{
    private static readonly int[] ShortPeriods = { 3, 5, 8, 10, 12, 15 };
    private static readonly int[] LongPeriods  = { 30, 35, 40, 45, 50, 60 };

    private readonly ChunkedSeries<double>[] _shortSeries;
    private readonly ChunkedSeries<double>[] _longSeries;
    private readonly IndicatorOutputSeries[] _shortWraps;
    private readonly IndicatorOutputSeries[] _longWraps;

    private readonly double[] _shortState;
    private readonly double[] _longState;
    private readonly bool[] _shortSeeded;
    private readonly bool[] _longSeeded;
    private readonly double[] _shortSeedSum;
    private readonly double[] _longSeedSum;
    private readonly int[] _shortSeedCount;
    private readonly int[] _longSeedCount;
    private readonly double[] _shortK;
    private readonly double[] _longK;

    public Gmma(IIndicator? parent = null, ISeries<double>? input = null) : base(parent, input)
    {
        _shortSeries = new ChunkedSeries<double>[ShortPeriods.Length];
        _longSeries  = new ChunkedSeries<double>[LongPeriods.Length];
        _shortWraps  = new IndicatorOutputSeries[ShortPeriods.Length];
        _longWraps   = new IndicatorOutputSeries[LongPeriods.Length];
        _shortState  = new double[ShortPeriods.Length];
        _longState   = new double[LongPeriods.Length];
        _shortSeeded = new bool[ShortPeriods.Length];
        _longSeeded  = new bool[LongPeriods.Length];
        _shortSeedSum   = new double[ShortPeriods.Length];
        _longSeedSum    = new double[LongPeriods.Length];
        _shortSeedCount = new int[ShortPeriods.Length];
        _longSeedCount  = new int[LongPeriods.Length];
        _shortK = new double[ShortPeriods.Length];
        _longK  = new double[LongPeriods.Length];

        // Cool-toned palette for short ribbon (trader group).
        var shortColors = new[]
        {
            new ChartColor(0x80, 0xDE, 0xEA),    // light cyan
            new ChartColor(0x4D, 0xD0, 0xE1),
            new ChartColor(0x26, 0xC6, 0xDA),
            new ChartColor(0x00, 0xBC, 0xD4),    // cyan
            new ChartColor(0x00, 0xAC, 0xC1),
            new ChartColor(0x00, 0x97, 0xA7),    // deep cyan
        };
        // Warm-toned palette for long ribbon (investor group).
        var longColors = new[]
        {
            new ChartColor(0xFF, 0xCD, 0xD2),    // light pink
            new ChartColor(0xEF, 0x9A, 0x9A),
            new ChartColor(0xE5, 0x73, 0x73),
            new ChartColor(0xEF, 0x53, 0x50),
            new ChartColor(0xE5, 0x39, 0x35),
            new ChartColor(0xD3, 0x2F, 0x2F),    // deep red
        };

        for (int i = 0; i < ShortPeriods.Length; i++)
        {
            _shortSeries[i] = new ChunkedSeries<double>();
            AddPlot(new Plot($"EMA{ShortPeriods[i]}", _shortSeries[i], PlotStyle.Line, shortColors[i], 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        }
        for (int i = 0; i < LongPeriods.Length; i++)
        {
            _longSeries[i] = new ChunkedSeries<double>();
            AddPlot(new Plot($"EMA{LongPeriods[i]}", _longSeries[i], PlotStyle.Line, longColors[i], 1.0) { Semantics = new PlotSemantics(PlotKind.Overlay, null, null, PlotValueUnit.Price, PlotBullishDirection.None, null) });
        }
    }

    public ISeries<double> Short(int idx)
    {
        if (idx < 0 || idx >= ShortPeriods.Length) throw new ArgumentOutOfRangeException(nameof(idx));
        return _shortWraps[idx] ??= new IndicatorOutputSeries(_shortSeries[idx], Bars);
    }
    public ISeries<double> Long(int idx)
    {
        if (idx < 0 || idx >= LongPeriods.Length) throw new ArgumentOutOfRangeException(nameof(idx));
        return _longWraps[idx] ??= new IndicatorOutputSeries(_longSeries[idx], Bars);
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        for (int i = 0; i < ShortPeriods.Length; i++)
        {
            _shortK[i] = 2d / (ShortPeriods[i] + 1);
            _shortSeeded[i] = false; _shortSeedSum[i] = 0; _shortSeedCount[i] = 0;
        }
        for (int i = 0; i < LongPeriods.Length; i++)
        {
            _longK[i] = 2d / (LongPeriods[i] + 1);
            _longSeeded[i] = false; _longSeedSum[i] = 0; _longSeedCount[i] = 0;
        }
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

    private void Consume(double v)
    {
        for (int i = 0; i < ShortPeriods.Length; i++)
            _shortSeries[i].Append(StepEma(v, ShortPeriods[i], _shortK[i], ref _shortState[i], ref _shortSeeded[i], ref _shortSeedSum[i], ref _shortSeedCount[i]));
        for (int i = 0; i < LongPeriods.Length; i++)
            _longSeries[i].Append(StepEma(v, LongPeriods[i], _longK[i], ref _longState[i], ref _longSeeded[i], ref _longSeedSum[i], ref _longSeedCount[i]));
    }

    private static double StepEma(double v, int period, double k,
                                  ref double state, ref bool seeded,
                                  ref double sum, ref int count)
    {
        if (!seeded)
        {
            sum += v; count++;
            if (count < period) return double.NaN;
            state = sum / period;
            seeded = true;
            return state;
        }
        state = state + k * (v - state);
        return state;
    }
}
