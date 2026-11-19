Indicators
Decision models
Call a connected decision model from an indicator. The model returns a choice, a score, or a probability. Your indicator shows it on the chart.

On this page
Connect a model
Build a request
Build the state
Ask and decide
Read the answer
Example indicator
On a chart
Strategies
Write a decision provider
Limits
Who can call it
Connect a model
You connect a model in the Connections dialog, under Decisions. The built-in connection is System One. It speaks POST /v1/systemone, and its presets fill in the endpoint and model for you:

Preset	Endpoint	Model	Key
Jev	https://api.typesafe.ai	jev-latest	Your TypeSafe key.
OpenRouter	https://openrouter.ai/api	jev-latest	Your OpenRouter key and credit.
Laya	http://127.0.0.1:8080	laya	None. Runs on this PC and takes up to 20 choice options.
Kev	http://127.0.0.1:8008	kev-latest	None. Runs on this PC.
Custom	You enter it.	You enter it.	Optional.
Leave the endpoint and model empty to use the preset's. The client adds /v1/systemone. An API key is only sent over https, or to a server on this PC.

Connecting sends one yes/no question. If the server does not answer, the row stays failed. When it answers, the connection is pinned to the model version the server reports, so an alias such as jev-latest cannot switch models in the middle of a run. An answer from another version arrives as Faulted; reconnect to use the new version.

When more than one decision connection is up, the one that connected first answers, and the next one takes over when it disconnects or its key is rejected. A running strategy follows along without a restart. Turn off Use for decisions to keep a connection up without it answering. Parallel requests (1 to 8) and Call timeout (5 to 60 seconds) set how many asks may be in flight and how long one may take, counted from the moment you ask.

The state leaves this PC. For Jev the state and the questions go to TypeSafe, for OpenRouter to OpenRouter, and for Custom to the server you entered. For a Laya or Kev preset they go to the server on this machine. The SabrTrader cloud never sees them. It only grants the feature.
Build a request
A DecisionRequest is a name plus 1 to 16 questions (Name, Questions). It is immutable: build it once, keep it in a field, and pass the same instance on every bar. The engine sizes its buffers per request instance, and one strategy or indicator may ask with at most 32 different instances.

Every question has a Name (the key you read the answer back with) and Instructions (the text the model reads). There are three kinds:

Question	Constructor	The model returns
ChoiceQuestion	(string name, string instructions, IReadOnlyList<DecisionOption> options), 2 to 255 options with unique keys.	One option, a confidence, and optionally a probability per option.
ScoreQuestion	(string name, string instructions, IReadOnlyList<string> levels), 2 to 10 levels, lowest first.	A position on the scale in level units (0 is the first level), a confidence, and optionally a probability per level.
YesNoQuestion	(string name, string instructions). The instructions are a statement.	The probability, 0 to 1, that the statement is true.
A DecisionOption is (string key, string criterion): Key is what you compare against, Criterion tells the model when to pick it. Request names, question names, option keys and state field names are 1 to 64 characters of [A-Za-z0-9_]. Instructions, criteria and level texts are 1 to 1024 UTF-8 bytes. The constructors throw ArgumentException on a broken rule, so a bad request fails when you build it, never on a live bar.

C#
private static readonly DecisionRequest EntryRequest = new("entry", new DecisionQuestion[]
{
    new ChoiceQuestion("side", "Which way does this closed bar's order flow point?", new[]
    {
        new DecisionOption("long", "Buyers lifted the offer and delta closed near its high"),
        new DecisionOption("short", "Sellers hit the bid and delta closed near its low"),
        new DecisionOption("flat", "The prints disagree, or the bar is noise"),
    }),
    new ScoreQuestion("conviction", "How clean is the imbalance on this bar?",
        new[] { "none", "weak", "clear", "strong" }),
    new YesNoQuestion("pocAtExtreme", "The point of control sits in the top or bottom third of the bar"),
});
Build the state
The state is what the model looks at: a list of named fields that DecisionStateBuilder writes into a compact byte form. Keep one builder per strategy or indicator, call Reset() before each bar's fields, and pass it to Ask. Ask copies the bytes, so you can reset and refill the builder right after.

Member	What it does
Add(string name, string value)
Add(string name, bool value)
Add(string name, long value)
Add(string name, double value)	Writes one field. A string may be up to 1024 UTF-8 bytes. A double must be finite (NaN and infinity fault the builder), and -0 is written as 0.
BeginState(string name) / EndState()	Open and close a named group of fields. Groups nest up to 4 deep. An EndState with no open group faults the builder.
HasFault	True once any write broke a rule. The builder does not throw; it stops writing, and Ask returns InvalidState.
Reset()	Clears the fields and the fault.
Length, Written	The byte count and the bytes written so far.
Field names are unique within their group. Two sibling groups may use the same names, so a "prev" and a "curr" group can both hold "close". A whole state holds at most 256 entries (each BeginState and EndState counts as one) and 16 KB. Order matters: the same fields in the same order produce the same bytes.

Ask and decide
Decisions is an IDecisionClient with one method, DecisionAskStatus Ask(DecisionRequest request, DecisionStateBuilder state). It returns immediately and does not wait on the network. The answer arrives later in OnDecision(DecisionAnswer answer), on the chart's session worker, never during OnBarUpdate. Newer bars may have closed by then, so use answer.BarCount to find the bar the answer belongs to.

Ask returns	Meaning
Accepted	OnDecision runs once for this ask, unless the strategy stops first (or the indicator recalculates).
NotEntitled	This seat cannot call a decision model. Nothing was sent.
NoConnection	No decision connection is serving: none is connected, Use for decisions is off, or the server rejected the key.
InvalidState	The builder has HasFault set, a choice question has more options than the connected model accepts (Laya takes 20), or this strategy or indicator already asked with 32 different request instances.
NotAvailableHere	This host does not deliver decisions. Chart indicators also get this while history is loading.
A host that does not deliver decisions hands you DecisionClient.None, whose Ask always returns NotAvailableHere. It is also a handy stand-in in unit tests.

Each request instance holds at most one waiting ask. If you ask again with the same request before the waiting one has gone out, the waiting one completes as Superseded and the new state takes its place. An ask that is already with the model runs to the end. So a slow model never builds up a backlog: it answers the latest state you gave it.

answer.Status	Meaning
Answered	The model answered and the answer passed every check. The TryGet* lookups work.
Superseded	You asked again with the same request before this ask was sent. The newer ask gets its own answer.
Unavailable	No answer: a timeout, a busy or unreachable server, a disconnect, a rejected key, or a backtest past its call cap.
Faulted	The model answered with something that did not pass the checks, or answered from another model version than the connection is pinned to.
The activity log says which case it was.

Read the answer
A DecisionAnswer carries RequestName, BarCount (your bar count when you asked: BarCount in a strategy, CurrentBar + 1 in an indicator), ProviderId and ModelVersion (who answered), and Status. Read each question by name with TryGetChoice, TryGetScore or TryGetYesNo. They return false unless Status is Answered. The answer is never pooled, so you can keep it.

Type	Members
ChoiceAnswer	OptionKey, OptionIndex (its position in the options you passed), Probability (the chosen option's own probability when the model sent a distribution, otherwise the confidence), Confidence, and TryGetProbability(optionKey, out probability) for any other option the model priced.
ScoreAnswer	Score in level units (0 is the first level), LevelIndex (the level the score falls in, Score rounded down), Confidence, and TryGetLevelProbability(levelIndex, out probability), which returns false when the model sent no per-level distribution.
YesNoAnswer	Probability that the statement is true.
OptionProbability	readonly struct (string OptionKey, double Probability): one entry of a choice distribution.
You choose the cutoff: the platform acts only on what your OnDecision does with the numbers.

Every answer is checked against your request before OnDecision sees it. It must answer every question exactly once with the right kind. A choice must be one of your option keys and may price only your options. Every probability and confidence must be 0 to 1. A score must lie between 0 and the last level, and a level distribution, when sent, must have one entry per level. Anything else produces Faulted, never Answered.

Example indicator
This indicator describes each closed bar and the 20-bar range before it to the model, and asks how likely the next bar trades above this bar's high. It writes the probability into a plot in its own panel, on the bar it asked about. Bars that are not answered stay empty.

BreakoutOdds.cs
using System;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Decisions;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace MyCompany.Indicators;

[IndicatorDescription("Asks the decision model how likely the next bar trades above this bar's high.")]
[IndicatorCategory(IndicatorCategory.Momentum)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class BreakoutOdds : IndicatorBase, IIndicatorPanelHint, IRepaintNotifier
{
    private const int RangeBars = 20;

    private static readonly DecisionRequest OddsRequest = new("breakout", new DecisionQuestion[]
    {
        new YesNoQuestion("breaksHigh", "The next bar trades above this bar's high"),
    });

    // One value per bar, NaN until the model has answered for that bar.
    private readonly ChunkedSeries<double> _odds = new();
    private readonly DecisionStateBuilder _state = new();
    private DecisionAskStatus _lastStatus = DecisionAskStatus.Accepted;

    public event Action? RepaintRequested;

    public PanelPlacement DefaultPanel => PanelPlacement.NewSubpanel;

    public BreakoutOdds(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot("Odds", _odds, PlotStyle.Line, new ChartColor(33, 150, 243), 2));
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        // History is never asked about: the chart returns NotAvailableHere while it loads.
        for (int i = 0; i < ctx.Bars(0).Count; i++)
            _odds.Append(double.NaN);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        _odds.Append(double.NaN);
        IBarSeries bars = ctx.Bars(0);
        if (bars.Count <= RangeBars) return;

        Bar bar = bars[0];
        double rangeHigh = double.MinValue, rangeLow = double.MaxValue;
        for (int i = 1; i <= RangeBars; i++)
        {
            rangeHigh = Math.Max(rangeHigh, bars[i].High);
            rangeLow = Math.Min(rangeLow, bars[i].Low);
        }

        _state.Reset();
        _state.BeginState("bar");
        _state.Add("open", bar.Open);
        _state.Add("high", bar.High);
        _state.Add("low", bar.Low);
        _state.Add("close", bar.Close);
        _state.EndState();
        _state.BeginState("range");
        _state.Add("bars", (long)RangeBars);
        _state.Add("high", rangeHigh);
        _state.Add("low", rangeLow);
        _state.EndState();

        DecisionAskStatus status = Decisions.Ask(OddsRequest, _state);
        if (status != _lastStatus && status != DecisionAskStatus.Accepted)
            ctx.Print("Breakout odds not asked: " + status);
        _lastStatus = status;
    }

    // Runs on the chart's session worker, never during OnBarUpdate.
    protected override void OnDecision(DecisionAnswer answer)
    {
        if (answer.Status != DecisionStatus.Answered) return;
        if (!answer.TryGetYesNo("breaksHigh", out YesNoAnswer breaksHigh)) return;

        // answer.BarCount is the bar count when you asked; newer bars may have closed since.
        int barsAgo = Context.CurrentBar + 1 - answer.BarCount;
        if (barsAgo < 0 || barsAgo >= _odds.Count) return;
        _odds.Set(barsAgo, breaksHigh.Probability);
        RepaintRequested?.Invoke();
    }

    public void RequestRepaint() => RepaintRequested?.Invoke();
}
The request is a static field, so every bar asks with the same instance. IRepaintNotifier lets the chart repaint as soon as an answer lands, without waiting for the next tick. See Repaint requests.

On a chart
While the chart is loading or recalculating history, Ask returns NotAvailableHere and does not call the model, so only live bars are asked about. When the indicator recalculates, answers to asks made before that are dropped and never reach OnDecision. If OnDecision throws, the exception is caught and reported for this indicator; it does not reach the chart.

An indicator does not place orders. Write the answer into a plot, as above, and a strategy can read that plot like any other indicator value. See Using indicators.

Strategies
A strategy has the same Decisions and OnDecision. It asks from OnBar, places the order in OnDecision, and in a backtest waits for each bar's answers before the next bar. See Strategies: decision models.

Write a decision provider
A decision model plugs in as a venue plugin under Decisions in the Connections dialog. How to write one is on Strategies: decision models.

Limits
DecisionLimits holds every hard limit as a constant, so you can check against them in code.

Constant	Value	Applies to
MaxQuestions	16	Questions in one request.
MinChoiceOptions / MaxChoiceOptions	2 / 255	Options in one choice question. The connected model may accept fewer (DecisionProviderProfile.MaxChoiceOptions).
MinScoreLevels / MaxScoreLevels	2 / 10	Levels in one score question.
MinNameLength / MaxNameLength	1 / 64	Request, question, option and field names, [A-Za-z0-9_].
MaxStringUtf8Bytes	1024	Instructions, criteria, level texts, and string field values.
MaxStateBytes	16384	One state.
MaxEntries	256	Fields in one state, counting each BeginState and EndState.
MaxDepth	4	Nesting of BeginState groups.
MaxRequestsPerClient	32	Different request instances one strategy or indicator may ask with.
Who can call it
Pro, Ultimate, Lifetime, and an account trial can call it. A Free seat gets NotEntitled. The model is not called.

