using Sim.Core;
using Sim.Setup;

namespace Tools.Battles;

/// <summary>
/// Counts what each shell catches, from the events of a match: a shell is a shot fired by a unit
/// whose blueprint has a blast radius, and it has caught every unit that a hit names it for. A
/// shell that lands on nobody, or is lost, has caught none. A shell still in the air when the
/// fight ends is not counted.
/// </summary>
/// <remarks>
/// It reads only who fired a shot and which shot a hit came from, so it counts a shell that hits
/// its target alone and a shell with a blast the same way.
/// </remarks>
public sealed class ShellCounter
{
    private readonly int? _player;
    private readonly Dictionary<EntityId, int> _flying = [];
    private readonly List<EntityId> _landed = [];
    private int _count;
    private int _caught;
    private int _most;

    /// <param name="player">The player whose shells are counted, or null for those of every player.</param>
    public ShellCounter(int? player = null) => _player = player;

    public ShellCount Count => new(_count, _caught, _most);

    /// <summary>Takes in the tick the match has just run.</summary>
    public void AfterTick(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        foreach (var simEvent in match.Simulation.Events)
        {
            if (simEvent.Kind == SimEventKind.ShotFired
                && (_player is not { } player || simEvent.Player == player)
                && match.Combatants.TryGet(simEvent.Other, out var firer)
                && match.Research.Tech(simEvent.Player).Blueprints[firer.Chassis].BlastRadius is not null)
            {
                Fired(simEvent.Entity);
            }
            else if (simEvent.Kind == SimEventKind.ShotHit)
            {
                Hit(simEvent.Other);
            }
        }

        EndOfTick(match.Shots.Contains);
    }

    /// <summary>A shell was fired.</summary>
    public void Fired(EntityId shell) => _flying.Add(shell, 0);

    /// <summary>A shot hit a unit. It counts when the shot is a shell.</summary>
    public void Hit(EntityId shot)
    {
        if (_flying.TryGetValue(shot, out var caught))
        {
            _flying[shot] = caught + 1;
        }
    }

    /// <summary>Counts every shell that is no longer in the air.</summary>
    public void EndOfTick(Func<EntityId, bool> inTheAir)
    {
        ArgumentNullException.ThrowIfNull(inTheAir);
        _landed.Clear();
        foreach (var shell in _flying.Keys)
        {
            if (!inTheAir(shell))
            {
                _landed.Add(shell);
            }
        }

        foreach (var shell in _landed)
        {
            var caught = _flying[shell];
            _count++;
            _caught += caught;
            _most = Math.Max(_most, caught);
            _flying.Remove(shell);
        }
    }
}
