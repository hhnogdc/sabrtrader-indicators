using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// TD Sequential (Tom DeMark) — the setup/countdown exhaustion
/// sequencer. Output is rendered as two signed count histograms:
///   <list type="bullet">
///     <item><b>Setup</b>: +1…+9 for a SELL setup (nine closes greater
///       than the close four bars earlier — a potential top), −1…−9 for
///       a BUY setup (nine closes lower — a potential bottom).</item>
///     <item><b>Countdown</b>: +1…+13 / −1…−13, started once the matching
///       setup completes at 9, counting bars whose close clears the
///       high/low two bars ago.</item>
///   </list>
///
/// Logic:
///   <code>
///   Buy setup  : Close &lt; Close[4]  (consecutive; completes at 9)
///   Sell setup : Close &gt; Close[4]  (consecutive; completes at 9)
///   Buy countdown  (after a 9 buy setup) : count Close ≤ Low[2]  → 13
///   Sell countdown (after a 9 sell setup): count Close ≥ High[2] → 13
///   </code>
/// A completed setup in one direction cancels and re-arms the opposite
/// countdown. The consecutive setup count implicitly encodes DeMark's
/// price flip (a count of 1 can only follow a bar that broke the streak).
///
/// Plots Setup (DodgerBlue) / Countdown (Crimson) histograms, reference
/// lines at ±9 and ±13, range −13…13, sub-panel.
/// </summary>
[IndicatorDescription("TD Sequential (DeMark): setup and countdown exhaustion sequencer. Setup counts nine closes vs the close four bars back (+ = sell setup/top, - = buy setup/bottom); countdown then runs to 13 against the high/low two bars back. The 9 and 13 completions flag potential reversals.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class TdSequential : IndicatorBase, IIndicatorPanelHint
{
    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    private readonly ChunkedSeries<double> _setup = new();
    private readonly ChunkedSeries<double> _countdown = new();
    private IndicatorOutputSeries? _setupWrap, _cdWrap;

    // Rolling history of prior bars (current bar pushed AFTER evaluation).
    private double[] _closes = new double[4];   // need Close[4] = Ago(3)
    private double[] _lows = new double[2];      // need Low[2]  = Ago(1)
    private double[] _highs = new double[2];     // need High[2] = Ago(1)
    private int _cHead, _cCount, _hlHead, _hlCount;

    private int _buySetup, _sellSetup;
    private int _buyCd, _sellCd;
    private bool _buyCdActive, _sellCdActive;

    public TdSequential(IIndicator? parent = null, IBarSeries? bars = null) : base(parent, bars: bars)
    {
        AddPlot(new Plot("Setup",     _setup,     PlotStyle.Histogram, PlotColors.DodgerBlue, 2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, -13, 13, PlotValueUnit.Raw, PlotBullishDirection.None, "+9 sell setup / -9 buy setup") });
        AddPlot(new Plot("Countdown", _countdown, PlotStyle.Histogram, PlotColors.Crimson,    2.0) { Semantics = new PlotSemantics(PlotKind.Histogram, -13, 13, PlotValueUnit.Raw, PlotBullishDirection.None, "+/-13 countdown completion") });
        AddLine("Sell countdown",  13, PlotColors.DarkGray, participatesInAutoScale: false);
        AddLine("Sell setup",       9, PlotColors.DarkCyan, participatesInAutoScale: false);
        AddLine("Zero line",        0, PlotColors.DarkGray);
        AddLine("Buy setup",       -9, PlotColors.DarkCyan, participatesInAutoScale: false);
        AddLine("Buy countdown",  -13, PlotColors.DarkGray, participatesInAutoScale: false);
    }

    public ISeries<double> Setup     => _setupWrap ??= new IndicatorOutputSeries(_setup, Bars);
    public ISeries<double> Countdown => _cdWrap ??= new IndicatorOutputSeries(_countdown, Bars);

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        _closes = new double[4];
        _lows = new double[2];
        _highs = new double[2];
        _cHead = _cCount = _hlHead = _hlCount = 0;
        _buySetup = _sellSetup = _buyCd = _sellCd = 0;
        _buyCdActive = _sellCdActive = false;
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
        double c = bar.Close;

        bool haveC4 = _cCount >= 4;
        double c4 = haveC4 ? CloseAgo(3) : 0;

        // ── Setup (consecutive count, restart at 1 after a completed 9) ──
        _buySetup  = (haveC4 && c < c4) ? (_buySetup  == 9 ? 1 : _buySetup  + 1) : 0;
        _sellSetup = (haveC4 && c > c4) ? (_sellSetup == 9 ? 1 : _sellSetup + 1) : 0;

        // A completed setup arms the matching countdown and cancels the opposite.
        if (_buySetup == 9)  { _buyCdActive = true;  _buyCd = 0; _sellCdActive = false; _sellCd = 0; }
        if (_sellSetup == 9) { _sellCdActive = true; _sellCd = 0; _buyCdActive = false; _buyCd = 0; }

        // ── Countdown (needs Low[2] / High[2] = Ago(1)) ──
        bool haveAgo1 = _hlCount >= 2;
        if (_buyCdActive && haveAgo1 && c <= LowAgo(1))  { _buyCd++;  if (_buyCd  >= 13) _buyCdActive  = false; }
        if (_sellCdActive && haveAgo1 && c >= HighAgo(1)) { _sellCd++; if (_sellCd >= 13) _sellCdActive = false; }

        double setupSigned = _sellSetup > 0 ? _sellSetup : (_buySetup > 0 ? -_buySetup : 0);
        double cdSigned     = _sellCd    > 0 ? _sellCd    : (_buyCd    > 0 ? -_buyCd    : 0);
        _setup.Append(setupSigned);
        _countdown.Append(cdSigned);

        // Push current bar into the history rings for subsequent bars.
        _closes[_cHead] = c;
        _cHead = (_cHead + 1) % _closes.Length;
        if (_cCount < _closes.Length) _cCount++;
        _lows[_hlHead] = bar.Low;
        _highs[_hlHead] = bar.High;
        _hlHead = (_hlHead + 1) % _lows.Length;
        if (_hlCount < _lows.Length) _hlCount++;
    }

    private double CloseAgo(int k)
    {
        int cap = _closes.Length;
        return _closes[((_cHead - 1 - k) % cap + cap) % cap];
    }

    private double LowAgo(int k)
    {
        int cap = _lows.Length;
        return _lows[((_hlHead - 1 - k) % cap + cap) % cap];
    }

    private double HighAgo(int k)
    {
        int cap = _highs.Length;
        return _highs[((_hlHead - 1 - k) % cap + cap) % cap];
    }
}
