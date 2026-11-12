using Sim.Core;
using Sim.Data;
using Sim.Production;
using Sim.Tech;

namespace Ai;

/// <summary>
/// The army (design 5.5, item 3): of each line of its personality it trains the newest chassis
/// it has unlocked, and it keeps the lines in the personality's ratio by always ordering the
/// line that is furthest behind its share.
/// </summary>
/// <remarks>
/// "Then adjust for what it has scouted": a row of the response table that is in force can raise
/// a line to a share of the army's supply, whether the personality trains the line or not. While
/// the line is below that share it is ordered before the ratio is asked, and the ratio shares
/// out the rest.
/// </remarks>
internal sealed class AiArmy
{
    /// <summary>The most orders it places in one round of thinking.</summary>
    public const int OrdersPerRound = 4;

    private readonly AiSight _sight;
    private readonly AiPersonalityDef _personality;
    private readonly ChassisBlueprint?[] _blueprints;
    private readonly int[] _unitsByLine;
    private readonly List<(EntityId Structure, int Type, int Orders)> _producers = [];
    private readonly EntityId[] _one = new EntityId[1];
    private readonly AiResponses? _responses;
    private readonly int[] _supplyOfLine;
    private long _armySupply;
    private int _supplyRoom;
    private int _supplyFree;

    public AiArmy(AiSight sight, AiPersonalityDef personality, AiResponses? responses = null)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(personality);
        _sight = sight;
        _personality = personality;
        _responses = responses;
        _blueprints = new ChassisBlueprint?[personality.Lines.Count];
        _unitsByLine = new int[sight.Data.Rules.Lines.Count];
        _supplyOfLine = new int[_unitsByLine.Length];
    }

    /// <summary>What came of trying to order a unit.</summary>
    private enum Placement
    {
        /// <summary>Nothing was ordered, and a unit of another line may be.</summary>
        Passed,

        /// <summary>Nothing was ordered, and nothing else is: the stockpile does not cover the unit yet.</summary>
        Waits,

        Ordered,
    }

    /// <summary>
    /// The least supply a unit of its army takes, of the chassis it would train now, or 0 when it
    /// has unlocked none. Set by <see cref="Think"/>.
    /// </summary>
    public int SmallestUnitSupply { get; private set; }

    /// <summary>
    /// True when its army is as large as its supply lets it be, as the last round of thinking
    /// found it: it can train a unit, and the supply its army may take has room for none. Supply
    /// used above the cap, as after a displaced Light footprint (design 2.3), is at the cap too.
    /// </summary>
    public bool IsAtSupplyCap { get; private set; }

    /// <summary>
    /// The newest chassis of a line that the player has unlocked, or null: the one of the highest
    /// epoch.
    /// </summary>
    public static ChassisBlueprint? NewestUnlocked(PlayerTech tech, int line)
    {
        ArgumentNullException.ThrowIfNull(tech);

        ChassisBlueprint? newest = null;
        foreach (var blueprint in tech.Blueprints)
        {
            if (blueprint.Unlocked && blueprint.Line.Index == line && (newest is null || blueprint.Chassis.Epoch > newest.Chassis.Epoch))
            {
                newest = blueprint;
            }
        }

        return newest;
    }

    /// <summary>
    /// The position in the personality's lines of the line that is furthest behind its share: the
    /// one with the fewest units for each part of its share. Lines that are level go in the
    /// order of the file. Lines for which <paramref name="skip"/> is set are passed over; -1
    /// when every line is.
    /// </summary>
    public static int FurthestBehind(IReadOnlyList<AiLineShare> lines, ReadOnlySpan<int> unitsByLine, ReadOnlySpan<bool> skip)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var best = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (skip[i])
            {
                continue;
            }

            // units / share, compared without dividing.
            if (best < 0
                || (long)unitsByLine[lines[i].Line] * lines[best].Share < (long)unitsByLine[lines[best].Line] * lines[i].Share)
            {
                best = i;
            }
        }

        return best;
    }

    /// <param name="supplyRoom">The supply its army may still take: what is free, less what it keeps for workers.</param>
    /// <param name="budget">The round's budget.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void Think(int supplyRoom, AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        var lines = _personality.Lines;
        var tech = _sight.Tech;
        SmallestUnitSupply = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var blueprint = NewestUnlocked(tech, lines[i].Line);
            _blueprints[i] = blueprint;
            if (blueprint is not null)
            {
                // One round of the ratio is what the workers earn for.
                budget.Want(lines[i].Share * blueprint.Materials, lines[i].Share * blueprint.Energy);
                SmallestUnitSupply = SmallestUnitSupply == 0 ? blueprint.Supply : Math.Min(SmallestUnitSupply, blueprint.Supply);
            }
        }

        // And one unit of each line a row of the response table raises.
        var raised = _responses?.ArmySupply ?? [];
        foreach (var share in raised)
        {
            if (NewestUnlocked(tech, share.Line) is { } blueprint)
            {
                budget.Want(blueprint.Materials, blueprint.Energy);
                SmallestUnitSupply = SmallestUnitSupply == 0 ? blueprint.Supply : Math.Min(SmallestUnitSupply, blueprint.Supply);
            }
        }

        IsAtSupplyCap = SmallestUnitSupply > 0 && supplyRoom < SmallestUnitSupply;

        // A research that ends before these orders are carried out may raise a price, and the
        // order would be refused. It orders in the next round, at the price that then holds.
        if (_sight.ResearchEndsWithin(AiPlayer.ThinkInterval))
        {
            return;
        }

        Array.Clear(_unitsByLine);
        _sight.CountUnitsByLine(_unitsByLine);

        // The supply of its army by line, each unit at what the newest chassis of its line takes.
        _armySupply = 0;
        for (var line = 0; line < _unitsByLine.Length; line++)
        {
            _supplyOfLine[line] = NewestUnlocked(tech, line)?.Supply ?? 1;
            _armySupply += (long)_unitsByLine[line] * _supplyOfLine[line];
        }

        _producers.Clear();
        foreach (var structure in _sight.Structures)
        {
            var orders = structure.IsFinished ? _sight.OrdersAt(structure.Entity) : -1;
            if (orders >= 0)
            {
                _producers.Add((structure.Entity, structure.Type, orders));
            }
        }

        _supplyRoom = supplyRoom;
        _supplyFree = _sight.SupplyFree;
        Span<bool> skip = stackalloc bool[lines.Count];
        for (var placed = 0; placed < OrdersPerRound; placed++)
        {
            var result = Placement.Passed;

            // A line that is below the share a row raises it to goes before the ratio.
            foreach (var share in raised)
            {
                if (result == Placement.Passed
                    && share.IsBelow((long)_unitsByLine[share.Line] * _supplyOfLine[share.Line], _armySupply)
                    && NewestUnlocked(tech, share.Line) is { } blueprint)
                {
                    result = TryOrder(blueprint, budget, commands);
                }
            }

            skip.Clear();
            while (result == Placement.Passed && FurthestBehind(lines, _unitsByLine, skip) is var index && index >= 0)
            {
                skip[index] = true;
                if (_blueprints[index] is { } blueprint)
                {
                    result = TryOrder(blueprint, budget, commands);
                }
            }

            if (result != Placement.Ordered)
            {
                return;
            }
        }
    }

    /// <summary>Orders one unit of a chassis, or a pair, at the structure of its line with the shortest queue.</summary>
    private Placement TryOrder(ChassisBlueprint blueprint, AiBudget budget, List<Command> commands)
    {
        var producer = FindProducer(blueprint.Line.BuiltAt);
        if (producer < 0)
        {
            return Placement.Passed;
        }

        // The simulation makes the order a pair when the stockpile and the free supply cover two.
        var count = blueprint.BatchSize >= TrainOrder.MaxCount
            && budget.StockCovers(TrainOrder.MaxCount * blueprint.Materials, TrainOrder.MaxCount * blueprint.Energy)
            && _supplyFree >= TrainOrder.MaxCount * blueprint.Supply
                ? TrainOrder.MaxCount
                : 1;
        if (count * blueprint.Supply > _supplyRoom)
        {
            // A unit of another line may still fit.
            return Placement.Passed;
        }

        if (!budget.Covers(count * blueprint.Materials, count * blueprint.Energy))
        {
            // The line that is behind is waited for: buying another in its place would leave it behind for good.
            return Placement.Waits;
        }

        budget.Pay(count * blueprint.Materials, count * blueprint.Energy);
        _one[0] = _producers[producer].Structure;
        commands.Add(new Command(CommandType.Train, _sight.Player, _one, contentId: blueprint.Chassis.Index));
        _producers[producer] = _producers[producer] with { Orders = _producers[producer].Orders + 1 };
        var line = blueprint.Line.Index;
        _unitsByLine[line] += count;
        _armySupply += (long)count * _supplyOfLine[line];
        _supplyRoom -= count * blueprint.Supply;
        _supplyFree -= count * blueprint.Supply;
        return Placement.Ordered;
    }

    /// <summary>
    /// The finished structure of a type with the shortest queue, as a position in the round's
    /// list, or -1 when there is none or every queue is as long as it keeps them.
    /// </summary>
    private int FindProducer(int type)
    {
        var best = -1;
        for (var i = 0; i < _producers.Count; i++)
        {
            if (_producers[i].Type == type
                && _producers[i].Orders < AiEconomy.QueueLength
                && (best < 0 || _producers[i].Orders < _producers[best].Orders))
            {
                best = i;
            }
        }

        return best;
    }
}
