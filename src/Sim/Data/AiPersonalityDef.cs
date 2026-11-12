namespace Sim.Data;

/// <summary>
/// One personality of the computer player (design 5.5), from <c>ai/&lt;id&gt;.jsonc</c>: what it
/// wants of its economy, which tracks it researches first and which lines its army is made of.
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
public sealed record AiPersonalityDef(
    string Id,
    string Name,
    IReadOnlyList<AiWorkerTarget> WorkerTargets,
    int ExpandAtMaterials,
    IReadOnlyList<int> TrackWeights,
    IReadOnlyList<AiLineShare> Lines)
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
