using Sim.Core;
using Sim.Outcome;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// Conquest by attack (design 5.1) in a golden replay. No script was written for it and no match
/// recorded: <c>switchback-siege.replay</c>, the recorded match of structures in combat, is
/// decided by conquest since a match can be lost. The units of its second player bring down the
/// only Capitol of the first in tick 850 of 8,000, and what the first player orders in the
/// 7,150 ticks after that is dropped.
/// </summary>
public class ConquestByAttackReplayTests
{
    private const string GoldenReplay = "switchback-siege.replay";
    private const int Loser = 0;
    private const int Winner = 1;
    // 850 on the branch of #65. With every open piece in, the same commands bring the Capitol down three ticks later.
    private const int TickOfTheFall = 853;

    [Fact]
    public void TheCapitolFallsToHitsAndItsOwnerIsOutInThatTick()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var capitol = CapitolOf(match, Loser);
        var left = match.Health.Get(capitol).HitPoints;
        var hits = 0;

        for (var tick = 0; tick < TickOfTheFall; tick++)
        {
            match.Simulation.Tick();
            left -= HitsOn(match, capitol, ref hits);
            Assert.True(left > 0, $"The hits up to tick {tick} leave the Capitol nothing.");
            Assert.True(match.World.Entities.IsAlive(capitol), $"The Capitol is gone after tick {tick}.");
            Assert.False(match.Standings.IsDecided, $"The match is decided after tick {tick}.");
        }

        Assert.Equal(1, HoldsOf(match, Loser));
        match.Simulation.Tick();
        left -= HitsOn(match, capitol, ref hits);

        // Every hit point it had was taken by a hit the simulation reported, the last in this tick.
        Assert.True(left <= 0, $"The hits up to tick {TickOfTheFall} leave the Capitol {left} thousandths of a hit point.");
        Assert.True(hits > 20, $"The Capitol fell to {hits} hits.");
        var about = match.Simulation.Events
            .Where(reported => reported.Entity == capitol && reported.Kind is SimEventKind.Death or SimEventKind.StructureRemoved)
            .ToArray();
        Assert.Equal([SimEventKind.Death, SimEventKind.StructureRemoved], about.Select(reported => reported.Kind));
        Assert.All(about, reported => Assert.Equal((TickOfTheFall, Loser), (reported.Tick, reported.Player)));

        Assert.Equal((PlayerStanding.Eliminated, PlayerStanding.Playing), (match.Standings.Of(Loser), match.Standings.Of(Winner)));
        Assert.Equal((Winner, TickOfTheFall, TickOfTheFall), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(Loser)));
    }

    /// <summary>
    /// The replay holds the orders the first player gave for the rest of the match, as the
    /// script gave them when nobody could lose. None is carried out: nothing of that player
    /// appears on the map again and none of its units takes an order. The winner's orders are
    /// carried out as before, and the standings never change again.
    /// </summary>
    [Fact]
    public void WhatTheLoserOrdersAfterwardsIsDroppedAndTheWinnerPlaysOn()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        match.Simulation.Run(TickOfTheFall + 1);
        var ordersOfTheLoser = 0;
        var placedByTheWinner = 0;
        var madeByTheWinner = 0;

        for (var tick = TickOfTheFall + 1; tick < replay.TickCount; tick++)
        {
            var fromTheLoser = due[tick].Count(command => command.Player == Loser);
            var dropped = match.Simulation.DroppedCommandCount;
            var bodies = BodiesOf(match, Loser);

            match.Simulation.Tick();

            ordersOfTheLoser += fromTheLoser;
            Assert.True(match.Simulation.DroppedCommandCount - dropped >= fromTheLoser, $"Tick {tick} carried out an order of the player who is out.");
            Assert.True(BodiesOf(match, Loser) <= bodies, $"Something of the player who is out appeared in tick {tick}.");
            Assert.DoesNotContain(match.Simulation.Events, reported =>
                reported.Player == Loser && reported.Kind is SimEventKind.StructurePlaced or SimEventKind.OrderRefused);
            placedByTheWinner += match.Simulation.Events.Count(reported => reported.Player == Winner && reported.Kind == SimEventKind.StructurePlaced);
            madeByTheWinner += due[tick].Count(command => command.Player == Winner && command.Type == CommandType.DebugSpawn);
        }

        Assert.True(ordersOfTheLoser > 100, $"The player who is out gave {ordersOfTheLoser} orders after the fall.");
        Assert.True(placedByTheWinner > 5, $"The winner placed {placedByTheWinner} structures after the fall.");
        Assert.True(madeByTheWinner > 5, $"The winner made units {madeByTheWinner} times after the fall.");
        Assert.Equal((PlayerStanding.Eliminated, PlayerStanding.Playing), (match.Standings.Of(Loser), match.Standings.Of(Winner)));
        Assert.Equal((Winner, TickOfTheFall, TickOfTheFall), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(Loser)));
    }

    /// <summary>The damage of the hits the tick that has just run reported on an entity, in thousandths of a hit point.</summary>
    private static long HitsOn(Match match, EntityId entity, ref int hits)
    {
        var damage = 0L;
        foreach (var reported in match.Simulation.Events)
        {
            if (reported.Entity == entity && reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit)
            {
                damage += reported.Amount;
                hits++;
            }
        }

        return damage;
    }

    private static EntityId CapitolOf(Match match, int player)
    {
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.Bodies.Get(entity).Owner == player && match.Structures.ValueAt(i).Type == match.Data.StartStructure.Index)
            {
                return entity;
            }
        }

        throw new Xunit.Sdk.XunitException($"Player {player} has no Capitol.");
    }

    /// <summary>The structures of a player that keep it in the match.</summary>
    private static int HoldsOf(Match match, int player)
    {
        var holds = 0;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.World.Entities.IsAlive(entity)
                && match.Bodies.Get(entity).Owner == player
                && match.Data.StructureOf(match.Structures.ValueAt(i).Type) is { } def
                && ConquestSystem.KeepsInTheMatch(def))
            {
                holds++;
            }
        }

        return holds;
    }

    private static int BodiesOf(Match match, int player)
    {
        var bodies = 0;
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            bodies += match.Bodies.ValueAt(i).Owner == player ? 1 : 0;
        }

        return bodies;
    }
}
