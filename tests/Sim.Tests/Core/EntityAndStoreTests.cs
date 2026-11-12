using Sim.Core;

namespace Sim.Tests.Core;

public class EntityAndStoreTests
{
    private struct Health : IStateValue
    {
        public int Points;

        public void Walk(IStateWalker walker) => Points = walker.Field(Points);
    }

    private static EntityId[] EntitiesOf(ComponentStore<Health> store) =>
        [.. Enumerable.Range(0, store.Count).Select(store.EntityAt)];

    private static EntityId[] EntitiesOf(EntityRegistry entities) =>
        [.. Enumerable.Range(0, entities.Count).Select(index => entities[index])];

    [Fact]
    public void IdsAscendFromOneAndAreNeverReused()
    {
        var world = new World(0);

        var first = world.Entities.Create();
        var second = world.Entities.Create();
        Assert.Equal(new EntityId(1), first);
        Assert.Equal(new EntityId(2), second);
        Assert.True(first < second);

        world.Entities.Destroy(second);
        world.FlushDestroyedEntities();
        var third = world.Entities.Create();

        Assert.Equal(new EntityId(3), third);
        Assert.Equal([first, third], EntitiesOf(world.Entities));
        Assert.Equal("#3", third.ToString());
    }

    [Fact]
    public void NoneIsNeverAlive()
    {
        var world = new World(0);
        world.Entities.Create();

        Assert.Equal(default, EntityId.None);
        Assert.False(world.Entities.IsAlive(EntityId.None));
        Assert.False(world.Entities.IsAlive(new EntityId(2)));
        Assert.False(world.Entities.IsAlive(new EntityId(-1)));
        Assert.False(world.Entities.Destroy(EntityId.None));
        Assert.False(world.Entities.Destroy(new EntityId(2)));
    }

    [Fact]
    public void DestroyTakesEffectAtOnceButDataStaysUntilTheFlush()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        var a = world.Entities.Create();
        var b = world.Entities.Create();
        var c = world.Entities.Create();
        health.Add(a, new Health { Points = 10 });
        health.Add(b, new Health { Points = 20 });
        health.Add(c, new Health { Points = 30 });

        Assert.True(world.Entities.Destroy(b));
        Assert.False(world.Entities.Destroy(b));

        Assert.False(world.Entities.IsAlive(b));
        Assert.Equal(3, world.Entities.Count);
        Assert.Equal(2, world.Entities.AliveCount);
        Assert.Equal([a, b, c], EntitiesOf(health));
        Assert.Equal(20, health.Get(b).Points);

        world.FlushDestroyedEntities();

        Assert.Equal(2, world.Entities.Count);
        Assert.Equal(2, world.Entities.AliveCount);
        Assert.Equal([a, c], EntitiesOf(world.Entities));
        Assert.Equal([a, c], EntitiesOf(health));
        Assert.False(health.Contains(b));
        Assert.Equal(30, health.Get(c).Points);
        Assert.True(world.Entities.IsAlive(a) && world.Entities.IsAlive(c));
    }

    [Fact]
    public void AFlushRemovesDestroyedEntitiesFromEveryStore()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        var armor = world.AddStore(new ComponentStore<Health>("armor"));
        var entities = Enumerable.Range(0, 200).Select(_ => world.Entities.Create()).ToArray();
        foreach (var entity in entities)
        {
            health.Add(entity, new Health { Points = entity.Value });
            if (entity.Value % 3 == 0)
            {
                armor.Add(entity, new Health { Points = -entity.Value });
            }
        }

        // Destroyed out of order, including the first and the last.
        foreach (var entity in entities.Reverse().Where(entity => entity.Value % 2 == 0 || entity.Value == 1 || entity.Value == 199))
        {
            world.Entities.Destroy(entity);
        }

        world.FlushDestroyedEntities();

        var survivors = entities.Where(entity => entity.Value % 2 == 1 && entity.Value != 1 && entity.Value != 199).ToArray();
        Assert.Equal(survivors, EntitiesOf(world.Entities));
        Assert.Equal(survivors, EntitiesOf(health));
        Assert.Equal(survivors.Where(entity => entity.Value % 3 == 0), EntitiesOf(armor));
        Assert.All(survivors, entity => Assert.Equal(entity.Value, health.Get(entity).Points));
        Assert.All(EntitiesOf(armor), entity => Assert.Equal(-entity.Value, armor.Get(entity).Points));

        // A second flush with nothing destroyed changes nothing.
        var hash = world.ComputeHash();
        world.FlushDestroyedEntities();
        Assert.Equal(hash, world.ComputeHash());
    }

    [Fact]
    public void AStoreKeepsEntityOrderWhateverTheOrderOfAdding()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        var entities = Enumerable.Range(0, 50).Select(_ => world.Entities.Create()).ToArray();

        // 17 and 50 share no factor, so this visits every entity once in a scrambled order.
        for (var i = 0; i < 50; i++)
        {
            var entity = entities[i * 17 % 50];
            health.Add(entity, new Health { Points = entity.Value * 2 });
        }

        Assert.Equal(50, health.Count);
        Assert.Equal(entities, EntitiesOf(health));
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(entities[i].Value * 2, health.ValueAt(i).Points);
            Assert.Equal(i, health.IndexOf(entities[i]));
        }
    }

    /// <summary>
    /// Every lookup of a store and of the list of entities, against a walk over what they hold:
    /// stores of every size from none, with IDs missing before, between and after the ones
    /// there, and entities destroyed and not yet removed.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void ALookupFindsEveryEntityThatIsThereAndNoOther(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        for (var size = 0; size <= 70; size++)
        {
            var world = new World(0);
            var health = world.AddStore(new ComponentStore<Health>("health"));
            var created = Enumerable.Range(0, size).Select(_ => world.Entities.Create()).ToArray();
            foreach (var entity in created.Where(_ => random.NextInt(3) > 0))
            {
                health.Add(entity, new Health { Points = entity.Value });
            }

            // Some are gone for good, which leaves gaps in both, and some are destroyed and still listed.
            foreach (var entity in created.Where(_ => random.NextInt(4) == 0))
            {
                world.Entities.Destroy(entity);
            }

            world.FlushDestroyedEntities();
            var destroyed = created.Where(entity => world.Entities.IsAlive(entity) && random.NextInt(4) == 0).ToArray();
            foreach (var entity in destroyed)
            {
                world.Entities.Destroy(entity);
            }

            var stored = EntitiesOf(health);
            var listed = EntitiesOf(world.Entities);
            for (var value = -2; value <= size + 2; value++)
            {
                var entity = new EntityId(value);
                var index = Array.IndexOf(stored, entity);
                var position = Array.IndexOf(listed, entity);
                var alive = position >= 0 && !destroyed.Contains(entity);

                Assert.Equal(index, health.IndexOf(entity));
                Assert.Equal(index >= 0, health.Contains(entity));
                Assert.Equal(index >= 0, health.TryGet(entity, out var found));
                Assert.Equal(index >= 0 ? value : 0, found.Points);
                Assert.Equal(position, world.Entities.PositionOf(entity));
                Assert.Equal(alive, world.Entities.IsAlive(entity));

                // A hint changes how the answer is found and never the answer.
                foreach (var hint in new[] { index, position, index - 1, index + 1, 0, -1, size, stored.Length, listed.Length, int.MaxValue, int.MinValue })
                {
                    Assert.Equal(index, health.IndexOf(entity, hint));
                    Assert.Equal(alive, world.Entities.IsAlive(entity, hint));
                }
            }
        }
    }

    /// <summary>
    /// A hint is where a value was. What was added or removed since may have moved the value, or
    /// taken it away: the lookup then searches, and answers as it does without a hint.
    /// </summary>
    [Fact]
    public void AHintThatIsOutOfDateIsSafe()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        var a = world.Entities.Create();
        var b = world.Entities.Create();
        var c = world.Entities.Create();
        health.Add(b, new Health { Points = 20 });
        health.Add(c, new Health { Points = 30 });
        var (hintOfB, hintOfC) = (health.IndexOf(b), health.IndexOf(c));
        var (listedB, listedC) = (world.Entities.PositionOf(b), world.Entities.PositionOf(c));
        Assert.Equal((0, 1, 1, 2), (hintOfB, hintOfC, listedB, listedC));

        // A value added before them moves both up.
        health.Add(a, new Health { Points = 10 });
        Assert.Equal(1, health.IndexOf(b, hintOfB));
        Assert.Equal(2, health.IndexOf(c, hintOfC));
        Assert.Equal(30, health.ValueAt(health.IndexOf(c, hintOfC)).Points);

        // One removed moves the later one down, and where it was there is now another entity or nothing.
        health.Remove(b);
        Assert.Equal(-1, health.IndexOf(b, 1));
        Assert.Equal(1, health.IndexOf(c, 2));

        // Destroyed: not alive at once, at the same place in the list until the flush, and then gone from it.
        world.Entities.Destroy(b);
        Assert.False(world.Entities.IsAlive(b, listedB));
        Assert.True(world.Entities.IsAlive(c, listedC));
        world.FlushDestroyedEntities();
        Assert.False(world.Entities.IsAlive(b, listedB));
        Assert.True(world.Entities.IsAlive(c, listedC));
        Assert.Equal(1, world.Entities.PositionOf(c));
        Assert.Equal(-1, world.Entities.PositionOf(b));

        // The place b had in the list is c's now: a hint is never taken for the entity it names.
        Assert.True(world.Entities.IsAlive(c, listedB));
        world.Entities.Destroy(c);
        Assert.False(world.Entities.IsAlive(c, listedB));
        Assert.True(world.Entities.IsAlive(a, listedB));
    }

    [Fact]
    public void ValuesAreReadAndChangedInPlace()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        var a = world.Entities.Create();
        var b = world.Entities.Create();
        health.Add(a, new Health { Points = 10 });

        health.Get(a).Points -= 3;
        health.ValueAt(0).Points -= 1;

        Assert.True(health.TryGet(a, out var value));
        Assert.Equal(6, value.Points);
        Assert.False(health.TryGet(b, out var missing));
        Assert.Equal(0, missing.Points);
        Assert.True(health.Contains(a));
        Assert.False(health.Contains(b));
        Assert.Equal(-1, health.IndexOf(b));
        Assert.Throws<KeyNotFoundException>(() => health.Get(b));
        Assert.Throws<ArgumentException>(() => health.Add(a, default));
        Assert.Throws<ArgumentException>(() => health.Add(EntityId.None, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.ValueAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.EntityAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Entities[2]);

        Assert.True(health.Remove(a));
        Assert.False(health.Remove(a));
        Assert.Equal(0, health.Count);
        Assert.True(world.Entities.IsAlive(a));
    }

    [Fact]
    public void ASystemCanDestroyEntitiesWhileItWalksAStore()
    {
        var world = new World(0);
        var health = world.AddStore(new ComponentStore<Health>("health"));
        for (var i = 0; i < 10; i++)
        {
            health.Add(world.Entities.Create(), new Health { Points = i });
        }

        var visited = 0;
        for (var i = 0; i < health.Count; i++)
        {
            visited++;
            if (health.ValueAt(i).Points % 2 == 0)
            {
                world.Entities.Destroy(health.EntityAt(i));
            }
        }

        world.FlushDestroyedEntities();

        Assert.Equal(10, visited);
        Assert.Equal([1, 3, 5, 7, 9], Enumerable.Range(0, health.Count).Select(index => health.ValueAt(index).Points));
    }

    [Fact]
    public void StoresAreKeptInNameOrderAndNamesAreUnique()
    {
        var world = new World(0);
        world.AddStore(new ComponentStore<Health>("movement"));
        world.AddStore(new ComponentStore<Health>("combat"));
        world.AddStore(new ComponentStore<Health>("economy"));
        world.AddStore(new ComponentStore<Health>("Zebra"));

        // Ordinal order: capital letters sort before lower-case ones, under every culture.
        Assert.Equal(["Zebra", "combat", "economy", "movement"], world.Stores.Select(store => store.Name));
        Assert.Throws<ArgumentException>(() => world.AddStore(new ComponentStore<Health>("combat")));
        Assert.Throws<ArgumentNullException>(() => world.AddStore<ComponentStore<Health>>(null!));
        Assert.Throws<ArgumentException>(() => new ComponentStore<Health>(string.Empty));
    }
}
