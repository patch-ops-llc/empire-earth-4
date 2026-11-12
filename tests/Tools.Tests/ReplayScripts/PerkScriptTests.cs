using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>perks</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach of Regeneration, Reserves and Charge
/// (design 2.3).
/// </summary>
public class PerkScriptTests
{
    private const string GoldenReplay = "ridge-perks.replay";

    private const string SecondGoldenReplay = "ridge-perks-in-fog.replay";

    private static readonly string[] EveryRule =
    [
        "a charged melee attack",
        "a charged shot",
        "a charge by a unit that stood before its owner had Charge",
        "a plain attack by a unit that carries Charge",
        "an attack by a unit without Charge",
        "a regeneration",
        "a regeneration that stops at the maximum",
        "a regeneration of a unit that was trained",
        "a regeneration of a unit that carries Charge",
        "a refund for a unit that died with Reserves",
        "a refund that is rounded",
        "a refund for a unit trained before its owner had Reserves",
        "a trained unit that died without Reserves",
        "a unit nobody paid for that died",
    ];

    /// <summary>What the second recorded match no longer reaches under the rules of today: the first has it.</summary>
    private static readonly string[] OnlyTheFirstReaches =
    [
        "a charged melee attack",
    ];

    private static readonly string[] ItsCommands =
    [
        nameof(CommandType.AttackMove),
        nameof(CommandType.Build),
        nameof(CommandType.DebugSpawn),
        nameof(CommandType.Gather),
        nameof(CommandType.Move),
        nameof(CommandType.SetRallyPoint),
        nameof(CommandType.StartResearch),
        nameof(CommandType.Train),
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to, on each map, and plays each player
    /// to the perks of its part. The attack-moves come last, when a raider's units carry Charge,
    /// which takes two researches that have to be earned.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrdersAndReachesThePerks(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "7000", "--orders", "34", "--script", "perks");

        Assert.Equal(ItsCommands, replay.Commands.Select(entry => entry.Command.Type.ToString()).Distinct().Order(StringComparer.Ordinal));
        var match = ReplayRuns.Start(replay);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            match.Simulation.Tick();
        }

        // Each player has what its part is for: the raider Charge and Regeneration, the defender Regeneration and Reserves.
        var roles = match.Data.TrackRoles;
        Assert.Equal((3, 2), (match.Research.State.Tier(0, roles.Mobility), match.Research.State.Tier(0, roles.Logistics)));
        Assert.Equal((3, 2), (match.Research.State.Tier(1, roles.Manufacturing), match.Research.State.Tier(1, roles.Logistics)));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach each of the three perks in a match that
    /// was played to them, by researches that were paid for and with units that were trained,
    /// and the cases beside the plain one that a change could break. It reaches every rule of
    /// the list now that a finished research brings the units on the map up to date (live
    /// refresh); before that, played by the rules of the fog, it did not reach two of them.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesEveryRuleItGuards()
    {
        AssertReached(EveryRule, WhatItReaches(ReplayRuns.ReadGolden(GoldenReplay)));
    }

    /// <summary>
    /// The second recorded match was recorded when units pick only what their owner sees and
    /// attack the structures of an enemy, and before a finished research brought the units on
    /// the map up to date. Played with that, it reaches every rule of the list but one, which
    /// the first has.
    /// </summary>
    [Fact]
    public void ItsSecondGoldenReplayReachesTheRulesItGuards()
    {
        AssertReached(EveryRule.Except(OnlyTheFirstReaches), WhatItReaches(ReplayRuns.ReadGolden(SecondGoldenReplay)));
    }

    private static void AssertReached(IEnumerable<string> expected, HashSet<string> reached) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));

    /// <summary>Plays a replay through and lists what it reaches, in the words of <see cref="EveryRule"/>.</summary>
    private static HashSet<string> WhatItReaches(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var paid = new Dictionary<EntityId, (Fraction Paid, bool WithReserves, bool WithCharge, int Chassis, int Owner)>();
        var stoodWithoutCharge = new HashSet<EntityId>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            // What each unit was before the tick, for those that the tick takes away.
            paid.Clear();
            for (var i = 0; i < match.PerkCounts.Count; i++)
            {
                var unit = match.PerkCounts.EntityAt(i);
                var owner = match.Bodies.Get(unit).Owner;
                var chassis = match.Combatants.Get(unit).Chassis;
                var perks = match.Research.Tech(owner).Blueprints[chassis].Perks;
                paid[unit] = (match.PerkCounts.ValueAt(i).Paid, perks.DeathRefund is not null, perks.Charge is not null, chassis, owner);
                if (perks.Charge is null)
                {
                    stoodWithoutCharge.Add(unit);
                }
            }

            match.Simulation.Tick();
            seen.UnionWith(WhatTheTickDid(match, paid, stoodWithoutCharge));
        }

        return seen;
    }

    /// <summary>What the tick that has just run did, from its events, in the words of the list above.</summary>
    private static HashSet<string> WhatTheTickDid(
        Match match,
        Dictionary<EntityId, (Fraction Paid, bool WithReserves, bool WithCharge, int Chassis, int Owner)> before,
        HashSet<EntityId> stoodWithoutCharge)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var charged = new HashSet<EntityId>();
        var refunded = new HashSet<EntityId>();
        foreach (var reported in match.Simulation.Events)
        {
            switch (reported.Kind)
            {
                case SimEventKind.ChargedAttack:
                    charged.Add(reported.Entity);
                    if (stoodWithoutCharge.Contains(reported.Entity))
                    {
                        seen.Add("a charge by a unit that stood before its owner had Charge");
                    }

                    break;
                case SimEventKind.MeleeHit:
                    seen.Add(charged.Contains(reported.Other) ? "a charged melee attack" : PlainAttack(before, reported.Other));
                    break;
                case SimEventKind.ShotFired:
                    seen.Add(charged.Contains(reported.Other) ? "a charged shot" : PlainAttack(before, reported.Other));
                    break;
                case SimEventKind.Regenerated:
                    seen.Add("a regeneration");
                    var health = match.Health.Get(reported.Entity);
                    var blueprint = match.Research.Tech(reported.Player).Blueprints[match.Combatants.Get(reported.Entity).Chassis];
                    if (health.HitPoints == health.Maximum && reported.Amount < (blueprint.Perks.Regeneration!.SharePerSecond.ToFraction() * health.Maximum).RoundHalfUp())
                    {
                        seen.Add("a regeneration that stops at the maximum");
                    }

                    if (match.PerkCounts.Get(reported.Entity).Paid > Fraction.Zero)
                    {
                        seen.Add("a regeneration of a unit that was trained");
                    }

                    if (blueprint.Perks.Charge is not null)
                    {
                        seen.Add("a regeneration of a unit that carries Charge");
                    }

                    break;
                case SimEventKind.ReservesRefunded:
                    seen.Add("a refund for a unit that died with Reserves");
                    refunded.Add(reported.Entity);
                    var unit = before[reported.Entity];
                    Assert.True(unit.WithReserves);
                    var share = match.Research.Tech(unit.Owner).Blueprints[unit.Chassis].Perks.DeathRefund!.Share.ToFraction() * unit.Paid;
                    Assert.Equal(share.RoundHalfUp(), reported.Amount);
                    if (share != reported.Amount)
                    {
                        seen.Add("a refund that is rounded");
                    }

                    // Today's price is not the price it was built at: the refund is from what was stored.
                    if (unit.Paid != match.Production.PriceOf(unit.Owner, unit.Chassis).Materials)
                    {
                        seen.Add("a refund for a unit trained before its owner had Reserves");
                    }

                    break;
                default:
                    break;
            }
        }

        foreach (var reported in match.Simulation.Events)
        {
            if (reported.Kind == SimEventKind.Death && before.TryGetValue(reported.Entity, out var unit) && !refunded.Contains(reported.Entity))
            {
                seen.Add(unit.Paid > Fraction.Zero ? "a trained unit that died without Reserves" : "a unit nobody paid for that died");
                Assert.False(unit.WithReserves && unit.Paid >= 2);
            }
        }

        return seen;
    }

    private static string PlainAttack(
        Dictionary<EntityId, (Fraction Paid, bool WithReserves, bool WithCharge, int Chassis, int Owner)> before,
        EntityId attacker)
    {
        return before[attacker].WithCharge ? "a plain attack by a unit that carries Charge" : "an attack by a unit without Charge";
    }
}
