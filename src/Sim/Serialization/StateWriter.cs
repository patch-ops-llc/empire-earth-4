using Sim.Core;
using Sim.Numerics;

namespace Sim.Serialization;

/// <summary>The walker that writes a snapshot: every field goes to a <see cref="ByteWriter"/> and none is changed.</summary>
internal sealed class StateWriter(ByteWriter writer) : IStateWalker
{
    public bool IsRestoring => false;

    public bool Field(bool value, string name = "")
    {
        writer.WriteBool(value);
        return value;
    }

    public int Field(int value, string name = "")
    {
        writer.WriteInt32(value);
        return value;
    }

    public uint Field(uint value, string name = "")
    {
        writer.WriteUInt32(value);
        return value;
    }

    public long Field(long value, string name = "")
    {
        writer.WriteInt64(value);
        return value;
    }

    public ulong Field(ulong value, string name = "")
    {
        writer.WriteUInt64(value);
        return value;
    }

    public Int128 Field(Int128 value, string name = "")
    {
        writer.WriteInt128(value);
        return value;
    }

    public EntityId Field(EntityId value, string name = "")
    {
        writer.WriteEntityId(value);
        return value;
    }

    public Fixed Field(Fixed value, string name = "")
    {
        writer.WriteFixed(value);
        return value;
    }

    public Angle Field(Angle value, string name = "")
    {
        writer.WriteAngle(value);
        return value;
    }

    public Fraction Field(Fraction value, string name = "")
    {
        writer.WriteFraction(value);
        return value;
    }

    public int Count(int count, int minimumBytesPerItem = 1, string name = "")
    {
        writer.WriteCount(count);
        return count;
    }
}
