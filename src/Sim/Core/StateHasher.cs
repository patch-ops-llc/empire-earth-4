using System.Numerics;
using Sim.Numerics;

namespace Sim.Core;

/// <summary>
/// Accumulates the 64-bit state hash that peers compare to detect a desync. It is fed whole
/// numbers, one 64-bit word each, and nothing else: there is no overload for floating-point values
/// or for strings.
/// </summary>
/// <remarks>
/// The result depends on every word and on their order and count, and is the same on every machine
/// because it uses only wrapping 64-bit integer arithmetic. The mixing steps and constants are
/// those of xxHash64's handling of 8-byte input. This is a mutable struct: pass it by reference.
/// </remarks>
public struct StateHasher
{
    private const ulong Prime1 = 0x9E3779B185EBCA87;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4F;
    private const ulong Prime3 = 0x165667B19E3779F9;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63;
    private const ulong Prime5 = 0x27D4EB2F165667C5;

    private ulong _hash;
    private ulong _count;

    public void Add(ulong value)
    {
        unchecked
        {
            var mixed = BitOperations.RotateLeft(value * Prime2, 31) * Prime1;
            _hash = (BitOperations.RotateLeft(_hash ^ mixed, 27) * Prime1) + Prime4;
            _count++;
        }
    }

    /// <summary>Adds the value's 64 bits as they are, so -1 adds the same word as <see cref="ulong.MaxValue"/>.</summary>
    public void Add(long value) => Add(unchecked((ulong)value));

    /// <summary>Adds the value widened to 64 bits with its sign, so it hashes like the same <see cref="long"/>.</summary>
    public void Add(int value) => Add((long)value);

    public void Add(uint value) => Add((ulong)value);

    public void Add(bool value) => Add(value ? 1UL : 0UL);

    /// <summary>Adds two words: the low 64 bits, then the high 64 bits.</summary>
    public void Add(Int128 value)
    {
        Add(unchecked((ulong)value));
        Add(unchecked((ulong)(value >> 64)));
    }

    public void Add(EntityId value) => Add(value.Value);

    public void Add(Fixed value) => Add(value.Raw);

    public void Add(Angle value) => Add(value.Units);

    /// <summary>Adds four words: the numerator, then the denominator.</summary>
    public void Add(Fraction value)
    {
        Add(value.Numerator);
        Add(value.Denominator);
    }

    /// <summary>The hash of everything added so far. More can be added afterwards.</summary>
    public readonly ulong Finish()
    {
        unchecked
        {
            var hash = _hash + (_count * Prime5);
            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            return hash;
        }
    }
}
