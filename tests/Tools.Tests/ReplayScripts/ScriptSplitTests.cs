using Sim.Core;
using Sim.Serialization;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The split of <c>ScriptedOrders</c> into a script for each kind of order (plan piece G1) moved
/// the orders and changed none.
/// </summary>
public class ScriptSplitTests
{
    /// <summary>
    /// The three scripts together give, tick for tick, the commands that the one class gave
    /// before the split. The two matches are those of the two golden replays recorded last
    /// before it, with the seeds of their orders: <c>switchback-research.replay</c> and
    /// <c>switchback-workers.replay</c>.
    /// </summary>
    /// <remarks>
    /// The scripts are named here, so that a script added later is not among them: its orders
    /// were never in the one class.
    /// </remarks>
    [Theory]
    [InlineData("1,-,0", "4", "8")]
    [InlineData("0,1", "5", "13")]
    public void TheScriptsTogetherGiveTheOrdersFromBeforeTheSplit(string starts, string seed, string orders)
    {
        var replay = ReplayRuns.Record(
            "--map", "switchback", "--starts", starts, "--seed", seed, "--orders", orders, "--ticks", "4800", "--script", "gather,movement,research");

        var match = replay.Description.CreateMatch(ReplayRuns.GoldenData);
        var before = new ScriptedOrdersBeforeTheSplit(match, ulong.Parse(orders, System.Globalization.CultureInfo.InvariantCulture));
        var recorded = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var given = new List<Command>();
        var types = new HashSet<CommandType>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            given.Clear();
            before.AddOrdersFor(tick, given);
            var now = recorded[tick].Select(Bytes).ToList();
            Assert.True(
                given.Select(Bytes).SequenceEqual(now),
                $"The orders for tick {tick} differ. Before the split: [{string.Join(", ", given.Select(Bytes))}]. Now: [{string.Join(", ", now)}].");
            foreach (var command in given)
            {
                match.Simulation.Commands.Enqueue(tick, command);
                types.Add(command.Type);
            }

            match.Simulation.Tick();
        }

        Assert.Equal(4800, replay.TickCount);
        Assert.True(replay.Commands.Count > 100, "The match is too short to show anything.");
        Assert.Superset(new HashSet<CommandType> { CommandType.Move, CommandType.Gather, CommandType.StartResearch, CommandType.CancelResearch }, types);
    }

    private static string Bytes(Command command)
    {
        var writer = new ByteWriter();
        CommandEncoding.Write(writer, command);
        return Convert.ToHexString(writer.WrittenSpan);
    }
}
