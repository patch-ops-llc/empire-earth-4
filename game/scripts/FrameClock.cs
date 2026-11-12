using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Game;

/// <summary>
/// The clock of the <see cref="FrameTest"/>: what each part of the game's own code took in each
/// frame. Only the frame test turns it on. Off, as in play, each timed place costs two tests of
/// a flag.
/// </summary>
/// <remarks>
/// <para>
/// A place is timed with <see cref="Begin"/> before it and <see cref="End(string, FrameSpan)"/>
/// after it. A part's name is its kind, a colon and what it is: <c>after tick: HealthBars</c>.
/// The kinds in use are <c>after tick</c>, <c>every frame</c>, <c>selection</c> for what hears
/// of a change of the selection, <c>input</c>, <c>draw</c> and <c>session</c>. Every part with
/// a kind counts as the game's code outside the tick, which is what the frame budget is about.
/// <see cref="TickPart"/> and <see cref="WholeFrame"/> have no kind and are not counted so.
/// </para>
/// <para>
/// Timed places may lie inside each other. A part is given only its own time: what was timed
/// inside it is taken off. So no time is counted twice and the parts can be added up.
/// </para>
/// <para>
/// The world view times every <see cref="WorldLayer"/> and the frame of the interface every
/// <see cref="InterfacePart"/>, so a piece that draws and reads only through those is timed with
/// no line of its own. Code that Godot calls directly is not seen from there: a node's own
/// <c>_Process</c>, <c>_Draw</c> or input handler. Such a node times itself, as the debug
/// overlay does, or its time shows only in the report's last line, the rest of the frame.
/// </para>
/// </remarks>
public static class FrameClock
{
    /// <summary>The part the simulation's tick is booked under.</summary>
    public const string TickPart = "the tick";

    /// <summary>The part the time from one frame's start to the next is booked under.</summary>
    public const string WholeFrame = "the whole frame";

    private static readonly Dictionary<string, int> Ids = [];
    private static readonly Dictionary<(string Kind, Type Who), int> IdsByType = [];
    private static readonly List<string> Names = [];
    private static readonly List<long[]> Frames = [];
    private static long[] _current = new long[64];
    private static long _frameStarted;
    private static long _timed;

    /// <summary>Whether anything is timed.</summary>
    public static bool On { get; private set; }

    /// <summary>The number of frames taken so far.</summary>
    public static int FrameCount => Frames.Count;

    /// <summary>Turns the clock on and forgets what it held. The first frame begins now.</summary>
    public static void Start()
    {
        On = true;
        Frames.Clear();
        Array.Clear(_current);
        _timed = 0;
        _frameStarted = Stopwatch.GetTimestamp();
    }

    public static void Stop() => On = false;

    /// <summary>Call before a place to be timed, and hand what it returns to <c>End</c>.</summary>
    public static FrameSpan Begin() => On ? new FrameSpan(Stopwatch.GetTimestamp(), _timed) : default;

    /// <summary>Books the time since <paramref name="span"/> began, less what was timed inside it, under a part.</summary>
    public static void End(string part, FrameSpan span)
    {
        if (On && span.Started != 0)
        {
            Book(IdOf(part), span);
        }
    }

    /// <summary>
    /// The same for a part named by its kind and the type of what was called, as
    /// <c>after tick: HealthBars</c>. The name is made once for each type.
    /// </summary>
    public static void End(string kind, object who, FrameSpan span)
    {
        if (On && span.Started != 0)
        {
            End(kind, who.GetType(), span);
        }
    }

    /// <summary>The same, with the type itself.</summary>
    public static void End(string kind, Type who, FrameSpan span)
    {
        if (!On || span.Started == 0)
        {
            return;
        }

        if (!IdsByType.TryGetValue((kind, who), out var id))
        {
            id = IdOf($"{kind}: {who.Name}");
            IdsByType.Add((kind, who), id);
        }

        Book(id, span);
    }

    /// <summary>
    /// Ends the frame that has run and begins the next. Call it first thing in a frame, outside
    /// every timed place.
    /// </summary>
    public static void NextFrame()
    {
        if (!On)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        _current[IdOf(WholeFrame)] += now - _frameStarted;
        _frameStarted = now;
        Frames.Add(_current);
        _current = new long[_current.Length];
    }

    /// <summary>What the frames so far took in the parts chosen, each frame's parts added up.</summary>
    public static FrameTimes Of(Func<string, bool> chosen) => Of(name => chosen(name) ? 1 : 0);

    /// <summary>
    /// The same, with each part's time counted as many times as <paramref name="weight"/> says,
    /// which may be -1 to take a part off another.
    /// </summary>
    public static FrameTimes Of(Func<string, int> weight)
    {
        var frames = Frames.Count;
        if (frames == 0)
        {
            return default;
        }

        var weights = new int[Names.Count];
        for (var id = 0; id < weights.Length; id++)
        {
            weights[id] = weight(Names[id]);
        }

        var values = new long[frames];
        long sum = 0;
        for (var i = 0; i < frames; i++)
        {
            var frame = Frames[i];
            long value = 0;
            for (var id = 0; id < weights.Length && id < frame.Length; id++)
            {
                value += weights[id] * frame[id];
            }

            values[i] = value;
            sum += value;
        }

        Array.Sort(values);
        var toMilliseconds = 1000.0 / Stopwatch.Frequency;
        var p99 = values[Math.Max(0, (int)Math.Ceiling(frames * 0.99) - 1)];
        return new FrameTimes(sum * toMilliseconds / frames, p99 * toMilliseconds, values[^1] * toMilliseconds);
    }

    /// <summary>Whether a part counts as the game's code outside the tick: it has a kind.</summary>
    public static bool IsOwnCode(string part) => part.Contains(": ", StringComparison.Ordinal);

    /// <summary>
    /// The report: all of the game's code outside the tick, then each kind, then each part, then
    /// the tick, the whole frame and what is left of the frame. For each the average, the 99th
    /// percentile and the worst of a frame, in milliseconds.
    /// </summary>
    public static string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"  {string.Empty,-58} {"average",8} {"p99",8} {"worst",8}   ms a frame, over {Frames.Count} frames");

        void Line(string name, FrameTimes times) =>
            text.AppendLine(CultureInfo.InvariantCulture, $"  {name,-58} {times.Average,8:0.000} {times.P99,8:0.000} {times.Worst,8:0.000}");

        Line("ALL OF THE GAME'S CODE OUTSIDE THE TICK", Of(IsOwnCode));
        text.AppendLine();

        var parts = Names.Where(IsOwnCode).Order(StringComparer.Ordinal).ToList();
        foreach (var kind in parts.Select(KindOf).Distinct())
        {
            Line($"{kind}, all of it", Of(name => IsOwnCode(name) && KindOf(name) == kind));
        }

        text.AppendLine();
        foreach (var part in parts)
        {
            Line(part, Of(name => name == part));
        }

        text.AppendLine();
        Line(TickPart, Of(name => name == TickPart));
        Line(WholeFrame, Of(name => name == WholeFrame));
        Line("the rest of the frame: the engine, and code not timed", Of(name => name == WholeFrame ? 1 : -1));
        return text.ToString();
    }

    private static string KindOf(string part) => part[..part.IndexOf(": ", StringComparison.Ordinal)];

    private static void Book(int id, FrameSpan span)
    {
        var elapsed = Stopwatch.GetTimestamp() - span.Started;

        // What was timed inside this place is already booked, under its own parts.
        var own = elapsed - (_timed - span.TimedBefore);
        _current[id] += own;
        _timed += own;
    }

    private static int IdOf(string part)
    {
        if (!Ids.TryGetValue(part, out var id))
        {
            id = Names.Count;
            Ids.Add(part, id);
            Names.Add(part);
            if (id >= _current.Length)
            {
                Array.Resize(ref _current, _current.Length * 2);
            }
        }

        return id;
    }
}

/// <summary>A timed place that has begun: when, and how much the clock had booked by then.</summary>
public readonly record struct FrameSpan(long Started, long TimedBefore);

/// <summary>What frames took, in milliseconds a frame: the average, the 99th percentile and the worst.</summary>
public readonly record struct FrameTimes(double Average, double P99, double Worst);
