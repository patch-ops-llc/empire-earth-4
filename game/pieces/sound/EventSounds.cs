using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>Answers whether the local player may know of something that happened in the last tick.</summary>
public delegate bool KnowsEvent(in SimEvent happened);

/// <summary>A sound that something in the match calls for.</summary>
/// <param name="Cue">The sound.</param>
/// <param name="Position">Where it happened, in tiles. It counts only for a cue of the world.</param>
public readonly record struct SoundRequest(SoundCue Cue, FixedVector2 Position);

/// <summary>
/// The kinds of event a sound is played for. The four of combat are on every branch that has
/// this piece. The others come with pull requests that were still open when sound was built, so
/// they are found by name: a build that has the kind plays its sound, and a build that does not
/// has nothing to play it for.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>StructureFinished</c>, <c>BuildRefused</c>: construction, 15c (pull request 45).</item>
/// <item><c>StructureRemoved</c>: structures on the map, 15b (pull request 42). A structure that falls to attacks needs structures in combat as well, 16b (pull request 58).</item>
/// <item><c>TrainRefused</c>: production, 15d (pull request 46).</item>
/// <item><c>ResearchFinished</c>, <c>EpochChanged</c>: live refresh, 14c (pull request 37).</item>
/// </list>
/// Once those are on <c>main</c>, each lookup by name can become the kind itself, and
/// <see cref="Missing"/> can go.
/// </remarks>
public sealed class SoundEventKinds
{
    /// <summary>The names looked up, in the order <see cref="Missing"/> lists them.</summary>
    public static readonly IReadOnlyList<string> LaterNames =
        ["StructureFinished", "StructureRemoved", "ResearchFinished", "EpochChanged", "BuildRefused", "TrainRefused"];

    private readonly List<string> _missing = [];

    /// <param name="find">Gives the kind of a name, or null when this build has no such kind.</param>
    public SoundEventKinds(Func<string, SimEventKind?> find)
    {
        ArgumentNullException.ThrowIfNull(find);
        StructureFinished = Find(find, LaterNames[0]);
        StructureRemoved = Find(find, LaterNames[1]);
        ResearchFinished = Find(find, LaterNames[2]);
        EpochChanged = Find(find, LaterNames[3]);
        BuildRefused = Find(find, LaterNames[4]);
        TrainRefused = Find(find, LaterNames[5]);
    }

    public SimEventKind StructureFinished { get; }

    public SimEventKind StructureRemoved { get; }

    public SimEventKind ResearchFinished { get; }

    public SimEventKind EpochChanged { get; }

    public SimEventKind BuildRefused { get; }

    public SimEventKind TrainRefused { get; }

    /// <summary>The names this build has no kind for. Their sounds are made, and nothing in a match calls for them.</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <summary>The kinds of the build that is running.</summary>
    public static SoundEventKinds OfThisBuild() =>
        new(name => Enum.TryParse<SimEventKind>(name, out var kind) && kind != SimEventKind.None ? kind : null);

    /// <summary>True when nothing in this build's matches can call for the cue, because the kind of event it answers is not in it.</summary>
    public bool IsWaiting(SoundCue cue) => cue switch
    {
        SoundCue.StructureFinished => StructureFinished == SimEventKind.None,
        SoundCue.StructureLost => StructureRemoved == SimEventKind.None,
        SoundCue.ResearchFinished => ResearchFinished == SimEventKind.None,
        SoundCue.EpochReached => EpochChanged == SimEventKind.None,
        _ => false,
    };

    // A kind this build does not have is None, which no event in the list ever is.
    private SimEventKind Find(Func<string, SimEventKind?> find, string name)
    {
        if (find(name) is { } kind)
        {
            return kind;
        }

        _missing.Add(name);
        return SimEventKind.None;
    }
}

/// <summary>
/// Turns the events of a tick into the sounds the local player hears for them. It reads the
/// events and decides; it plays nothing and holds nothing of the match.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is heard.</b>
/// </para>
/// <list type="bullet">
/// <item>A melee hit and a shot that lands, each with its own sound, when the player may know of it.</item>
/// <item>
/// A death, when the player may know of it. A structure whose hit points ran out is reported
/// as a death and as a structure removed in the same tick: that is a structure destroyed, with
/// its own sound, and not a unit's death.
/// </item>
/// <item>A structure removed without a death, as a foundation its owner cancelled: nothing.</item>
/// <item>A structure finished and a research finished: the local player's own only. They are notices to their owner.</item>
/// <item>A change of epoch: every player's, since the design announces it to all (design 1.4).</item>
/// <item>An order, a build order or a training order refused: the local player's own only.</item>
/// </list>
/// <para>
/// <b>The fog.</b> Whether the player may know of an event is asked of <see cref="KnowsEvent"/>,
/// which is the fog of war where the build has one (see <see cref="SightOfTheView"/>). The one
/// kind not asked about is the change of epoch, which the design tells every player wherever it
/// happens. A sound for the player's own event needs no asking: a player knows what is theirs.
/// </para>
/// </remarks>
public sealed class EventSounds
{
    private readonly int _player;
    private readonly SoundEventKinds _kinds;
    private readonly HashSet<EntityId> _died = [];
    private readonly HashSet<EntityId> _removed = [];

    /// <param name="player">The local player's number.</param>
    /// <param name="kinds">The kinds of event that come with later pieces.</param>
    public EventSounds(int player, SoundEventKinds kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _player = player;
        _kinds = kinds;
    }

    /// <summary>
    /// Adds to a list the sounds the events of one tick call for, in the order of the events.
    /// Every event that calls for a sound adds one: thinning them out is the limiter's work.
    /// </summary>
    public void Collect(IReadOnlyList<SimEvent> events, KnowsEvent knows, List<SoundRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(knows);
        ArgumentNullException.ThrowIfNull(requests);

        _died.Clear();
        _removed.Clear();
        for (var i = 0; i < events.Count; i++)
        {
            var happened = events[i];
            if (happened.Kind == SimEventKind.Death)
            {
                _died.Add(happened.Entity);
            }
            else if (happened.Kind == _kinds.StructureRemoved)
            {
                _removed.Add(happened.Entity);
            }
        }

        for (var i = 0; i < events.Count; i++)
        {
            var happened = events[i];
            if (CueFor(in happened, knows) is { } cue)
            {
                requests.Add(new SoundRequest(cue, happened.Position));
            }
        }
    }

    private SoundCue? CueFor(in SimEvent happened, KnowsEvent knows)
    {
        var kind = happened.Kind;
        var own = happened.Player == _player;
        switch (kind)
        {
            case SimEventKind.None:
                return null;
            case SimEventKind.MeleeHit:
                return knows(in happened) ? SoundCue.MeleeHit : null;
            case SimEventKind.ShotHit:
                return knows(in happened) ? SoundCue.ShotHit : null;
            case SimEventKind.Death:
                return !_removed.Contains(happened.Entity) && knows(in happened) ? SoundCue.UnitDied : null;
            case SimEventKind.OrderRefused:
                return own ? SoundCue.OrderRefused : null;
            default:
                break;
        }

        // The kinds found by name cannot stand in a switch: their numbers are not known here.
        if (kind == _kinds.StructureRemoved)
        {
            return _died.Contains(happened.Entity) && knows(in happened) ? SoundCue.StructureLost : null;
        }

        if (kind == _kinds.StructureFinished)
        {
            return own ? SoundCue.StructureFinished : null;
        }

        if (kind == _kinds.ResearchFinished)
        {
            return own ? SoundCue.ResearchFinished : null;
        }

        if (kind == _kinds.EpochChanged)
        {
            return SoundCue.EpochReached;
        }

        if (kind == _kinds.BuildRefused || kind == _kinds.TrainRefused)
        {
            return own ? SoundCue.OrderRefused : null;
        }

        return null;
    }
}
