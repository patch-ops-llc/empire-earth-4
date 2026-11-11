using System.Runtime.CompilerServices;
using Sim.Numerics;

namespace Sim.Core;

/// <summary>
/// What a store hands each of its fields to, one after another. The same walk serves three
/// purposes: the state hash reads every field, a snapshot writes every field, and a restore gives
/// every field the value it reads. Because there is one walk, a field cannot be in the hash and
/// missing from a snapshot, or the other way round.
/// </summary>
/// <remarks>
/// <para>
/// Each method takes the field's value and returns the value the field must have afterwards. Always
/// assign the result back: <c>Speed = walker.Field(Speed);</c>. While hashing and writing the
/// result is the value that went in. While restoring it is the value from the snapshot.
/// </para>
/// <para>
/// Walk every field that can change during a match, always in the same order, entities in
/// ascending ID order and never in the order of a hash-based collection. There is no method for a
/// floating-point value or for text, because neither belongs in the state.
/// </para>
/// <para>
/// The last parameter of each method is filled in by the compiler with the text of the argument,
/// such as <c>Speed</c>. Leave it out. Tools that show or compare states use it to name the field.
/// </para>
/// </remarks>
public interface IStateWalker
{
    /// <summary>
    /// True while a snapshot is being read into the world. A store that keeps a number of items
    /// that can change makes room for the count it gets from <see cref="Count"/> when this is true.
    /// </summary>
    bool IsRestoring { get; }

    bool Field(bool value, [CallerArgumentExpression(nameof(value))] string name = "");

    int Field(int value, [CallerArgumentExpression(nameof(value))] string name = "");

    uint Field(uint value, [CallerArgumentExpression(nameof(value))] string name = "");

    long Field(long value, [CallerArgumentExpression(nameof(value))] string name = "");

    ulong Field(ulong value, [CallerArgumentExpression(nameof(value))] string name = "");

    Int128 Field(Int128 value, [CallerArgumentExpression(nameof(value))] string name = "");

    EntityId Field(EntityId value, [CallerArgumentExpression(nameof(value))] string name = "");

    Fixed Field(Fixed value, [CallerArgumentExpression(nameof(value))] string name = "");

    Angle Field(Angle value, [CallerArgumentExpression(nameof(value))] string name = "");

    Fraction Field(Fraction value, [CallerArgumentExpression(nameof(value))] string name = "");

    /// <summary>
    /// The number of items whose fields follow, for a store that holds a number of items that can
    /// change. While restoring, the result is never negative and never more than the snapshot has
    /// bytes for, given that each item takes at least <paramref name="minimumBytesPerItem"/> bytes
    /// (four for each <see cref="int"/>, <see cref="EntityId"/> or <see cref="Fixed"/> and one for
    /// each <see cref="bool"/>), so it is safe to allocate for.
    /// </summary>
    int Count(int count, int minimumBytesPerItem = 1, [CallerArgumentExpression(nameof(count))] string name = "");
}
