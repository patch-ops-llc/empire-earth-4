using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Replay;

/// <summary>
/// One kind of scripted order for <c>replay record</c>: movement, research, gathering. Each is a
/// class in a file of its own under <c>Scripts/</c>, with an <see cref="OrderScriptAttribute"/>
/// that names it and a constructor that takes a <see cref="ScriptSetup"/>.
/// <see cref="ScriptedOrders"/> finds the classes by reflection, so a piece that adds a kind of
/// order adds a file and edits no file that another piece edits.
/// </summary>
/// <remarks>
/// A script has a clock and a random generator of its own, from <see cref="ScriptSetup.Generator"/>,
/// so that adding it changes nothing another script draws. It reads the match and never changes
/// it: what it wants done it adds as commands.
/// </remarks>
internal abstract class OrderScript
{
    protected OrderScript(ScriptSetup setup) => Match = setup.Match;

    protected Match Match { get; }

    /// <summary>The units of one player, after <see cref="CollectUnitsOf"/>.</summary>
    protected List<EntityId> Own { get; } = [];

    /// <summary>Some of <see cref="Own"/>, after <see cref="PickSome"/>, or whatever else the script puts there.</summary>
    protected List<EntityId> Picked { get; } = [];

    /// <summary>
    /// Adds the script's commands for a tick, which is the next to run, in the order they run.
    /// It is called once for every tick, in order, after the scripts whose names come before its
    /// own.
    /// </summary>
    public abstract void AddOrdersFor(int tick, List<Command> commands);

    /// <summary>Puts the units a player can order into <see cref="Own"/>, in the order of their numbers.</summary>
    protected void CollectUnitsOf(int player)
    {
        Own.Clear();
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (bodies.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(entity) && Match.Movers.Contains(entity))
            {
                Own.Add(entity);
            }
        }
    }

    /// <summary>Puts each unit of <see cref="Own"/> into <see cref="Picked"/> with an even chance, and at least one.</summary>
    protected void PickSome(DeterministicRandom random)
    {
        Picked.Clear();
        foreach (var unit in Own)
        {
            if (random.NextInt(2) == 0)
            {
                Picked.Add(unit);
            }
        }

        if (Picked.Count == 0)
        {
            Picked.Add(Own[random.NextInt(Own.Count)]);
        }
    }

    /// <summary>A point anywhere on the map, on open or on blocked ground alike.</summary>
    protected FixedVector2 AnyPoint(DeterministicRandom random)
    {
        var x = Coordinate(random, Match.Grid.Width);
        var y = Coordinate(random, Match.Grid.Height);
        return new FixedVector2(x, y);
    }

    private static Fixed Coordinate(DeterministicRandom random, int tiles) => Fixed.FromRaw(random.NextInt(tiles * Fixed.RawOne));
}

/// <summary>Names an <see cref="OrderScript"/> for <c>replay record --script</c>.</summary>
/// <param name="name">
/// Lowercase letters, with hyphens between words. In a tick the scripts add their orders in the
/// order of their names, so the name also decides where a script's orders stand among the
/// others of the same tick.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class OrderScriptAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
