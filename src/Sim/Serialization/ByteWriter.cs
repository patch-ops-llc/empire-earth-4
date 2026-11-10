using System.Buffers.Binary;
using System.Text;
using Sim.Core;
using Sim.Numerics;

namespace Sim.Serialization;

/// <summary>
/// Writes the project's byte format into a buffer that grows as needed. Everything that puts
/// simulation data on bytes goes through this class: encoded commands, snapshots, replay files and
/// network messages. <see cref="ByteReader"/> reads it back.
/// </summary>
/// <remarks>
/// The format is a plain sequence of values with nothing between them: no tags, no padding and no
/// lengths except where a method says so. Whole numbers are little-endian at a fixed width, so the
/// bytes are the same on every machine and every value has exactly one encoding. Each method names
/// its width: nothing is chosen by overload, so changing the type of a variable cannot silently
/// change the bytes.
/// </remarks>
public sealed class ByteWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private byte[] _buffer;
    private int _length;

    public ByteWriter(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _buffer = new byte[capacity];
    }

    /// <summary>The number of bytes written so far.</summary>
    public int Length => _length;

    /// <summary>The bytes written so far. Valid until the next write or <see cref="Clear"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    /// <summary>The bytes written so far. Valid until the next write or <see cref="Clear"/>.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);

    /// <summary>A copy of the bytes written so far.</summary>
    public byte[] ToArray() => WrittenSpan.ToArray();

    /// <summary>Forgets what was written and keeps the buffer, so one writer can serve many messages.</summary>
    public void Clear() => _length = 0;

    /// <summary>One byte.</summary>
    public void WriteByte(byte value) => Reserve(1)[0] = value;

    /// <summary>One byte: 1 for true, 0 for false.</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Two bytes.</summary>
    public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(sizeof(ushort)), value);

    /// <summary>Four bytes, two's complement.</summary>
    public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(sizeof(int)), value);

    /// <summary>Four bytes.</summary>
    public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);

    /// <summary>Eight bytes, two's complement.</summary>
    public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(sizeof(long)), value);

    /// <summary>Eight bytes.</summary>
    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(sizeof(ulong)), value);

    /// <summary>Sixteen bytes, two's complement: the low 64 bits, then the high 64 bits.</summary>
    public void WriteInt128(Int128 value)
    {
        WriteUInt64(unchecked((ulong)value));
        WriteUInt64(unchecked((ulong)(value >> 64)));
    }

    /// <summary>
    /// The number of items that follow, as four bytes. It is never negative.
    /// <see cref="ByteReader.ReadCount"/> checks it against the bytes that are left before anything
    /// is allocated for the items.
    /// </summary>
    public void WriteCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        WriteInt32(count);
    }

    /// <summary>Four bytes: the ID's number.</summary>
    public void WriteEntityId(EntityId value) => WriteInt32(value.Value);

    /// <summary>Four bytes: the raw Q16.16 value.</summary>
    public void WriteFixed(Fixed value) => WriteInt32(value.Raw);

    /// <summary>Two bytes: the angle in units of 1/4,096 of a turn, from 0 to 4,095.</summary>
    public void WriteAngle(Angle value) => WriteUInt16(checked((ushort)value.Units));

    /// <summary>Thirty-two bytes: the numerator, then the denominator, in lowest terms as a <see cref="Fraction"/> always is.</summary>
    public void WriteFraction(Fraction value)
    {
        WriteInt128(value.Numerator);
        WriteInt128(value.Denominator);
    }

    /// <summary>
    /// A count of bytes, then the text as UTF-8. Throws if the text is not valid Unicode (a
    /// surrogate without its pair), because such text has no UTF-8 form to read back.
    /// </summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The text is not valid Unicode.", nameof(value), exception);
        }

        WriteCount(byteCount);
        StrictUtf8.GetBytes(value, Reserve(byteCount));
    }

    /// <summary>The bytes as they are, with no count before them.</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));

    private Span<byte> Reserve(int count)
    {
        var end = checked(_length + count);
        if (end > _buffer.Length)
        {
            var doubled = (int)Math.Min((long)_buffer.Length * 2, Array.MaxLength);
            Array.Resize(ref _buffer, Math.Max(end, doubled));
        }

        var span = _buffer.AsSpan(_length, count);
        _length = end;
        return span;
    }
}
