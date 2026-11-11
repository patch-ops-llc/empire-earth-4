using Sim.Core;
using Sim.Numerics;

namespace Sim.Serialization;

/// <summary>The walker that restores a snapshot: every field gets the value read from a <see cref="ByteReader"/>.</summary>
internal sealed class StateReader(ByteReader reader) : IStateWalker
{
    public bool IsRestoring => true;

    public bool Field(bool value, string name = "") => reader.ReadBool();

    public int Field(int value, string name = "") => reader.ReadInt32();

    public uint Field(uint value, string name = "") => reader.ReadUInt32();

    public long Field(long value, string name = "") => reader.ReadInt64();

    public ulong Field(ulong value, string name = "") => reader.ReadUInt64();

    public Int128 Field(Int128 value, string name = "") => reader.ReadInt128();

    public EntityId Field(EntityId value, string name = "") => reader.ReadEntityId();

    public Fixed Field(Fixed value, string name = "") => reader.ReadFixed();

    public Angle Field(Angle value, string name = "") => reader.ReadAngle();

    public Fraction Field(Fraction value, string name = "") => reader.ReadFraction();

    public int Count(int count, int minimumBytesPerItem = 1, string name = "") => reader.ReadCount(minimumBytesPerItem);
}
