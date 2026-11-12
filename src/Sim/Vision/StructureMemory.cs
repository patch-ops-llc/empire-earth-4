using System.Globalization;
using Sim.Core;
using Sim.Map;

namespace Sim.Vision;

/// <summary>
/// A structure of another player as a player last saw it (design 5.3): where it stands, what it
/// is, and how it looked. It is written while the player sees the structure and kept as it is
/// from the moment the player no longer does.
/// </summary>
public struct RememberedStructure
{
    /// <summary>The structure. It may be gone by now: the entity is then no longer alive.</summary>
    public EntityId Entity { get; set; }

    /// <summary>The player it belonged to.</summary>
    public int Owner { get; set; }

    /// <summary>Its place in the roster, as in <see cref="Data.StructureDef.Index"/>.</summary>
    public int Type { get; set; }

    /// <summary>The x of the lowest tile it covers.</summary>
    public int X { get; set; }

    /// <summary>The y of the lowest tile it covers.</summary>
    public int Y { get; set; }

    /// <summary>The tiles it covers along each side.</summary>
    public int Size { get; set; }

    /// <summary>Its hit points when last seen, in thousandths.</summary>
    public long HitPoints { get; set; }

    /// <summary>Its hit points at most when last seen, in thousandths.</summary>
    public long MaxHitPoints { get; set; }

    /// <summary>False while it was still building itself when last seen.</summary>
    public bool IsFinished { get; set; }

    /// <summary>The ticks it had built for when last seen: its whole build time once it is finished.</summary>
    public int Built { get; set; }

    /// <summary>
    /// Its owner's tier, when last seen, in the track whose look the structure shows (design 2.5
    /// and 3.2), or zero for a structure that looks the same at every tier.
    /// </summary>
    public int LookTier { get; set; }

    /// <summary>
    /// The track that was in research in the slot the structure gives when last seen, which its
    /// banner shows (design 3.2), or <see cref="NoTrack"/>.
    /// </summary>
    public int Banner { get; set; }

    /// <summary>The number of the tick in which it was last seen.</summary>
    public int Tick { get; set; }

    /// <summary>The <see cref="Banner"/> of a structure that flies none.</summary>
    public const int NoTrack = -1;

    /// <summary>The tiles it covers.</summary>
    public readonly TileSquare Tiles => new(X, Y, Size);
}

/// <summary>
/// What each player remembers of the structures of the others (design 5.3): for each player, the
/// structures it has seen, each as it was when last seen. It is state: an attack order may name
/// a structure its player remembers, and a build order is judged against the structures its
/// player knows of, so every machine must remember the same.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MatchVision.Refresh"/> writes it, whenever sight is worked out: a structure a
/// player sees is written down as it is, and one that is gone is forgotten once the player sees
/// one of its tiles without it. In between it stays as it is, so a snapshot keeps it and a
/// restore does not work it out again.
/// </para>
/// <para>
/// A memory outlives its structure: nothing is dropped when an entity is destroyed. The player
/// does not know of that until it looks.
/// </para>
/// <para>
/// A player's memories are kept in ascending order of the structures' entity IDs.
/// </para>
/// </remarks>
public sealed class StructureMemory : IStateStore
{
    public const string StoreName = "remembered";

    // Entity, owner, type, x, y, size: 4 bytes each. Two hit point values of 8. The flag is 1. Built, look tier, banner, tick: 4 each.
    private const int BytesPerItem = (6 * sizeof(int)) + (2 * sizeof(long)) + 1 + (4 * sizeof(int));

    private static readonly string[] FieldNames =
        ["Owner", "Type", "X", "Y", "Size", "HitPoints", "MaxHitPoints", "IsFinished", "Built", "LookTier", "Banner", "Tick"];

    private readonly int _width;
    private readonly int _height;
    private readonly RememberedStructure[][] _items;
    private readonly int[] _counts;

    // What the walk calls each field. Made once, so that the walk, which the state hash runs, makes no text.
    private readonly string[] _countNames;
    private readonly string[][] _fieldNames;

    public StructureMemory(int players, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(players);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        PlayerCount = players;
        _width = width;
        _height = height;
        _items = new RememberedStructure[players][];
        _counts = new int[players];
        _countNames = new string[players];
        _fieldNames = new string[players][];
        for (var player = 0; player < players; player++)
        {
            _items[player] = [];
            var prefix = string.Create(CultureInfo.InvariantCulture, $"[{player}]");
            _countNames[player] = "count" + prefix;
            _fieldNames[player] = [.. FieldNames.Select(field => prefix + "." + field)];
        }
    }

    public string Name => StoreName;

    public int PlayerCount { get; }

    /// <summary>How many structures the player remembers, those it sees now among them.</summary>
    public int Count(int player)
    {
        CheckPlayer(player);
        return _counts[player];
    }

    /// <summary>One of the player's memories, by its place in ascending order of entity IDs.</summary>
    public ref readonly RememberedStructure At(int player, int index)
    {
        CheckPlayer(player);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _counts[player]);
        return ref _items[player][index];
    }

    /// <summary>The place of the player's memory of a structure, or a negative number when it has none.</summary>
    public int IndexOf(int player, EntityId entity)
    {
        if (player < 0 || player >= PlayerCount)
        {
            return -1;
        }

        var items = _items[player];
        var low = 0;
        var high = _counts[player] - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var found = items[middle].Entity;
            if (found == entity)
            {
                return middle;
            }

            if (found < entity)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    /// <summary>The player's memory of a structure. False when it has none.</summary>
    public bool TryGet(int player, EntityId entity, out RememberedStructure seen)
    {
        var index = IndexOf(player, entity);
        seen = index >= 0 ? _items[player][index] : default;
        return index >= 0;
    }

    /// <summary>Writes down a structure as a player sees it now, in place of what the player remembered of it.</summary>
    public void Set(int player, in RememberedStructure seen)
    {
        CheckPlayer(player);
        Check(player, in seen);
        var index = IndexOf(player, seen.Entity);
        if (index >= 0)
        {
            _items[player][index] = seen;
            return;
        }

        index = ~index;
        var count = _counts[player];
        if (count == _items[player].Length)
        {
            Array.Resize(ref _items[player], Math.Max(8, 2 * count));
        }

        var items = _items[player];
        Array.Copy(items, index, items, index + 1, count - index);
        items[index] = seen;
        _counts[player] = count + 1;
    }

    /// <summary>Forgets one of the player's memories, by its place.</summary>
    public void RemoveAt(int player, int index)
    {
        CheckPlayer(player);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _counts[player]);
        var items = _items[player];
        var count = --_counts[player];
        Array.Copy(items, index + 1, items, index, count - index);
        items[count] = default;
    }

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
        // A memory outlives its structure.
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        for (var player = 0; player < PlayerCount; player++)
        {
            var oldCount = _counts[player];
            var count = _counts[player] = walker.Count(oldCount, BytesPerItem, _countNames[player]);
            if (count > _items[player].Length)
            {
                Array.Resize(ref _items[player], Math.Max(count, 2 * _items[player].Length));
            }

            var items = _items[player];
            if (walker.IsRestoring)
            {
                // Nothing of what the store held before may show through what the walk assigns.
                Array.Clear(items, 0, Math.Max(oldCount, count));
            }

            var names = _fieldNames[player];
            var previous = EntityId.None;
            for (var i = 0; i < count; i++)
            {
                ref var item = ref items[i];

                // Named as a component store names its entities, which the tools that list a state go by.
                item.Entity = walker.Field(item.Entity, "_entities[i]");
                item.Owner = walker.Field(item.Owner, names[0]);
                item.Type = walker.Field(item.Type, names[1]);
                item.X = walker.Field(item.X, names[2]);
                item.Y = walker.Field(item.Y, names[3]);
                item.Size = walker.Field(item.Size, names[4]);
                item.HitPoints = walker.Field(item.HitPoints, names[5]);
                item.MaxHitPoints = walker.Field(item.MaxHitPoints, names[6]);
                item.IsFinished = walker.Field(item.IsFinished, names[7]);
                item.Built = walker.Field(item.Built, names[8]);
                item.LookTier = walker.Field(item.LookTier, names[9]);
                item.Banner = walker.Field(item.Banner, names[10]);
                item.Tick = walker.Field(item.Tick, names[11]);

                if (!walker.IsRestoring)
                {
                    continue;
                }

                if (item.Entity <= previous)
                {
                    throw new InvalidDataException(
                        $"Entity IDs in store '{Name}' must be positive and ascend for each player: {item.Entity} follows {previous}.");
                }

                previous = item.Entity;
                if (Problem(player, in item) is { } problem)
                {
                    throw new InvalidDataException($"Player {player}'s memory of structure {item.Entity} {problem}.");
                }
            }
        }
    }

    private void Check(int player, in RememberedStructure seen)
    {
        if (seen.Entity <= EntityId.None)
        {
            throw new ArgumentException("A remembered structure names its entity.", nameof(seen));
        }

        if (Problem(player, in seen) is { } problem)
        {
            throw new ArgumentException($"Player {player}'s memory of structure {seen.Entity} {problem}.", nameof(seen));
        }
    }

    /// <summary>What is wrong with a memory, as the end of a sentence, or null.</summary>
    private string? Problem(int player, in RememberedStructure seen) =>
        seen.Owner < 0 || seen.Owner >= PlayerCount ? "names an owner who is not in the match"
        : seen.Owner == player ? "is of one of the player's own structures"
        : seen.Size < 1 || seen.X < 0 || seen.Y < 0 || seen.X > _width - seen.Size || seen.Y > _height - seen.Size ? "has tiles that are not on the map"
        : seen.Type < 0 || seen.Built < 0 || seen.LookTier < 0 || seen.Banner < RememberedStructure.NoTrack || seen.Tick < 0 ? "has a number below what it can be"
        : seen.MaxHitPoints < 0 ? "has hit points below what it can have"
        : null;

    private void CheckPlayer(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
    }
}
