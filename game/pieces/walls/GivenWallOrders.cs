using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The wall orders the local player has just given, kept for a moment so that a refusal can be
/// told from that of a build order: the simulation reports both with
/// <see cref="SimEventKind.BuildRefused"/>, which names the first tile and not the kind of order.
/// </summary>
/// <remarks>
/// An order runs a few ticks after it is given, and its refusal is reported in the tick it
/// runs in. So a refusal at the first tile of a wall order that was given within the last
/// seconds is that order's.
/// </remarks>
public sealed class GivenWallOrders
{
    // An order is carried out well within this many ticks of being given, also over a slow line.
    private const int Kept = 3 * Sim.SimInfo.TicksPerSecond;

    private readonly List<(FixedVector2 Site, int Tick)> _given = [];

    /// <summary>Notes a wall order that was given in a tick, by the center of its first tile.</summary>
    public void Note(FixedVector2 site, int tick)
    {
        _given.RemoveAll(order => tick - order.Tick > Kept);
        _given.Add((site, tick));
    }

    /// <summary>True when a refusal is that of a wall order noted here.</summary>
    public bool IsRefusalOfOne(in SimEvent happened)
    {
        if (happened.Kind != SimEventKind.BuildRefused)
        {
            return false;
        }

        foreach (var (site, tick) in _given)
        {
            if (site == happened.Position && happened.Tick - tick <= Kept)
            {
                return true;
            }
        }

        return false;
    }
}
