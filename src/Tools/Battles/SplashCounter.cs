using Sim.Core;
using Sim.Setup;

namespace Tools.Battles;

/// <summary>
/// The hits of one fight or one player that splash (design 2.3, Overgunned II), and how many
/// other units each reached.
/// </summary>
/// <param name="Made">The hits that splash: each deals part of its damage to every other enemy within the splash radius of its target.</param>
/// <param name="ReachedAnyone">How many of them reached at least one other enemy.</param>
/// <param name="Reached">The other enemies they reached, added up.</param>
/// <param name="MostReached">The most one hit reached.</param>
public sealed record SplashCount(int Made, int ReachedAnyone, int Reached, int MostReached)
{
    public static SplashCount None { get; } = new(0, 0, 0, 0);

    public double AverageReached => Made == 0 ? 0 : (double)Reached / Made;

    public double PercentThatReachedAnyone => Made == 0 ? 0 : 100.0 * ReachedAnyone / Made;

    public SplashCount Plus(SplashCount other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Made + other.Made, ReachedAnyone + other.ReachedAnyone, Reached + other.Reached, Math.Max(MostReached, other.MostReached));
    }
}

/// <summary>
/// Counts what each splash reaches, from the events of a match: a hit that splashes is reported
/// once, whether or not it reached anyone, and once more for every other enemy it reached. Both
/// name the shot, or the attacker of a melee hit, and a shot lands once and a unit hits once in
/// a tick, so the two are matched within the tick.
/// </summary>
public sealed class SplashCounter
{
    private readonly int? _player;
    private readonly Dictionary<EntityId, int> _open = [];
    private int _made;
    private int _reachedAnyone;
    private int _reached;
    private int _most;

    /// <param name="player">The player whose hits are counted, or null for those of every player.</param>
    public SplashCounter(int? player = null) => _player = player;

    public SplashCount Count => new(_made, _reachedAnyone, _reached, _most);

    /// <summary>Takes in the tick the match has just run.</summary>
    public void AfterTick(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        foreach (var simEvent in match.Simulation.Events)
        {
            if (_player is { } player && simEvent.Player != player)
            {
                continue;
            }

            if (simEvent.Kind == SimEventKind.Splash)
            {
                Splashed(simEvent.Entity);
            }
            else if (simEvent.Kind == SimEventKind.SplashHit)
            {
                Reached(simEvent.Other);
            }
        }

        EndOfTick();
    }

    /// <summary>A hit that splashes landed: <paramref name="source"/> is its shot, or the attacker of a melee hit.</summary>
    public void Splashed(EntityId source) => _open[source] = 0;

    /// <summary>The splash of <paramref name="source"/> reached another enemy.</summary>
    public void Reached(EntityId source)
    {
        if (_open.TryGetValue(source, out var reached))
        {
            _open[source] = reached + 1;
        }
    }

    /// <summary>Counts every splash of the tick.</summary>
    public void EndOfTick()
    {
        foreach (var reached in _open.Values)
        {
            _made++;
            _reachedAnyone += reached > 0 ? 1 : 0;
            _reached += reached;
            _most = Math.Max(_most, reached);
        }

        _open.Clear();
    }
}
