using Sim.Core;
using Sim.Numerics;

namespace Sim.Map;

/// <summary>
/// An entity's presence on the map: which player owns it and where its center is. Everything that
/// stands on the map has one, whether or not it can move, and a command handler reads the owner
/// from here.
/// </summary>
public struct Body : IStateHashable
{
    /// <summary>The name of the store that holds every body.</summary>
    public const string StoreName = "bodies";

    /// <summary>The owning player, numbered from 0 in the order of the match's player list.</summary>
    public int Owner { get; set; }

    /// <summary>The center, in tiles.</summary>
    public FixedVector2 Position { get; set; }

    public readonly void AddToHash(ref StateHasher hasher)
    {
        hasher.Add(Owner);
        hasher.Add(Position.X);
        hasher.Add(Position.Y);
    }
}
