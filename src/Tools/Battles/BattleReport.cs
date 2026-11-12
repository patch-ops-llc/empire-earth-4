using System.Globalization;

namespace Tools.Battles;

/// <summary>One battle fought both ways round, and what is said of it.</summary>
public sealed record BattleReport(BattleDef Battle, BattleResult MassWest, BattleResult MassEast)
{
    /// <summary>A target is met only when it is met both ways (design 3.1).</summary>
    public bool Met => Battle.Target.IsMetBy(MassWest) && Battle.Target.IsMetBy(MassEast);

    public bool SameWinner => MassWest.Winner == MassEast.Winner;

    /// <summary>
    /// The difference between the two fights, in points of army value kept: of the winner when
    /// both have the same winner, and otherwise of the mass army.
    /// </summary>
    public double DifferenceInPoints
    {
        get
        {
            var side = SameWinner && MassWest.Winner is { } winner ? winner : Side.Mass;
            return Math.Abs(MassWest.Of(side).PercentKept - MassEast.Of(side).PercentKept);
        }
    }

    public void Write(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine($"{Battle.Number}. {Battle.Name}");
        output.WriteLine($"   mass:   {Army(Side.Mass)}");
        output.WriteLine($"   elite:  {Army(Side.Elite)}");
        output.WriteLine($"   target: {Battle.Target.Text}. The design expects, with its starting numbers: {Battle.Expected}.");
        WriteFight(output, "mass west", MassWest);
        WriteFight(output, "mass east", MassEast);
        output.WriteLine(Invariant(
            $"   between the two fights: {(SameWinner ? "the same winner" : "a different winner")}, {DifferenceInPoints:0.0} points of army value kept apart"));
        output.WriteLine(Met ? "   target MET" : $"   target MISSED{WhyMissed()}");
    }

    public static void WriteSummary(IReadOnlyList<BattleReport> reports, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(output);

        var met = reports.Where(report => report.Met).Select(report => report.Battle.Number).ToList();
        var missed = reports.Where(report => !report.Met).Select(report => report.Battle.Number).ToList();
        output.WriteLine(
            $"battles: {met.Count} of {reports.Count} targets met"
            + (met.Count > 0 ? $" ({string.Join(", ", met)})" : "")
            + (missed.Count > 0 ? $"; missed: {string.Join(", ", missed)}." : "."));

        var shells = reports.SelectMany(report => new[] { report.MassWest.Shells, report.MassEast.Shells }).Where(count => count.Landed > 0).ToList();
        if (shells.Count > 0 && shells.All(count => count.MostCaught <= 1))
        {
            output.WriteLine(
                "battles: no shell caught more than one unit. A shell of this build hits its target alone: the blast of design 2.4 "
                + "is not in it, so a battle with Artillery is not yet the design's test.");
        }
    }

    private string Army(Side side)
    {
        // Both fights start with the same armies, so the first speaks for both.
        var army = Battle.Army(side);
        var result = MassWest.Of(side);
        var units = string.Join(" and ", army.Parts.Select(part => $"{part.Count} {part.Chassis}"));
        return Invariant($"{units}, price {result.Price:N0}, supply {result.Supply} of {result.SupplyCap}");
    }

    private static void WriteFight(TextWriter output, string name, BattleResult result)
    {
        var outcome = result.Winner is { } winner
            ? Invariant($"{Tools.Battles.Battle.Name(winner)} wins after {result.Seconds:0.0} s and keeps {result.Of(winner).PercentKept:0.0}% of its army value ({result.Of(winner).UnitsKept} of {result.Of(winner).Units} units)")
            : result.TimedOut
                ? Invariant($"no winner after {result.Seconds:0.0} s: mass keeps {result.Mass.PercentKept:0.0}% ({result.Mass.UnitsKept} units), elite {result.Elite.PercentKept:0.0}% ({result.Elite.UnitsKept} units)")
                : Invariant($"no winner: both armies fell in the same tick, after {result.Seconds:0.0} s");
        output.WriteLine($"   {name}: {outcome}");
        output.WriteLine($"              attacks made of those its units had time for: mass {Attacks(result.Mass)}, elite {Attacks(result.Elite)}");
        if (result.Shells.Landed > 0)
        {
            output.WriteLine(Invariant(
                $"              shells: {result.Shells.Landed} landed, {result.Shells.AverageCaught:0.0} units caught on average, {result.Shells.MostCaught} at most"));
        }
    }

    private static string Attacks(ArmyResult army) =>
        Invariant($"{army.Attacks:N0} of {army.AttacksPossible:N0} ({army.PercentOfAttacksMade:0}%, and {army.PercentOfOpeningAttacksMade:0}% in the first {Tools.Battles.Battle.OpeningSeconds} s)");

    private string WhyMissed()
    {
        var reasons = new List<string>();
        foreach (var (name, result) in new[] { ("mass west", MassWest), ("mass east", MassEast) })
        {
            if (!Battle.Target.IsMetBy(result))
            {
                reasons.Add(name);
            }
        }

        return reasons.Count == 2 ? " both ways" : $" with {reasons[0]}";
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
