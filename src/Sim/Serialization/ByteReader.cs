using System.Buffers.Binary;
using System.Text;
using Sim.Core;
using Sim.Numerics;

namespace Sim.Serialization;

/// <summary>
/// Reads what a <see cref="ByteWriter"/> wrote, value by value in the same order.
/// </summary>
/// <remarks>
/// The bytes may come from another machine or from a file, so nothing about them is trusted. Every
/// way they can be wrong (too short, a value its type cannot hold, a count larger than the bytes
/// left) throws <see cref="InvalidDataException"/> and nothing else, before any memory is allocated
/// for what a count announces. Every value has exactly one encoding, so bytes that were read
/// without an error write back as the same bytes.
/// </remarks>
public sealed class ByteReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    public ByteReader(ReadOnlyMemory<byte> data) => _data = data;

    /// <summary>The number of bytes read so far.</summary>
    public int Position => _position;

    /// <summary>The number of bytes not read yet.</summary>
    public int Remaining => _data.Length - _position;

    public bool IsAtEnd => _position == _data.Length;

    /// <summary>Throws unless every byte has been read. Call it after the last value of a message or file.</summary>
    public void ExpectEnd()
    {
        if (!IsAtEnd)
        {
            throw new InvalidDataException($"{Remaining} bytes are left over after the last value, at offset {_position}.");
        }
    }

    public byte ReadByte() => Take(1)[0];

    /// <summary>One byte, which must be 0 or 1.</summary>
    public bool ReadBool()
    {
        var value = ReadByte();
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw Invalid($"a true-or-false byte is {value}, not 0 or 1", 1),
        };
    }

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));

    public Int128 ReadInt128()
    {
        var low = ReadUInt64();
        var high = ReadUInt64();
        return unchecked((Int128)new UInt128(high, low));
    }

    /// <summary>
    /// The number of items that follow. Throws if it is negative, or if that many items of at
    /// least <paramref name="minimumBytesPerItem"/> bytes each cannot fit in the bytes that are
    /// left. A caller can therefore allocate for the count it gets: it is never larger than the
    /// input.
    /// </summary>
    public int ReadCount(int minimumBytesPerItem)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumBytesPerItem);

        var count = ReadInt32();
        if (count < 0)
        {
            throw Invalid($"a count is {count}", sizeof(int));
        }

        if ((long)count * minimumBytesPerItem > Remaining)
        {
            throw Invalid($"a count of {count} items needs more than the {Remaining} bytes that are left", sizeof(int));
        }

        return count;
    }

    public EntityId ReadEntityId() => new(ReadInt32());

    public Fixed ReadFixed() => Fixed.FromRaw(ReadInt32());

    /// <summary>Two bytes, which must be less than 4,096.</summary>
    public Angle ReadAngle()
    {
        var units = ReadUInt16();
        if (units >= Angle.UnitsPerTurn)
        {
            throw Invalid($"an angle is {units} units, not less than {Angle.UnitsPerTurn}", sizeof(ushort));
        }

        return Angle.FromUnits(units);
    }

    /// <summary>Thirty-two bytes, which must be a fraction in lowest terms with a positive denominator.</summary>
    public Fraction ReadFraction()
    {
        var numerator = ReadInt128();
        var denominator = ReadInt128();
        if (denominator > 0)
        {
            Fraction fraction;
            try
            {
                fraction = new Fraction(numerator, denominator);
            }
            catch (OverflowException)
            {
                throw Invalid("a fraction is outside the range of the type", 32);
            }

            if (fraction.Numerator == numerator && fraction.Denominator == denominator)
            {
                return fraction;
            }
        }

        throw Invalid("a fraction is not in lowest terms with a positive denominator", 32);
    }

    /// <summary>A count of bytes, then that many bytes of text, which must be valid UTF-8.</summary>
    public string ReadString()
    {
        var byteCount = ReadCount(1);
        var bytes = Take(byteCount);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("a piece of text is not valid UTF-8", byteCount);
        }
    }

    /// <summary>The next <paramref name="count"/> bytes as they are. The span points into the reader's input.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Take(count);
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new InvalidDataException($"The data ends too soon: {count} bytes are needed at offset {_position} and {Remaining} are left.");
        }

        var span = _data.Span.Slice(_position, count);
        _position += count;
        return span;
    }

    private InvalidDataException Invalid(string problem, int valueSize) =>
        new($"At offset {_position - valueSize}, {problem}.");
}
