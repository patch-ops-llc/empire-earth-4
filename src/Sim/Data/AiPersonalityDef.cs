namespace Sim.Data;

/// <summary>
/// One personality of the computer player (design 5.5), from <c>ai/&lt;id&gt;.jsonc</c>: what it
/// wants of its economy, which tracks it researches first, which lines its army is made of and
/// when that army attacks.
/// The simulation does not read it. The computer player does, in <c>src/Ai/</c>, and it is part
/// of the data so that it is checked, hashed and edited like every other number of the game.
/// </summary>
/// <param name="Id">The personality's ID, which is its file's name.</param>
/// <param name="Name">Its display name.</param>
/// <param name="WorkerTargets">
/// How many workers it keeps, by epoch, in rising order of epoch. The first entry is for epoch 1.
/// </param>
/// <param name="ExpandAtMaterials">
/// The Materials in the stockpile above which it expands to another deposit, in whole Materials.
/// </param>
/// <param name="TrackWeights">
/// One weight per track, by the track's index in <see cref="GameData.Tracks"/>. Of the researches
/// it may start and can pay for, it starts the one whose track has the highest weight.
/// </param>
/// <param name="Lines">The lines its army is made of, in the order of the file, each with its share of the army's units.</param>
/// <param name="Fight">When its army attacks, retreats and raids.</param>
public sealed record AiPersonalityDef(
    string Id,
    string Name,
    IReadOnlyList<AiWorkerTarget> WorkerTargets,
    int ExpandAtMaterials,
    IReadOnlyList<int> TrackWeights,
    IReadOnlyList<AiLineShare> Lines,
    AiFightDef Fight)
{
    /// <summary>The workers it keeps in an epoch: the count of the last entry at or below it.</summary>
    public int WorkersAt(int epoch)
    {
        var workers = WorkerTargets[0].Workers;
        foreach (var target in WorkerTargets)
        {
            if (target.Epoch <= epoch)
            {
                workers = target.Workers;
            }
        }

        return workers;
    }

    /// <summary>The most workers it keeps in any epoch.</summary>
    public int MostWorkers
    {
        get
        {
            var most = 0;
            foreach (var target in WorkerTargets)
            {
                most = Math.Max(most, target.Workers);
            }

            return most;
        }
    }
}

/// <summary>The workers a personality keeps from an epoch on, until a later entry says otherwise.</summary>
public readonly record struct AiWorkerTarget(int Epoch, int Workers);

/// <summary>
/// One line of a personality's army. <see cref="Line"/> is an index into the roster in
/// <see cref="RuleSet.Lines"/>; <see cref="Share"/> is its part of the army's units, as a whole
/// number against the shares of the other lines: 3 and 1 make three units of four.
/// </summary>
public readonly record struct AiLineShare(int Line, int Share);

/// <summary>
/// How a personality fights (design 5.5, item 5). The value of an army is the price of its
/// units, Materials and Energy added.
/// </summary>
/// <param name="AttackRatio">It attacks when its army's value is this many times its estimate of the enemy's. At least 1.</param>
/// <param name="AssumedEnemyValue">
/// The least it takes the enemy's army to be worth. Its estimate is the value of the enemy units
/// it has seen and has not seen die, and never less than this, so that an enemy it has not seen
/// yet is not taken to have no army.
/// </param>
/// <param name="RetreatLosses">It retreats when it has lost this share of the value of the force it attacked with.</param>
/// <param name="ReinforceUnits">The fewest units it sends after a force that is already attacking.</param>
/// <param name="Passage">The kind of map marker its army goes through on the way to the enemy. A raid goes through the other kind.</param>
/// <param name="RaidLine">The line it raids workers and Generators with: an index into <see cref="RuleSet.Lines"/>.</param>
/// <param name="RaidParty">The units of that line it sends on one raid.</param>
/// <param name="ReactionTicks">The ticks a reason to attack, retreat, defend or raid must hold before it acts on it.</param>
public sealed record AiFightDef(
    DataNumber AttackRatio,
    int AssumedEnemyValue,
    DataNumber RetreatLosses,
    int ReinforceUnits,
    MarkerKind Passage,
    int RaidLine,
    int RaidParty,
    int ReactionTicks)
{
    /// <summary>True when an army worth <paramref name="own"/> is enough to attack one worth <paramref name="enemy"/>.</summary>
    public bool IsEnoughToAttack(long own, long enemy) => own * Pow10(AttackRatio.Scale) >= AttackRatio.Unscaled * enemy;

    /// <summary>True when losing <paramref name="lost"/> of a force worth <paramref name="whole"/> is reason to retreat.</summary>
    public bool IsTooMuchLost(long lost, long whole) => whole > 0 && lost * Pow10(RetreatLosses.Scale) >= RetreatLosses.Unscaled * whole;

    private static long Pow10(int scale)
    {
        var value = 1L;
        for (var i = 0; i < scale; i++)
        {
            value *= 10;
        }

        return value;
    }
}
