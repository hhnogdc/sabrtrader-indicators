Indicators
AI requests
Ask the AI a question from an indicator, and get the answer back as your own C# type.

On this page
How it works
The answer type
Build the request
Build the context
Ask
Handle the answer
Example indicator
Strategies
Where it works
Who can call it
How it works
An indicator reaches the AI through its Ai property, an IAiClient. The types live in SabrTrader.Pipeline.AiRequests.

You write a record for the answer you want, for example a list of trendlines.
You build one AiRequest<TAnswer> with plain instructions and a handler.
On a bar, you fill an AiContextBuilder with what the AI should look at: your bars, plot values, your own data, and on a chart the chart picture.
You call Ai.Ask(request, context). It returns a status at once and never waits.
Later, your handler gets an AiAnswer<TAnswer>. When it was answered, Result is your own record, filled in. There is no text to parse.
The AI that answers is the provider and model the user chose in the AI settings. A script cannot pick its own model.

The answer type
The answer type is a record, class or struct with public properties. The AI fills it in. A property can be:

a number (int, long, double, decimal and the other numeric types), a bool, a string or an enum,
a DateTime or DateTimeOffset,
another record of the same kind,
a list or an array of any of these (IReadOnlyList<T>, List<T>, T[] and the like).
object, dictionaries, interfaces, abstract types and a type that contains itself are refused. The check runs when you build the AiRequest: it throws an ArgumentException that names the member, so you see the problem the first time the script starts, not when the AI answers. A list or a single value on its own is refused too; wrap it in a record.

Put [property: Description("...")] (from System.ComponentModel) on a record parameter to tell the AI what a field means. Use it for anything a name alone does not make clear, such as units or which index a number refers to.

Answer types
using System.Collections.Generic;
using System.ComponentModel;

public sealed record Anchor(
    [property: Description("Bar index, the same index as the bars you were sent")] int Bar,
    [property: Description("Price of the line at that bar")] double Price);

public sealed record Trendline(Anchor Start, Anchor End, bool IsSupport);

public sealed record TrendlineSet(IReadOnlyList<Trendline> Lines);
Build the request
new AiRequest<TAnswer>(string instructions, Action<AiAnswer<TAnswer>> onAnswer) holds the instructions and the handler. Build it once, in OnInit, and ask with it as often as you like. Empty instructions throw an ArgumentException. Instructions and OnAnswer read back what you passed.

C#
_trendlines = new AiRequest<TrendlineSet>(
    "Find every trendline that is still in play. Anchor each line on two swing points.",
    OnTrendlines);
AiRequest<TAnswer> also implements IAiAnswerFormat<TAnswer>. That interface is for the platform: its AnswerType, JsonOptions and TryRead tell the AI the answer shape and read the answer back. You do not use it.

Build the context
Keep one AiContextBuilder per script. Call Reset(), add what the AI needs, and pass it to Ask. Every method returns the builder, so the calls chain. Ask copies the context, so you can reset it again straight after. Use the builder from your own callbacks only; it is not thread-safe.

Method	Sends
Bars(int count)	The newest count bars of the script's own series, up to the current bar: open, high, low, close, volume and time.
Plot(ISeries<double> values, int count, string name)	The newest count values of a plot, your own or another indicator's. The item carries firstBar, the bar index of its first value. Values that are not numbers (gaps) go as null.
Data<T>(string name, T value)	Any data of your own, a number, a list or a record, written as JSON under name.
ChartImage()	A picture of the chart as it is on screen when the ask goes out. Only on a chart, and only with a model that reads pictures.
Reset()	Starts a new context. It keeps its buffers, so a builder reused on every ask allocates nothing new.
Bars and plot values carry the bar index your script itself uses. The last bar sent is the current bar. So the AI can answer in bar indexes, like the Anchor.Bar above, and you can use them directly.

A wrong argument does not throw. A count below 1, an empty name, or a value that cannot be written as JSON sets HasFault, and the next Ask returns InvalidContext. The builder also tells you what it holds: BarsRequested, ChartImageRequested, IsEmpty, and ItemsJson (the plots and data so far, which you do not need to read).

C#
_context.Reset()
    .Bars(300)
    .Plot(_swings, 300, "swings")
    .Data("instrument", "ES")
    .ChartImage();
AiAskStatus status = Ai.Ask(_trendlines, _context);
Ask
AiAskStatus Ask<TAnswer>(AiRequest<TAnswer> request, AiContextBuilder context) returns at once. It never blocks and never touches the network. Only Accepted is followed by a handler call.

AiAskStatus	Meaning	What to do
Accepted	The ask is on its way. The handler runs once for it, unless the script stops or recalculates first.	Wait for the handler.
NotEntitled	This user has no paid AI licence. Nothing was sent.	Carry on without the AI. Stop asking.
NotConfigured	No AI provider or model is chosen in the AI settings. Nothing was sent.	Tell the user once, and stop asking.
NotAvailableHere	This place does not ask the AI (see Where it works), or a chart is loading or recalculating history. Nothing was sent.	Ask again on a later bar.
InvalidContext	The context is empty, has HasFault set, or asks for a chart picture where there is no chart or the model cannot read pictures. Nothing was sent.	Fix the context, or ask without ChartImage().
Each request has one ask in flight at a time. If you ask again while one is running, the new ask waits behind it. If you ask once more before it goes out, the newest ask replaces the waiting one, and the replaced one is answered Superseded. So a slow answer never builds up a queue; the AI always gets your latest context.

There is no limit on how often you ask. Every ask is billed on the user's own AI key, so ask when something has changed, not on every tick. Each ask writes a line to the Activity log under the category AI.

An ask has 90 seconds, counted from the moment you asked. After that it ends as Timeout.

Where the host does not ask the AI, Ai is AiClient.None, whose Ask always returns NotAvailableHere. It also works as a stand-in in unit tests.

Handle the answer
The handler gets an AiAnswer<TAnswer> with Status and Result. Result is set only when Status is Answered, and is null otherwise. The answer is never pooled, so you can keep it.

The handler runs on the chart's session worker, never during OnBarUpdate or a recalculation. If the handler throws, the exception is caught and logged; it does not reach the chart.

AiAnswerStatus	Meaning	What to do
Answered	The AI answered in your type. Result holds it.	Use it.
Superseded	A newer ask of the same request replaced this one before it was sent.	Nothing. The newer ask gets its own answer.
Timeout	No answer within 90 seconds of the ask.	Keep what you had; ask again later.
NotEntitled	The paid AI licence ended while the ask was on its way.	Stop asking.
NoCredits	The AI account has no credits left.	Stop asking until the user tops up.
RateLimited	The AI provider refused the ask because too many were sent. It answers again later.	Ask less often.
NoAnswer	The AI replied without giving an answer in your type.	Ask again later; clearer instructions help.
InvalidAnswer	The AI gave an answer that does not fit your type, also after it was told what was wrong.	Ask again later; check the descriptions on your fields.
ChartImageUnavailable	The chart picture could not be taken, for example because the chart window is minimised.	Ask again later, or ask without ChartImage().
ServiceError	The AI service failed or cannot serve this ask yet. The Activity log says why.	Ask again later.
Example indicator
This indicator marks swing highs and lows in a plot. Every 20 bars it sends the last 300 bars, the swing plot and the chart picture, and asks which trendlines are still in play. The AI answers each line as two anchors, each a bar index and a price.

AiTrendlines.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using SabrTrader.Pipeline.AiRequests;
using SabrTrader.Pipeline.Bars;
using SabrTrader.Pipeline.Indicators;
using SabrTrader.Pipeline.Plots;
using SabrTrader.Pipeline.Series;

namespace MyCompany.Indicators;

public sealed record Anchor(
    [property: Description("Bar index, the same index as the bars you were sent")] int Bar,
    [property: Description("Price of the line at that bar")] double Price);

public sealed record Trendline(
    [property: Description("The older anchor")] Anchor Start,
    [property: Description("The newer anchor")] Anchor End,
    [property: Description("True when price sits above the line")] bool IsSupport);

public sealed record TrendlineSet(IReadOnlyList<Trendline> Lines);

[IndicatorDescription("Asks the AI which trendlines are still in play.")]
[IndicatorCategory(IndicatorCategory.Structure)]
[IndicatorInput(IndicatorInputKind.Bars)]
public sealed class AiTrendlines : IndicatorBase
{
    private const int BarsToSend = 300;

    // Swing highs and lows, one value per bar (NaN on other bars), so plot values and bars share an index.
    private readonly ChunkedSeries<double> _swings = new();
    private readonly AiContextBuilder _context = new();
    private AiRequest<TrendlineSet> _trendlines = null!;
    private bool _sendChartImage = true;
    private bool _stopAsking;

    [IndicatorParameter(DisplayName = "Ask every", Description = "Ask the AI once every this many bars.",
        Group = "Parameters", Order = 0, MinValue = 1, MaxValue = 1000, Step = 1)]
    public int AskEveryBars { get; set; } = 20;

    /// <summary>The newest answer. Draw it in your own rendering, or let a strategy read it.</summary>
    public IReadOnlyList<Trendline> Trendlines { get; private set; } = Array.Empty<Trendline>();

    public AiTrendlines(IIndicator? parent = null, ISeries<double>? input = null)
        : base(parent, input)
    {
        AddPlot(new Plot("Swings", _swings, PlotStyle.Dots, new ChartColor(255, 193, 7), 3));
    }

    public override void OnInit(IIndicatorContext ctx)
    {
        base.OnInit(ctx);
        // Built once. Throws ArgumentException here if TrendlineSet held a member the AI cannot fill.
        _trendlines = new AiRequest<TrendlineSet>(
            "Find every trendline that is still in play. Anchor each line on two swing points " +
            "from the swings plot and give the anchors as bar index and price.",
            OnTrendlines);
    }

    public override void OnDataLoaded(IIndicatorContext ctx)
    {
        IBarSeries bars = ctx.Bars(0);
        for (int barsAgo = bars.Count - 1; barsAgo >= 0; barsAgo--)
            AppendSwing(bars, barsAgo);
    }

    public override void OnBarUpdate(IIndicatorContext ctx)
    {
        IBarSeries bars = ctx.Bars(0);
        AppendSwing(bars, 0);
        if (_stopAsking || bars.Count % AskEveryBars != 0) return;

        _context.Reset().Bars(BarsToSend).Plot(_swings, BarsToSend, "swings");
        if (_sendChartImage) _context.ChartImage();

        switch (Ai.Ask(_trendlines, _context))
        {
            case AiAskStatus.Accepted:
            case AiAskStatus.NotAvailableHere:   // history is loading or recalculating; the next ask tries again
                break;
            case AiAskStatus.InvalidContext:     // this context is never empty, so the model cannot read pictures
                _sendChartImage = false;
                break;
            case AiAskStatus.NotEntitled:
            case AiAskStatus.NotConfigured:
                _stopAsking = true;
                ctx.Print("AI trendlines: no AI licence or no AI model chosen in the AI settings.");
                break;
        }
    }

    private void OnTrendlines(AiAnswer<TrendlineSet> answer)
    {
        switch (answer.Status)
        {
            case AiAnswerStatus.Answered:
                Trendlines = answer.Result!.Lines;
                Context.Print($"AI trendlines: {Trendlines.Count} line(s) in play.");
                break;
            case AiAnswerStatus.Superseded:      // a newer ask is on its way and gets its own answer
                break;
            case AiAnswerStatus.ChartImageUnavailable:
                Context.Print("AI trendlines: the chart picture could not be taken; keeping the last lines.");
                break;
            case AiAnswerStatus.NotEntitled:
            case AiAnswerStatus.NoCredits:
                _stopAsking = true;
                Context.Print($"AI trendlines stopped: {answer.Status}.");
                break;
            default:                             // Timeout, RateLimited, NoAnswer, InvalidAnswer, ServiceError
                Context.Print($"AI trendlines: no new lines ({answer.Status}); keeping the last ones.");
                break;
        }
    }

    // Marks the bar two back as a swing once two newer bars confirm it.
    private void AppendSwing(IBarSeries bars, int barsAgo)
    {
        _swings.Append(double.NaN);
        if (bars.Count - barsAgo < 5) return;
        Bar pivot = bars[barsAgo + 2];
        Bar a = bars[barsAgo + 4], b = bars[barsAgo + 3], c = bars[barsAgo + 1], d = bars[barsAgo];
        if (pivot.High > a.High && pivot.High > b.High && pivot.High >= c.High && pivot.High >= d.High)
            _swings.Set(2, pivot.High);
        else if (pivot.Low < a.Low && pivot.Low < b.Low && pivot.Low <= c.Low && pivot.Low <= d.Low)
            _swings.Set(2, pivot.Low);
    }
}
The swing plot holds one value per bar, so its firstBar and the bar indexes line up. The answer arrives after OnBarUpdate has returned, so Trendlines is the newest answer at that point. To draw the lines, see Custom rendering.

Strategies
A strategy has the same Ai property and asks the same way. It builds the request in OnInitialize, asks from OnBar, and places orders in the handler. See Strategies: AI requests.

Where it works
It works on chart indicators and on live strategy runs, on every venue.

Ask returns NotAvailableHere in backtests, the optimizer, the Strategy Analyzer, the ASP simulator and the Market Analyzer, and on a chart while it loads or recalculates history. Nothing is sent there, so a backtest never spends AI credit.

Who can call it
Asking needs the paid AI licence. Without it, Ask returns NotEntitled. The user also has to choose a provider and a model in the AI settings; until then Ask returns NotConfigured.

An indicator that uses Ai still runs for users without the licence. It just gets NotEntitled. If you share or sell it, handle that case so it stays useful without the AI.

