using Sim.Core;
using Sim.Vision;

namespace Ai;

/// <summary>
/// What it remembers of the enemy (design 5.5, item 4): the fighting units of other players it
/// has seen and has not seen die, each with its chassis and the accents it showed when last
/// seen, and the highest tier it has seen of each of their tracks. These are the signals of
/// design 3.2 that anyone with sight can read. Everything here comes through the sight of its
/// player; a unit that died unseen stays in the memory.
/// </summary>
/// <remarks>
/// With more than one other player, they are remembered together as one enemy.
/// </remarks>
internal sealed class AiIntel
{
    /// <summary>The levels an accent has (design 2.3): I and II.</summary>
    private const int AccentLevels = 2;

    private readonly AiSight _sight;
    private readonly Dictionary<int, Known> _units = [];
    private readonly List<SimEvent> _events = [];
    private readonly int[] _unitsByLine;
    private readonly long[] _accentsByLine;
    private readonly int[] _tiers;
    private long _accents;

    public AiIntel(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
        _unitsByLine = new int[sight.Data.Rules.Lines.Count];
        _accentsByLine = new long[_unitsByLine.Length];
        _tiers = new int[sight.Data.Tracks.Count];
    }

    /// <summary>The value of the enemy units it remembers, each at what its chassis would have cost this player when it was last seen.</summary>
    public long EnemyValue { get; private set; }

    /// <summary>The enemy fighting units it remembers.</summary>
    public int EnemyUnits => _units.Count;

    /// <summary>
    /// The line it remembers most enemy units of, as an index in the roster, or -1 while it
    /// remembers none. Of lines that are level, the first of the roster.
    /// </summary>
    public int MainLine { get; private set; } = -1;

    /// <summary>The highest epoch among the other players, which is announced to everyone.</summary>
    public int EnemyEpoch => _sight.HighestOtherEpoch;

    /// <summary>The enemy units of a line it remembers.</summary>
    public int UnitsOfLine(int line) => _unitsByLine[line];

    /// <summary>
    /// True when an enemy unit it remembers showed the accent of a track at a level or above it.
    /// With <paramref name="onMainLine"/>, only units of <see cref="MainLine"/> count.
    /// </summary>
    public bool HasAccent(int track, int level, bool onMainLine)
    {
        var accents = onMainLine ? (MainLine >= 0 ? _accentsByLine[MainLine] : 0) : _accents;
        return (accents & Bit(track, level)) != 0;
    }

    /// <summary>
    /// The highest tier of a track it has seen the enemy to have, or 0 when it has seen nothing
    /// that shows the track: the effective tiers of a unit it inspected, the tier a structure
    /// shows, and for the primary track of a line the epoch of a chassis it fields.
    /// </summary>
    public int EnemyTier(int track) => _tiers[track];

    /// <summary>
    /// Reads what happened in the tick that has just run: an enemy unit it saw die is forgotten.
    /// Call it after every tick, because the events of a tick are gone after the next.
    /// </summary>
    public void Watch()
    {
        _sight.CollectEvents(_events);
        foreach (var simEvent in _events)
        {
            if (simEvent.Kind == SimEventKind.Death && _units.Remove(simEvent.Entity.Value, out var known))
            {
                EnemyValue -= known.Value;
            }
        }
    }

    /// <summary>Remembers what the player sees now. Call it once a round, after the sight was read.</summary>
    public void Update()
    {
        var data = _sight.Data;
        var roles = data.TrackRoles;
        foreach (var unit in _sight.Enemies)
        {
            if (unit.IsWorker || unit.Chassis == SeenUnit.NoChassis)
            {
                continue;
            }

            var chassis = data.Chassis[unit.Chassis];
            long accents = 0;
            if (_sight.Inspect(unit.Entity, out var inspection))
            {
                foreach (var accent in inspection.Accents)
                {
                    for (var level = 1; level <= accent.Level; level++)
                    {
                        accents |= Bit(accent.Track, level);
                    }
                }

                Raise(roles.Firepower, inspection.Firepower);
                Raise(roles.Protection, inspection.Protection);
                Raise(roles.Mobility, inspection.Mobility);
            }

            // A chassis is unlocked by the primary track of its line at the tier of its epoch.
            if (data.LineOf(chassis.Line) is { } line)
            {
                Raise(line.PrimaryTrack, chassis.Epoch);
            }

            var value = _sight.ValueOf(unit.Chassis);
            _units.TryGetValue(unit.Entity.Value, out var before);
            _units[unit.Entity.Value] = new Known(chassis.Line, value, accents);
            EnemyValue += value - before.Value;
        }

        foreach (var structure in _sight.EnemyStructures)
        {
            if (structure.LookTier > 0 && data.StructureOf(structure.Type)?.Track is { } shown)
            {
                Raise(shown, structure.LookTier);
            }

            if (_sight.Inspect(structure.Entity, out var inspection))
            {
                Raise(roles.Protection, inspection.Protection);
                Raise(roles.Firepower, inspection.Firepower);
            }
        }

        // Counts and sets only, so the order in which the memory hands out its units does not matter.
        Array.Clear(_unitsByLine);
        Array.Clear(_accentsByLine);
        _accents = 0;
        foreach (var known in _units.Values)
        {
            _unitsByLine[known.Line]++;
            _accentsByLine[known.Line] |= known.Accents;
            _accents |= known.Accents;
        }

        MainLine = -1;
        for (var line = 0; line < _unitsByLine.Length; line++)
        {
            if (_unitsByLine[line] > 0 && (MainLine < 0 || _unitsByLine[line] > _unitsByLine[MainLine]))
            {
                MainLine = line;
            }
        }
    }

    private static long Bit(int track, int level) => 1L << ((track * AccentLevels) + level - 1);

    private void Raise(int track, int tier)
    {
        if (tier > _tiers[track])
        {
            _tiers[track] = tier;
        }
    }

    private readonly record struct Known(int Line, int Value, long Accents);
}
