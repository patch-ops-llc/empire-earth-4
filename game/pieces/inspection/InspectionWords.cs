using System.Globalization;
using Sim;
using Sim.Data;
using Sim.Economy;
using Sim.Numerics;
using Sim.Tech;

namespace Game;

/// <summary>
/// How the inspection panel and the epoch announcement say things. Names of tracks, epochs and
/// perks are worded as the research panel words them, in <see cref="ResearchWords"/>, so that a
/// perk reads the same where the player buys it and where the player sees it on a unit.
/// </summary>
public static class InspectionWords
{
    /// <summary>What a unit is called: its chassis, or that it is a worker.</summary>
    public static string Name(UnitFacts facts) =>
        facts.Blueprint is { } blueprint ? ResearchWords.NameOf(blueprint.Chassis.Id) : facts.IsWorker ? "Worker" : "Unit";

    /// <summary>A number of units of one kind: "Hoplite", "3 × Hoplite".</summary>
    public static string Counted(UnitGroup group) =>
        group.Count == 1 ? Name(group.First) : string.Create(CultureInfo.InvariantCulture, $"{group.Count} × {Name(group.First)}");

    /// <summary>The line and epoch of a chassis: "Guard, epoch 1 (Greeks)".</summary>
    public static string LineAndEpoch(GameData data, ChassisBlueprint blueprint)
    {
        var epoch = blueprint.Chassis.Epoch;
        return string.Create(CultureInfo.InvariantCulture, $"{ResearchWords.NameOf(blueprint.Line.Id)}, epoch {epoch} ({ResearchWords.Epoch(data, epoch)})");
    }

    /// <summary>Whose a unit is, for a unit that is not the local player's: "Player 2, red".</summary>
    public static string Owner(int player) => $"{MatchPanel.PlayerName(player)}, {PlayerColors.NameOf(player)}";

    /// <summary>Thousandths as a whole number, rounded half up, as the design shows hit points and damage.</summary>
    public static string Whole(long thousandths) => ((thousandths + 500) / 1000).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Hit points out of their maximum: "117 / 130". A unit that is damaged never reads as whole,
    /// and one that is alive never reads as 0.
    /// </summary>
    public static string Health(long hitPoints, long maximum)
    {
        var whole = (maximum + 500) / 1000;
        var now = (hitPoints + 500) / 1000;
        if (hitPoints < maximum)
        {
            now = Math.Min(now, whole - 1);
        }

        if (hitPoints > 0)
        {
            now = Math.Max(now, 1);
        }

        return string.Create(CultureInfo.InvariantCulture, $"{now} / {whole}");
    }

    /// <summary>A distance in tiles to one decimal, as the design shows distances: "5.8".</summary>
    public static string Tiles(Fixed tiles) => Tenths(tiles.Raw / (double)Fixed.RawOne);

    /// <summary>A speed in tiles a second to one decimal, from tiles a tick.</summary>
    public static string Speed(Fixed perTick) => Tenths(perTick.Raw / (double)Fixed.RawOne * SimInfo.TicksPerSecond);

    /// <summary>A number of ticks as seconds: "1 s", "1.5 s".</summary>
    public static string Seconds(int ticks) =>
        (ticks / (double)SimInfo.TicksPerSecond).ToString("0.##", CultureInfo.InvariantCulture) + " s";

    /// <summary>A price in short: "70 M, 10 E". M is Materials and E is Energy, as in the research panel.</summary>
    public static string Price(int materials, int energy) => energy == 0
        ? string.Create(CultureInfo.InvariantCulture, $"{materials} M")
        : string.Create(CultureInfo.InvariantCulture, $"{materials} M, {energy} E");

    /// <summary>How far a track is ahead of a chassis or behind it: "+2", "−1", and nothing when level with it.</summary>
    public static string Offset(int offset) => offset switch
    {
        > 0 => string.Create(CultureInfo.InvariantCulture, $"+{offset}"),
        < 0 => string.Create(CultureInfo.InvariantCulture, $"−{-offset}"),
        _ => string.Empty,
    };

    /// <summary>How a chassis attacks, with its perks applied: "Melee, a hit every 1 s", "Range 5.8, a shot every 4 s, blast 1.5".</summary>
    public static string Attack(ChassisBlueprint blueprint)
    {
        if (blueprint.DamagePerSecond <= 0)
        {
            return "No attack";
        }

        var every = Seconds(blueprint.Attack.IntervalTicks);
        if (blueprint.Range is not { } range)
        {
            return $"Melee, a hit every {every}";
        }

        var text = $"Range {Tiles(range)}, a shot every {every}";
        return blueprint.BlastRadius is { } blast ? $"{text}, blast {Tiles(blast)}" : text;
    }

    /// <summary>An accent with its level, as a badge names it: "Overgunned II".</summary>
    public static string Accent(GameData data, Accent accent)
    {
        var name = data.AccentOf(accent.Track)?.Name ?? data.Tracks[accent.Track].Name;
        return $"{name} {ResearchWords.Level(accent.Level)}";
    }

    /// <summary>
    /// What an accent does on a unit of this chassis, as the rest of a sentence. A perk that does
    /// nothing on the chassis, as a longer range on one that fights hand to hand, is left out.
    /// </summary>
    public static string AccentPerks(GameData data, ChassisBlueprint blueprint, Accent accent)
    {
        if (data.AccentOf(accent.Track) is not { } definition)
        {
            return string.Empty;
        }

        var all = definition.PerksAt(accent.Level);
        var has = blueprint.Perks;
        var applied = new PerkSet
        {
            RangeBonus = has.RangeBonus is null ? null : all.RangeBonus,
            AttackRateBonus = has.AttackRateBonus is null ? null : all.AttackRateBonus,
            Splash = has.Splash is null ? null : all.Splash,
            BlastRadiusBonus = has.BlastRadiusBonus is null ? null : all.BlastRadiusBonus,
            AreaDamageReduction = has.AreaDamageReduction is null ? null : all.AreaDamageReduction,
            Plating = has.Plating is null ? null : all.Plating,
            SightBonus = has.SightBonus is null ? null : all.SightBonus,
            Charge = has.Charge is null ? null : all.Charge,
            Batch = has.Batch is null ? null : all.Batch,
            DeathRefund = has.DeathRefund is null ? null : all.DeathRefund,
            Regeneration = has.Regeneration is null ? null : all.Regeneration,
            SupplyReduction = has.SupplyReduction is null ? null : all.SupplyReduction,
        };
        return ResearchWords.Perks(applied);
    }

    /// <summary>What a worker is doing, at a deposit or at a Generator.</summary>
    public static string Work(WorkerState state, bool atGenerator = false)
    {
        var place = atGenerator ? "a Generator" : "a deposit";
        return state switch
        {
            WorkerState.Walking => $"Walking to its place at {place}",
            WorkerState.Working => $"Working at {place}",
            WorkerState.Waiting => $"Waiting for a place at {place}",
            _ => "Has no work",
        };
    }

    /// <summary>The headline of an epoch announcement: "Epoch 2: Romans".</summary>
    public static string EpochTitle(GameData data, int epoch) =>
        string.Create(CultureInfo.InvariantCulture, $"Epoch {epoch}: {ResearchWords.Epoch(data, epoch)}");

    /// <summary>
    /// The sentence under the headline of an epoch announcement. For the local player it says
    /// what the new epoch changes (design 1.4): how high a track may now go.
    /// </summary>
    /// <param name="data">The game data.</param>
    /// <param name="player">The player whose epoch rose.</param>
    /// <param name="local">The local player.</param>
    /// <param name="epoch">The epoch the player is in now.</param>
    /// <param name="epochCap">The match's epoch cap.</param>
    public static string EpochSentence(GameData data, int player, int local, int epoch, int epochCap)
    {
        if (player != local)
        {
            return $"{MatchPanel.PlayerName(player)} ({PlayerColors.NameOf(player)}) has reached this epoch.";
        }

        var highest = Math.Min(Math.Min(epoch + data.Rules.Epoch.Reach, epochCap), data.TierCount);
        return highest > epoch
            ? string.Create(CultureInfo.InvariantCulture, $"You have reached a new epoch. Your tracks can now go up to tier {highest}, and tiers up to {epoch} cost half.")
            : string.Create(CultureInfo.InvariantCulture, $"You have reached a new epoch, the last of this match. Tiers up to {epoch} cost half.");
    }

    private static string Tenths(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
