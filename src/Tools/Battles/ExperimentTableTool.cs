using System.Globalization;
using Sim;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;

namespace Tools.Battles;

/// <summary>
/// EXPERIMENT, not for merging. The acceptance battles as rows of one Markdown table, for
/// comparing variants of the simulation, with one measure the report of <c>battles</c> does not
/// have: how much of an army stands crowded.
/// </summary>
public sealed class ExperimentTableTool : ITool
{
    public string Name => "battle-table";

    public string Summary => "EXPERIMENT: the acceptance battles as rows of a Markdown table. Arguments: [--label <text>] [--data <folder>].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        var label = "";
        var folder = BattlesTool.DefaultFolder;
        for (var i = 0; i + 1 < arguments.Count; i += 2)
        {
            if (arguments[i] == "--label")
            {
                label = arguments[i + 1];
            }
            else if (arguments[i] == "--data")
            {
                folder = arguments[i + 1];
            }
        }

        var data = DataLoader.Load(new DiskDataFiles(folder)).Data!;
        foreach (var battle in AcceptanceBattles.All)
        {
            var rows = new List<Row>();
            foreach (var massWest in new[] { true, false })
            {
                rows.Add(Fight(data, battle, massWest));
            }

            var met = rows.All(row => battle.Target.IsMetBy(row.Result));
            foreach (var row in rows)
            {
                var result = row.Result;
                var winner = result.Winner is { } side ? result.Of(side) : null;
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {label} | {battle.Number} | {(result.MassWest ? "mass west" : "mass east")} | {(result.Winner is { } won ? Battle.Name(won) : "none")} | {winner?.PercentKept ?? 0:0.0}% | {result.Seconds:0.0} s | {result.Mass.PercentOfOpeningAttacksMade:0}% | {result.Elite.PercentOfOpeningAttacksMade:0}% | {result.Mass.PercentOfAttacksMade:0}% | {result.Elite.PercentOfAttacksMade:0}% | {(result.Shells.Landed == 0 ? "" : $"{result.Shells.AverageCaught:0.0} / {result.Shells.MostCaught}")} | {row.MassCrowded:0}% | {(met ? "met" : "missed")} |"));
            }
        }

        return ExitCodes.Ok;
    }

    private static Row Fight(GameData data, BattleDef battle, bool massWest)
    {
        var fight = Battle.Start(data, battle, massWest);
        var match = fight.Match;
        var shells = new ShellCounter();
        var living = new[] { fight.Units(Side.Mass).Length, fight.Units(Side.Elite).Length };
        var (fighting, samples, crowded) = (0, 0, 0.0);
        while (match.World.Tick < Battle.DefaultMostTicks && living[0] > 0 && living[1] > 0)
        {
            match.Simulation.Tick();
            shells.AfterTick(match);
            fight.CountAttacks();
            living[0] = fight.Living(Side.Mass);
            living[1] = fight.Living(Side.Elite);
            if (fighting == 0)
            {
                foreach (var simEvent in match.Simulation.Events)
                {
                    fighting |= simEvent.Kind is SimEventKind.ShotFired or SimEventKind.MeleeHit ? 1 : 0;
                }
            }
            else if (++fighting % SimInfo.TicksPerSecond == 0 && living[0] > 0)
            {
                samples++;
                crowded += Crowded(fight, Side.Mass);
            }
        }

        Side? winner = living[0] > 0 == living[1] > 0 ? null : living[0] > 0 ? Side.Mass : Side.Elite;
        var result = new BattleResult(
            massWest, winner, match.World.Tick, living[0] > 0 && living[1] > 0, fight.Result(Side.Mass), fight.Result(Side.Elite), shells.Count);
        return new Row(result, samples == 0 ? 0 : 100 * crowded / samples);
    }

    /// <summary>The share of an army's living units that stand closer than three quarters of their spacing to another of them.</summary>
    private static double Crowded(Fight fight, Side side)
    {
        var match = fight.Match;
        var positions = new List<(FixedVector2 Position, Fixed Spacing)>();
        foreach (var unit in fight.Units(side))
        {
            if (match.World.Entities.IsAlive(unit))
            {
                positions.Add((match.Bodies.Get(unit).Position, match.Movers.Get(unit).Spacing));
            }
        }

        var crowded = 0;
        for (var i = 0; i < positions.Count; i++)
        {
            var near = positions[i].Spacing * 3 / 4;
            for (var j = 0; j < positions.Count; j++)
            {
                if (i != j && FixedVector2.DistanceSquaredRaw(positions[i].Position, positions[j].Position) < FixedVector2.SquareRaw(near))
                {
                    crowded++;
                    break;
                }
            }
        }

        return positions.Count == 0 ? 0 : (double)crowded / positions.Count;
    }

    private sealed record Row(BattleResult Result, double MassCrowded);
}
