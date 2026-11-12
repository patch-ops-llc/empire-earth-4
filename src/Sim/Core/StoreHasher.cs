namespace Sim.Core;

/// <summary>
/// The state hash taken apart: one hash for what the world itself holds and one for each store.
/// <see cref="World.ComputeHash"/> says that two worlds differ. These say where.
/// </summary>
/// <remarks>
/// <para>
/// The parts are found from the world: its own fields, then every store it has registered, in the
/// world's order. A match with more stores has more parts, and nothing here names a store.
/// </para>
/// <para>
/// Each part is hashed by the walk the state hash uses, so a field is in a part exactly when it is
/// in the state hash. The parts are not the state hash cut up, though: that one runs over all
/// fields in one piece, and cannot be worked out from these or these from it. Nothing here changes
/// the world, and nothing in the simulation reads these values.
/// </para>
/// </remarks>
public sealed class StoreHasher
{
    /// <summary>The name of the first part: the tick, the random generator and the list of entities.</summary>
    public const string WorldPart = "world";

    private readonly HashWalker _walker = new();

    /// <summary>The names of the parts: <see cref="WorldPart"/>, then the name of every store in the world's order.</summary>
    public static string[] PartNames(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var stores = world.Stores;
        var names = new string[stores.Count + 1];
        names[0] = WorldPart;
        for (var i = 0; i < stores.Count; i++)
        {
            names[i + 1] = stores[i].Name;
        }

        return names;
    }

    /// <summary>The number of parts a world has: one more than it has stores.</summary>
    public static int PartCount(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Stores.Count + 1;
    }

    /// <summary>
    /// Writes the hash of every part, in the order of <see cref="PartNames"/>. Call it between
    /// ticks, never from inside one.
    /// </summary>
    /// <param name="world">The world. It is not changed.</param>
    /// <param name="hashes">Receives the hashes. Its length is <see cref="PartCount"/>.</param>
    public void Compute(World world, Span<ulong> hashes)
    {
        ArgumentNullException.ThrowIfNull(world);

        var stores = world.Stores;
        if (hashes.Length != stores.Count + 1)
        {
            throw new ArgumentException($"The world has {stores.Count + 1} parts and there is room for {hashes.Length} hashes.", nameof(hashes));
        }

        // What World.Walk hands over before the stores, less the number of stores, which is the
        // same wherever the store names are.
        _walker.Reset();
        _walker.Field(world.Tick);
        world.Random.Walk(_walker);
        world.Entities.Walk(_walker);
        hashes[0] = _walker.Finish();

        for (var i = 0; i < stores.Count; i++)
        {
            _walker.Reset();
            stores[i].Walk(_walker);
            hashes[i + 1] = _walker.Finish();
        }
    }

    /// <summary>The hash of every part, in the order of <see cref="PartNames"/>, in a new array.</summary>
    public ulong[] Compute(World world)
    {
        var hashes = new ulong[PartCount(world)];
        Compute(world, hashes);
        return hashes;
    }
}
