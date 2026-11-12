using Sim.Core;
using Sim.Research;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Serialization;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>The research store as state: in the hash, in a snapshot, and sound after a restore.</summary>
public class ResearchStateTests
{
    private const int LastTick = 1300;
    private const int HolderFallsInTick = 700;
    private const CommandType Fall = (CommandType)60_000;

    /// <summary>The first entity of every match <see cref="Build"/> makes: the holder that falls.</summary>
    private static readonly EntityId Academy = new(1);

    /// <summary>
    /// A run in which every field of the store changes, before and after each snapshot: researches
    /// start in three slots, one is cancelled and its slot used again, a holder falls with a
    /// research in its slot, and three researches finish. Snapshots: before anything, with one
    /// and with four researches under way, right after the cancel, right before and right after
    /// the holder falls, right after the first finish, later, and at the end.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(301)]
    [InlineData(HolderFallsInTick)]
    [InlineData(HolderFallsInTick + 1)]
    [InlineData(900)]
    [InlineData(1000)]
    [InlineData(LastTick)]
    public void AMatchWithResearchRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);

    /// <summary>The run does what the test above says it does, so that the test means something.</summary>
    [Fact]
    public void TheRunStartsCancelsLosesAndFinishesResearch()
    {
        var match = Build();
        var events = match.Events();
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(LastTick);

        Assert.Equal(
            [
                (ResearchEventKind.Started, 0, 0, Firepower),
                (ResearchEventKind.Started, 5, 0, Protection),
                (ResearchEventKind.Started, 10, 1, Manufacturing),
                (ResearchEventKind.Started, 10, 0, Mobility),
                (ResearchEventKind.Cancelled, 300, 1, Manufacturing),
                (ResearchEventKind.Started, 320, 1, Logistics),
                (ResearchEventKind.Lost, HolderFallsInTick, 0, Protection),
                (ResearchEventKind.Finished, 899, 0, Firepower),
                (ResearchEventKind.Finished, 909, 0, Mobility),
                (ResearchEventKind.Finished, 1219, 1, Logistics),
            ],
            events.Select(reported => (reported.Kind, reported.Tick, reported.Player, reported.Track)));
        Assert.Equal([2, 1, 2, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal([1, 1, 1, 1, 2, 1], match.Research.State.Tiers(1).ToArray());
        Assert.Equal((Plenty - 200, Plenty - 200), match.Stock(0));
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(1));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// The table of blueprints is worked out from the tiers and is not in a snapshot. A match
    /// whose simulation alone was restored still has the old table until it is refreshed, and
    /// restoring the match refreshes it.
    /// </summary>
    [Fact]
    public void ARestoredMatchHasTheTableOfTheRestoredTiers()
    {
        var source = Create();
        source.OrderResearch(0, Firepower);
        source.Simulation.Run(TicksToTier2);
        var writer = new ByteWriter();
        Snapshot.Write(source.World, writer);

        var target = Create();
        var stale = target.Research.Tech(0);
        Snapshot.Restore(target.Simulation, new ByteReader(writer.ToArray()));
        Assert.Same(stale, target.Research.Tech(0));
        Assert.Equal(1, target.Research.Tech(0).Tiers[Firepower]);

        target.RefreshDerivedState();
        TechAssert.Same(source.Research.Tech(0), target.Research.Tech(0));
        Assert.Equal(2, target.Research.Tech(0).Tiers[Firepower]);

        var whole = Create();
        var untouched = whole.Research.Tech(1);
        Snapshot.Restore(whole, new ByteReader(writer.ToArray()));
        TechAssert.Same(source.Research.Tech(0), whole.Research.Tech(0));

        // A player whose tiers are the same keeps the table they had: nothing is worked out twice.
        Assert.Same(untouched, whole.Research.Tech(1));
    }

    [Fact]
    public void EveryFieldIsInTheStateHash()
    {
        var hashes = new HashSet<ulong> { Create().World.ComputeHash() };

        var started = Create();
        started.OrderResearch(0, Firepower);
        started.Simulation.Tick();
        var otherTrack = Create();
        otherTrack.OrderResearch(0, Protection);
        otherTrack.Simulation.Tick();
        var otherPlayer = Create();
        otherPlayer.OrderResearch(1, Firepower);
        otherPlayer.Simulation.Tick();
        var ticked = Create();
        ticked.Simulation.Tick();
        Assert.True(hashes.Add(started.World.ComputeHash()));
        Assert.True(hashes.Add(otherTrack.World.ComputeHash()));
        Assert.True(hashes.Add(otherPlayer.World.ComputeHash()));
        Assert.True(hashes.Add(ticked.World.ComputeHash()));

        // The same world with one field of the store set to something else: each is a new hash.
        string[] fields = ["tiers[track]", "slot.IsOpen", "slot.Holder", "slot.Track", "slot.Tier", "slot.TicksLeft", "slot.Ticks", "slot.Materials", "slot.Energy"];
        foreach (var field in fields)
        {
            for (var occurrence = 0; occurrence < 2; occurrence++)
            {
                var world = new World(1);
                var state = world.AddStore(new ResearchState(2, 6, 9, 1));
                var change = new ChangingWalker(field, occurrence, replacement: field == "slot.IsOpen" ? 1 : 3);
                try
                {
                    state.Walk(change);
                }
                catch (InvalidDataException)
                {
                    // The values need not make sense together here; only the hash is looked at.
                }

                Assert.True(change.Changed, field);
                Assert.True(hashes.Add(world.ComputeHash()), $"{field} {occurrence}");
            }
        }

        Assert.Equal("research", new ResearchState(1, 1, 1, 1).Name);
    }

    /// <summary>
    /// A snapshot is not trusted. Values the rules can never give the store are refused when it is
    /// restored, and not found later as a fault in the middle of a tick.
    /// </summary>
    [Theory]
    [InlineData("tiers[track]", 0, 0)]
    [InlineData("tiers[track]", 5, 10)]
    [InlineData("tiers[track]", 3, -1)]
    [InlineData("slot.IsOpen", 0, 0)]
    [InlineData("slot.Holder", 0, 4)]
    [InlineData("slot.Holder", 2, 4)]
    [InlineData("slot.Track", 0, 6)]
    [InlineData("slot.Track", 0, -1)]
    [InlineData("slot.Track", 2, 1)]
    [InlineData("slot.Tier", 0, 3)]
    [InlineData("slot.Tier", 0, 0)]
    [InlineData("slot.Tier", 2, 2)]
    [InlineData("slot.TicksLeft", 0, 0)]
    [InlineData("slot.TicksLeft", 0, 901)]
    [InlineData("slot.TicksLeft", 2, 1)]
    [InlineData("slot.Ticks", 0, 0)]
    [InlineData("slot.Ticks", 2, 1)]
    [InlineData("slot.Materials", 0, -1)]
    [InlineData("slot.Materials", 2, 1)]
    [InlineData("slot.Energy", 0, -1)]
    [InlineData("slot.Energy", 2, 1)]
    public void ARestoreRefusesWhatTheRulesCannotProduce(string field, int occurrence, long value)
    {
        // Player 0 researches Firepower in the first slot and has a free slot with a holder.
        var match = Create(players: 1);
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        var change = new ChangingWalker(field, occurrence, value);

        Assert.Throws<InvalidDataException>(() => match.Research.State.Walk(change));
        Assert.True(change.Changed);
    }

    [Fact]
    public void ARestoreOfTheValuesAStoreHasChangesNothing()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Mobility);
        match.Simulation.Run(3);
        var before = match.World.ComputeHash();

        match.Research.State.Walk(new ChangingWalker("nothing", 0, 0));

        Assert.Equal(before, match.World.ComputeHash());
    }

    [Fact]
    public void TheStoreAnswersForItsPlayersTracksAndSlotsOnly()
    {
        var state = new ResearchState(players: 2, tracks: 6, tierCount: 9, startTier: 3);

        Assert.Equal((2, 6), (state.PlayerCount, state.TrackCount));
        Assert.Equal([3, 3, 3, 3, 3, 3], state.Tiers(1).ToArray());
        Assert.Equal((-1, -1), (state.FreeSlot(0), state.SlotResearching(0, 0)));
        Assert.False(state.IsHolder(EntityId.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Tiers(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Tier(0, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Tier(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Slot(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Slot(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResearchState(0, 6, 9, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResearchState(2, 0, 9, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResearchState(2, 6, 9, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResearchState(2, 6, 9, 10));
    }

    private static Match Build()
    {
        var match = Create();
        Assert.Equal(Academy, match.PlaceHolder(0, 3, 3));
        match.Research.OpenSlot(0, Academy);
        match.Research.OpenSlot(0, match.PlaceHolder(0, 5, 3));
        match.Simulation.SetCommandHandler(Fall, new FallHandler());
        match.RefreshDerivedState();
        return match;
    }

    private static List<(int Tick, Command Command)> Script() =>
    [
        (0, new Command(CommandType.StartResearch, 0, contentId: Firepower)),
        (5, new Command(CommandType.StartResearch, 0, contentId: Protection)),
        (10, new Command(CommandType.StartResearch, 1, contentId: Manufacturing)),
        (10, new Command(CommandType.StartResearch, 0, contentId: Mobility)),
        (300, new Command(CommandType.CancelResearch, 1, contentId: Manufacturing)),
        (320, new Command(CommandType.StartResearch, 1, contentId: Logistics)),
        (HolderFallsInTick, new Command(Fall, 1, [Academy])),
    ];

    /// <summary>
    /// Destroys the entities it names, as combat will: inside a tick and before the research
    /// system runs. It is a command so that the run after a restore has it too.
    /// </summary>
    private sealed class FallHandler : ICommandHandler
    {
        public bool Execute(World world, in Command command)
        {
            foreach (var entity in command.Entities)
            {
                world.Entities.Destroy(entity);
            }

            return true;
        }
    }
}
