using Sim.Core;
using Sim.Numerics;
using Sim.Research;

namespace Tools.Replay.Scripts;

/// <summary>
/// The development tier: tracks raised by one tier and by several, a track raised while its
/// research is under way, and the commands the simulation must refuse. So that there is something
/// to cancel and something to refresh, the script also starts researches and makes a few fighting
/// units for each player beside the units it has.
/// </summary>
/// <remarks>
/// The command and the units it makes need <c>debug.spawn</c> in the rules of the data; without it
/// every one of them is dropped and only the researches remain.
/// </remarks>
[OrderScript("set-tier")]
internal sealed class TierOrders : OrderScript
{
    private const int LongestPause = 60;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int LargestForce = 4;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D14_D000_0000_0000;

    private readonly DeterministicRandom _random;
    private int _nextTick;

    public TierOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var research = Match.Research;
        var tracks = Match.Data.Tracks.Count;
        var player = _random.NextInt(Match.Players.Count);
        var track = _random.NextInt(tracks);
        var kind = _random.NextInt(100);
        var tier = research.State.Tier(player, track);

        if (kind < 22)
        {
            commands.Add(DebugSetTierHandler.CommandFor(player, track, tier + 1));
        }
        else if (kind < 34)
        {
            // Several tiers at once. Near the epoch cap this asks for more than the cap, and is refused.
            commands.Add(DebugSetTierHandler.CommandFor(player, track, tier + 2 + _random.NextInt(2)));
        }
        else if (kind < 52)
        {
            // Something for the command to cancel. It starts when the player has a slot and the price.
            commands.Add(new Command(CommandType.StartResearch, player, contentId: track));
        }
        else if (kind < 68)
        {
            // The track the player is researching, when there is one: the research is cancelled.
            var slot = BusySlot(player);
            if (slot >= 0)
            {
                track = research.State.Slot(player, slot).Track;
                tier = research.State.Tier(player, track);
            }

            commands.Add(DebugSetTierHandler.CommandFor(player, track, tier + 1 + _random.NextInt(2)));
        }
        else if (kind < 78)
        {
            // A fighting unit where one of the player's units stands, for the tiers to refresh.
            CollectUnitsOf(player);
            if (Own.Count > 0 && FightersAmongOwn() < LargestForce)
            {
                var point = Match.Bodies.Get(Own[_random.NextInt(Own.Count)]).Position;
                commands.Add(new Command(
                    CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: _random.NextInt(Match.Data.Chassis.Count)));
            }
        }
        else if (kind < 84)
        {
            // The tier the track is at, or one below it. A track is only ever raised.
            commands.Add(DebugSetTierHandler.CommandFor(player, track, _random.NextInt(2) == 0 ? tier : _random.NextInt(tier)));
        }
        else if (kind < 89)
        {
            commands.Add(DebugSetTierHandler.CommandFor(player, track, research.EpochCap + 1 + _random.NextInt(Match.Data.TierCount)));
        }
        else if (kind < 93)
        {
            // Between two tiers: a tier is a whole number.
            var between = Fixed.FromInt(tier) + Fixed.FromRaw(1 + _random.NextInt(Fixed.RawOne - 1));
            commands.Add(new Command(CommandType.DebugSetTier, player, targetX: between, contentId: track));
        }
        else if (kind < 96)
        {
            var noTrack = _random.NextInt(2) == 0 ? tracks + _random.NextInt(1000) : -1 - _random.NextInt(1000);
            commands.Add(DebugSetTierHandler.CommandFor(player, noTrack, tier + 1));
        }
        else
        {
            // From nobody in the match, with fields the command does not read.
            var nobody = Match.Players.Count + _random.NextInt(5);
            var point = AnyPoint(_random);
            commands.Add(new Command(CommandType.DebugSetTier, nobody, [new EntityId(1)], Fixed.FromInt(2), point.Y, new EntityId(2), track));
        }
    }

    /// <summary>The lowest slot in which the player has a research under way, or -1.</summary>
    private int BusySlot(int player)
    {
        for (var slot = 0; slot < ResearchState.SlotsPerPlayer; slot++)
        {
            if (Match.Research.State.Slot(player, slot).IsBusy)
            {
                return slot;
            }
        }

        return -1;
    }

    private int FightersAmongOwn()
    {
        var fighters = 0;
        foreach (var unit in Own)
        {
            if (Match.Combatants.Contains(unit))
            {
                fighters++;
            }
        }

        return fighters;
    }
}
