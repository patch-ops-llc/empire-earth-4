using Sim.Core;
using Sim.Map;

namespace Sim.Production;

/// <summary>
/// Design 2.5: "when a structure is destroyed ... every order in its production queue is refunded
/// in full and its supply released". Gives back the orders of every production structure that was
/// destroyed in this tick, each reported as a <see cref="SimEventKind.TrainRefunded"/>.
/// </summary>
/// <remarks>
/// It runs after the deaths and after the structures have left the map, and before income and
/// research. A destroyed structure is still in its stores until the tick ends, which is how it is
/// found here, exactly once, with nothing remembered from one tick to the next. The supply needs
/// nothing done: <see cref="MatchProduction.SupplyUsedBy"/> counts the structures that stand.
/// </remarks>
public sealed class ProductionRefundSystem : ISystem
{
    private readonly MatchProduction _production;
    private readonly ComponentStore<Body> _bodies;

    public ProductionRefundSystem(MatchProduction production, ComponentStore<Body> bodies)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(bodies);
        _production = production;
        _bodies = bodies;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var producers = _production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            var structure = producers.EntityAt(i);
            if (!world.Entities.IsAlive(structure) && _bodies.TryGet(structure, out var body))
            {
                _production.RefundAll(body.Owner, structure, ref producers.ValueAt(i));
            }
        }
    }
}
