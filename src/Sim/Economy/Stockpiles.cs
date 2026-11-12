using Sim.Core;

namespace Sim.Economy;

/// <summary>
/// Every player's stockpile of Materials and Energy (design 4.1), counted in thousandths of a
/// resource, because income arrives in fractions such as 0.75 Materials a second. Prices are whole
/// resources.
/// </summary>
/// <remarks>
/// This is the least a price needs: an amount for each player and resource, paying and adding.
/// Income, supply and the rest of the economy are not here.
/// </remarks>
public sealed class Stockpiles : IStateStore
{
    public const string StoreName = "stockpiles";

    /// <summary>How many of the counted units make one resource.</summary>
    public const long PerResource = 1000;

    private readonly long[] _materials;
    private readonly long[] _energy;

    public Stockpiles(int players)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(players);
        _materials = new long[players];
        _energy = new long[players];
    }

    public string Name => StoreName;

    public int PlayerCount => _materials.Length;

    /// <summary>The player's Materials, in thousandths.</summary>
    public long Materials(int player) => _materials[player];

    /// <summary>The player's Energy, in thousandths.</summary>
    public long Energy(int player) => _energy[player];

    /// <summary>Whether the player has at least this price, given in whole resources.</summary>
    public bool Covers(int player, int materials, int energy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(materials);
        ArgumentOutOfRangeException.ThrowIfNegative(energy);
        return _materials[player] >= materials * PerResource && _energy[player] >= energy * PerResource;
    }

    /// <summary>
    /// Takes a price, given in whole resources, from the player's stockpile. Returns false, having
    /// taken nothing, when the stockpile does not cover both amounts.
    /// </summary>
    public bool TryPay(int player, int materials, int energy)
    {
        if (!Covers(player, materials, energy))
        {
            return false;
        }

        _materials[player] -= materials * PerResource;
        _energy[player] -= energy * PerResource;
        return true;
    }

    /// <summary>Adds whole resources to the player's stockpile: a refund, or what a player starts with.</summary>
    public void Add(int player, int materials, int energy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(materials);
        ArgumentOutOfRangeException.ThrowIfNegative(energy);
        _materials[player] = checked(_materials[player] + (materials * PerResource));
        _energy[player] = checked(_energy[player] + (energy * PerResource));
    }

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        for (var player = 0; player < _materials.Length; player++)
        {
            _materials[player] = walker.Field(_materials[player]);
            _energy[player] = walker.Field(_energy[player]);
            if (_materials[player] < 0 || _energy[player] < 0)
            {
                throw new InvalidDataException($"The stockpile of player {player} is below zero.");
            }
        }
    }
}
