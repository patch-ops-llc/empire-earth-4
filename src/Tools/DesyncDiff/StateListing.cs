using System.Globalization;
using Sim.Core;
using Sim.Numerics;

namespace Tools.DesyncDiff;

/// <summary>One field of the state as text: where it is, what the code calls it and its value.</summary>
/// <param name="Section">The store's name, or <see cref="StateListing.WorldSection"/> for what the world itself holds.</param>
/// <param name="Item">The entity the field belongs to, such as <c>entity 7</c>, or empty for a field of the store itself.</param>
/// <param name="Name">The field, as the walk names it: the text of the argument, such as <c>Destination.X</c>.</param>
public readonly record struct StateField(string Section, string Item, string Name, string Value);

/// <summary>
/// Every field of a world in the order the state walk visits them, as text. It is the same walk
/// the state hash and a snapshot use, so nothing they cover is missing here and nothing else is
/// listed.
/// </summary>
public static class StateListing
{
    /// <summary>The section of the tick, the random generator and the entity registry.</summary>
    public const string WorldSection = "world";

    /// <summary>
    /// The names the stores give to the field that starts a new item: the entity's ID in a
    /// component store, and in the registry. A store that names it otherwise is still listed and
    /// compared, field by field in order, only without the entity beside each field.
    /// </summary>
    private static readonly HashSet<string> ItemKeys = new(StringComparer.Ordinal) { "_entities[i]", "_ids[i]" };

    public static List<StateField> Of(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // The walk of the whole world is the world's own fields, the number of stores, then each
        // store. Walking the stores one by one as well says where each begins.
        var whole = new Lister(WorldSection);
        world.Walk(whole);

        var fields = new List<StateField>();
        var stores = new List<StateField>();
        foreach (var store in world.Stores)
        {
            var lister = new Lister(store.Name);
            store.Walk(lister);
            stores.AddRange(lister.Fields);
        }

        var ownCount = whole.Fields.Count - stores.Count;
        if (ownCount < 0)
        {
            throw new InvalidOperationException("The stores hold more fields than the world they are in.");
        }

        fields.AddRange(whole.Fields.Take(ownCount));
        fields.AddRange(stores);
        return fields;
    }

    private sealed class Lister(string section) : IStateWalker
    {
        private string _item = "";

        public List<StateField> Fields { get; } = [];

        public bool IsRestoring => false;

        public bool Field(bool value, string name = "") => Add(value, name, value ? "true" : "false");

        public int Field(int value, string name = "")
        {
            StartItem(name, value);
            return Add(value, name, Text(value));
        }

        public uint Field(uint value, string name = "") => Add(value, name, Text(value));

        public long Field(long value, string name = "") => Add(value, name, Text(value));

        public ulong Field(ulong value, string name = "") => Add(value, name, Text(value));

        public Int128 Field(Int128 value, string name = "") => Add(value, name, Text(value));

        public EntityId Field(EntityId value, string name = "")
        {
            StartItem(name, value.Value);
            return Add(value, name, Text(value.Value));
        }

        public Fixed Field(Fixed value, string name = "") =>
            Add(value, name, string.Create(CultureInfo.InvariantCulture, $"{value.Raw / (double)Fixed.RawOne:0.#####} (raw {value.Raw})"));

        public Angle Field(Angle value, string name = "") => Add(value, name, Text(value.Units));

        public Fraction Field(Fraction value, string name = "") => Add(value, name, string.Create(CultureInfo.InvariantCulture, $"{value}"));

        public int Count(int count, int minimumBytesPerItem = 1, string name = "")
        {
            // A new list begins: what follows no longer belongs to the last item of the one before.
            _item = "";
            return Add(count, name, Text(count));
        }

        private static string Text<T>(T value)
            where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

        private void StartItem(string name, int id)
        {
            if (ItemKeys.Contains(name))
            {
                _item = string.Create(CultureInfo.InvariantCulture, $"entity {id}");
            }
        }

        private T Add<T>(T value, string name, string text)
        {
            Fields.Add(new StateField(section, _item, name, text));
            return value;
        }
    }
}
