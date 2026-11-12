using Sim.Core;
using Sim.Movement;
using Sim.Numerics;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The golden replay of the fight guards, on every system, the rule that a unit attacks only in
/// a tick in which it does not walk (design 2.4), and that a step its own side gives it is no
/// walk. So the recorded match must have attacks of both kinds in it.
/// </summary>
public class WalkRuleReplayTests
{
    private const string GoldenReplay = "ridge-fight.replay";

    [Fact]
    public void TheGoldenFightHasAttacksByUnitsThatStoodStillAndByUnitsTheirOwnSideSteppedAside()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var before = new Dictionary<EntityId, (FixedVector2 Position, bool Stood)>();
        var ordered = new HashSet<EntityId>();
        var (standing, stepped) = (0, 0);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            before.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                before[entity] = (match.Bodies.Get(entity).Position, match.Movers.Get(entity).State == MoveState.Idle);
            }

            ordered.Clear();
            foreach (var command in due[tick])
            {
                ordered.UnionWith(command.Entities.ToArray());
            }

            match.Simulation.Tick();
            foreach (var simEvent in match.Simulation.Events)
            {
                if (simEvent.Kind is not (SimEventKind.ShotFired or SimEventKind.MeleeHit)
                    || !before.TryGetValue(simEvent.Other, out var attacker))
                {
                    continue;
                }

                // An attacker is in the stores until the deaths of the tick, which come after the attacks.
                if (!match.Bodies.TryGet(simEvent.Other, out var body) || body.Position == attacker.Position)
                {
                    standing++;
                    continue;
                }

                // It stands elsewhere than before the tick. It stood still then and was given no
                // order in the tick, so it did not walk: its own side stepped it aside.
                Assert.True(
                    attacker.Stood && !ordered.Contains(simEvent.Other),
                    $"{simEvent.Other} attacked in tick {tick}, in which it walked.");
                stepped++;
            }
        }

        Assert.True(standing > 0, "No unit attacked from where it stood.");
        Assert.True(stepped > 0, "No unit attacked in a tick in which its own side stepped it aside.");
    }
}
