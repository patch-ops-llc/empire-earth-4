using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;

namespace Sim.Outcome;

/// <summary>
/// Conquest (design 5.1): a player who has no Capitol and no Outpost is eliminated, and the last
/// player standing has won. Players who lose their last one in the same tick go out together.
/// </summary>
/// <remarks>
/// <para>
/// A Capitol or an Outpost is a structure that claims ground: one whose file gives it a
/// territory. One that is still building itself counts, as the design counts "unfinished
/// Capitols and Outposts" among a player's for the overlap rule (design 2.5): it stands on the
/// map with all its hit points, and has to be destroyed like any other. A build order whose
/// worker is still on its way is no structure, and does not count.
/// </para>
/// <para>
/// Only a player who was given a start is judged. A player who starts with nothing, as the
/// players of the battle harness and of most tests do, has no Capitol to lose and is never
/// eliminated; it can still concede.
/// </para>
/// <para>
/// It runs last in the tick, so that it judges the map as the tick leaves it: after the
/// structures destroyed in the tick are gone and after the foundations of the tick are laid.
/// It changes nothing but the standings, which nothing else in a tick reads after the commands.
/// </para>
/// </remarks>
public sealed class ConquestSystem : ISystem
{
    private readonly GameData _data;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly Standings _standings;
    private readonly bool[] _judged;
    private readonly bool[] _holds;

    public ConquestSystem(
        GameData data,
        IReadOnlyList<PlayerSetup> players,
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        Standings standings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(standings);
        _data = data;
        _bodies = bodies;
        _structures = structures;
        _standings = standings;
        _judged = [.. players.Select(player => player.HasStart)];
        _holds = new bool[players.Count];
    }

    /// <summary>True for a structure that keeps its owner in the match: a Capitol or an Outpost.</summary>
    public static bool KeepsInTheMatch(StructureDef structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return structure.TerritoryRadius is not null;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_standings.IsDecided)
        {
            return;
        }

        Array.Clear(_holds);
        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (world.Entities.IsAlive(entity)
                && _data.StructureOf(_structures.ValueAt(i).Type) is { } def
                && KeepsInTheMatch(def)
                && _bodies.TryGet(entity, out var body)
                && body.Owner >= 0
                && body.Owner < _holds.Length)
            {
                _holds[body.Owner] = true;
            }
        }

        for (var player = 0; player < _holds.Length; player++)
        {
            if (_judged[player] && !_holds[player] && _standings.IsPlaying(player))
            {
                _standings.TakeOut(player, PlayerStanding.Eliminated, world.Tick);
            }
        }
    }
}
