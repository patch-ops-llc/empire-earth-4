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
