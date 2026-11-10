namespace Sim.Core;

/// <summary>
/// The simulation's only source of randomness: a seeded SplitMix64 generator owned by the
/// <see cref="World"/>. Its whole state is one 64-bit number, which is part of the state hash.
/// System.Random is never used in the simulation.
/// </summary>
/// <remarks>
/// Every draw changes the state, so the order and number of draws must be the same on every
/// machine. Draw only inside a tick, from code that runs in a fixed order.
/// </remarks>
public sealed class DeterministicRandom
{
    /// <summary>
    /// Starts the sequence at a seed. A generator made from the <see cref="State"/> of another
    /// continues that generator's sequence, which is how a snapshot restores it.
    /// </summary>
    public DeterministicRandom(ulong seed) => State = seed;

    /// <summary>The whole state of the generator.</summary>
    public ulong State { get; private set; }

    /// <summary>The next 64 random bits.</summary>
    public ulong NextUInt64()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15;
            var z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A whole number from 0 up to but not including <paramref name="exclusiveMax"/>, each equally likely.</summary>
    public ulong NextUInt64(ulong exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveMax);

        // Lemire's method: the high half of a 128-bit product, redrawing in the rare case that
        // would make some results more likely than others.
        var product = (UInt128)NextUInt64() * exclusiveMax;
        var low = unchecked((ulong)product);
        if (low < exclusiveMax)
        {
            var threshold = unchecked(0UL - exclusiveMax) % exclusiveMax;
            while (low < threshold)
            {
                product = (UInt128)NextUInt64() * exclusiveMax;
                low = unchecked((ulong)product);
            }
        }

        return (ulong)(product >> 64);
    }

    /// <summary>A whole number from 0 up to but not including <paramref name="exclusiveMax"/>, each equally likely.</summary>
    public int NextInt(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)NextUInt64((ulong)exclusiveMax);
    }

    /// <summary>
    /// A whole number from <paramref name="inclusiveMin"/> up to but not including
    /// <paramref name="exclusiveMax"/>, each equally likely.
    /// </summary>
    public int NextInt(int inclusiveMin, int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(inclusiveMin, exclusiveMax);
        var span = (ulong)((long)exclusiveMax - inclusiveMin);
        return (int)(inclusiveMin + (long)NextUInt64(span));
    }
}
