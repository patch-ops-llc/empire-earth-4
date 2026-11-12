using Sim.Core;
using Sim.Production;
using Sim.Setup;

namespace Game;

/// <summary>
/// One thing a structure trains, as the production panel offers it now: what an order placed in
/// this tick would make and cost, and whether it can be placed.
/// </summary>
/// <param name="Unit">A chassis by its index, or <see cref="TrainOrder.Worker"/>.</param>
/// <param name="Count">The units one order would make: 2 for a pair, otherwise 1.</param>
/// <param name="UnitMaterials">The Materials of one unit.</param>
/// <param name="UnitEnergy">The Energy of one unit.</param>
/// <param name="UnitSupply">The supply of one unit.</param>
/// <param name="Check">Whether the order can be placed, and if not, the simulation's reason.</param>
public readonly record struct ProductionOffer(int Unit, int Count, int UnitMaterials, int UnitEnergy, int UnitSupply, TrainCheck Check)
{
    /// <summary>The Materials the whole order would cost.</summary>
    public int Materials => Count * UnitMaterials;

    /// <summary>The Energy the whole order would cost.</summary>
    public int Energy => Count * UnitEnergy;

    /// <summary>The supply the whole order would hold.</summary>
    public int Supply => Count * UnitSupply;

    public bool Allowed => Check == TrainCheck.Allowed;

    /// <summary>
    /// The structure whose production the panel shows for a selection: the one selected entity,
    /// when it is a finished structure of the player's that trains. None for anything else.
    /// </summary>
    public static EntityId ProducerIn(MatchView view)
    {
        var selection = view.Selection;
        if (selection.Count != 1)
        {
            return EntityId.None;
        }

        var entity = selection.Entities.First();
        return IsProducerOf(view.Match, view.Player, entity) ? entity : EntityId.None;
    }

    /// <summary>True when the entity is a living, finished structure of the player's that trains.</summary>
    public static bool IsProducerOf(Match match, int player, EntityId entity) =>
        match.World.Entities.IsAlive(entity)
        && match.Production.Producers.Contains(entity)
        && match.Bodies.TryGet(entity, out var body)
        && body.Owner == player;

    /// <summary>
    /// Fills a list with what a structure offers the player now, in the order the simulation
    /// lists it: the worker first, then each line's chassis from the oldest epoch on.
    /// </summary>
    public static void Read(Match match, int player, EntityId structure, List<ProductionOffer> offers)
    {
        offers.Clear();
        if (!match.Structures.TryGet(structure, out var placed))
        {
            return;
        }

        var production = match.Production;
        foreach (var unit in production.TrainedAt(placed.Type))
        {
            var (materials, energy, supply) = production.PriceOf(player, unit);
            offers.Add(new ProductionOffer(
                unit,
                production.CountOf(player, unit),
                materials,
                energy,
                supply,
                production.CheckTrain(player, structure, unit)));
        }
    }
}
