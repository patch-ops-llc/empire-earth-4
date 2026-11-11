using Sim.Numerics;

namespace Sim.Core;

/// <summary>The walker behind <see cref="World.ComputeHash"/>: it feeds every field to a <see cref="StateHasher"/> and changes none.</summary>
internal sealed class HashWalker : IStateWalker
{
    private StateHasher _hasher;

    public bool IsRestoring => false;

    public void Reset() => _hasher = default;

    public ulong Finish() => _hasher.Finish();

    public bool Field(bool value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public int Field(int value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public uint Field(uint value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public long Field(long value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public ulong Field(ulong value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public Int128 Field(Int128 value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public EntityId Field(EntityId value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public Fixed Field(Fixed value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public Angle Field(Angle value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public Fraction Field(Fraction value, string name = "")
    {
        _hasher.Add(value);
        return value;
    }

    public int Count(int count, int minimumBytesPerItem = 1, string name = "")
    {
        _hasher.Add(count);
        return count;
    }
}
