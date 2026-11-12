using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Outcome;
using Sim.Setup;
using Sim.Structures;

namespace Net.Tests;

/// <summary>
/// Conquest (design 5.1) by attack, over the session and with the real game data: a player
/// shells the other's Capitol down with orders that travel as any order does, and the match is
/// decided in the same tick on every machine. The guns and the Tower are made with the
/// development spawn and placement, which are orders too.
/// </summary>
public class ConquestOverTheNetworkTests
{
    private static readonly TimeSpan Frame = TestMatch.Frame;
    /// <summary>The 2,000 hit points of a Capitol, in thousandths.</summary>
    private const long CapitolHitPoints = 2_000_000;

    private static readonly LinkConditions Internet = new(TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(30));

    /// <summary>
    /// The machine that hosts and one guest, as the game runs a match. Whichever seat attacks,
    /// its orders reach both machines for the same tick, and both see the Capitol fall, its
    /// owner go out and the other player win in that tick.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheMatchIsDecidedInTheSameTickOnTheHostsMachineAndTheGuests(int attacker)
    {
        var defender = 1 - attacker;
        var network = new LoopbackNetwork(seed: 5);
        var host = new MatchHost(TestData.Data, TestData.Description());
        var link = network.Connect(Internet);
        Assert.True(host.TryAttach(link.HostEnd));
        var guest = new MatchClient(link.ClientEnd, TestData.Data, TestData.GameVersion, readyWhenBuilt: true);
        MatchClient[] machines = [host.Local, guest];
        void Step()
        {
            network.Advance(Frame);
            host.Advance(Frame);
            guest.Advance(Frame);
        }

        Until(Step, () => host.CanStart, "the guest is ready");
        host.Start();
        Until(Step, () => machines.All(machine => machine.Session.Phase == SessionPhase.Running), "the match runs on both machines");
        var reports = machines.Select(Report.Of).ToArray();

        var siege = new Siege(machines[attacker].Match!, attacker, defender, guns: 4);
        Submit(machines[attacker].Session, siege.Making);
        Until(Step, () => machines.All(machine => Siege.GunsOf(machine.Match!, attacker).Length == 4 && siege.TowerIn(machine.Match!) != EntityId.None), "the guns and the Tower are on both machines");
        Submit(machines[attacker].Session, [siege.Attack(machines[attacker].Match!)]);
        Until(Step, () => machines.All(machine => machine.Match!.Standings.IsDecided), "the match is decided on both machines");

        var fall = machines[0].Match!.Standings.DecidedTick;
        Assert.All(machines, machine =>
        {
            var standings = machine.Match!.Standings;
            Assert.Equal((PlayerStanding.Playing, PlayerStanding.Eliminated), (standings.Of(attacker), standings.Of(defender)));
            Assert.Equal((attacker, fall, fall), (standings.Winner, standings.DecidedTick, standings.OutTick(defender)));
            Assert.Equal(SessionPhase.Running, machine.Session.Phase);
        });
        Assert.Equal(SessionPhase.Running, host.Session.Phase);
        Assert.All(reports, report => report.AssertTheFall(siege, fall, defender));

        // The match goes on to be watched, and what the player who is out orders is carried to
        // both machines and dropped on both.
        var dropped = machines.Select(machine => machine.Match!.Simulation.DroppedCommandCount).ToArray();
        var tick = machines[defender].Match!.World.Tick;
        Submit(machines[defender].Session, [new Command(CommandType.Move, defender, Siege.UnitsOf(machines[defender].Match!, defender), Fixed.FromInt(40), Fixed.FromInt(30))]);
        for (var i = 0; i < 60; i++)
        {
            Step();
        }

        Assert.InRange(machines[defender].Match!.World.Tick - tick, 17, 23);
        Assert.Equal(dropped.Select(count => count + 1), machines.Select(machine => machine.Match!.Simulation.DroppedCommandCount));

        // To the last tick the two machines reported the same things in the same order.
        host.End();
        Until(Step, () => guest.Session.Phase == SessionPhase.Stopped, "the guest hears of the end");
        Assert.Equal(host.Local.Match!.World.Tick, guest.Match!.World.Tick);
        Assert.Equal(host.Local.Match.World.ComputeHash(), guest.Match.World.ComputeHash());
        Assert.Equal(reports[0].Events, reports[1].Events);
    }

    /// <summary>
    /// A host and two players who are both elsewhere, each recording its replay. The replays
    /// agree, reproduce, and played again end with the same player out in the same tick.
    /// </summary>
    [Fact]
    public void TheReplaysOfBothPlayersHoldTheConquestAndGiveItAgain()
    {
        var match = new RealMatch(clients: 2, conditions: Internet, options: new HostOptions());
        match.Start();
        var attacker = match.Clients[1];
        var reports = match.Clients.Select(client => Report.Of(client.Player)).ToArray();

        var siege = new Siege(attacker.Match, attacker: 1, defender: 0, guns: 6);
        Submit(attacker.Player.Session, siege.Making);
        match.Until(() => match.Clients.All(client => Siege.GunsOf(client.Match, 1).Length == 6 && siege.TowerIn(client.Match) != EntityId.None), "the guns and the Tower are on both machines");
        Submit(attacker.Player.Session, [siege.Attack(attacker.Match)]);
        match.Until(() => match.Clients.All(client => client.Match.Standings.IsDecided), "the match is decided on both machines");
        var fall = attacker.Match.Standings.DecidedTick;
        match.Run(seconds: 2);
        var finalTick = match.Finish();

        Assert.True(finalTick > fall + 30);
        Assert.Equal(match.Clients[0].Hash, match.Clients[1].Hash);
        Assert.Equal(reports[0].Events, reports[1].Events);
        Assert.All(reports, report => report.AssertTheFall(siege, fall, defender: 0));
        Assert.All(match.Clients, client =>
        {
            Assert.Equal((1, fall), (client.Match.Standings.Winner, client.Match.Standings.OutTick(0)));
            Assert.True(client.Recording!.IsComplete);
            var replay = client.ReadReplay();
            Assert.Equal(finalTick, replay.TickCount);
            Assert.Equal(siege.Making.Length + 1, replay.Commands.Count);

            var again = replay.Description.CreateMatch(TestData.Data);
            var result = replay.Run(again);
            Assert.True(result.Reproduced);
            Assert.Equal(client.Hash, result.FinalHash);
            Assert.Equal((PlayerStanding.Eliminated, PlayerStanding.Playing), (again.Standings.Of(0), again.Standings.Of(1)));
            Assert.Equal((1, fall, fall), (again.Standings.Winner, again.Standings.DecidedTick, again.Standings.OutTick(0)));
        });
    }

    private static void Submit(ClientSession session, IEnumerable<Command> commands)
    {
        foreach (var command in commands)
        {
            Assert.True(session.Submit(command));
        }
    }

    private static void Until(Action step, Func<bool> condition, string what)
    {
        for (var i = 0; i < 6000 && !condition(); i++)
        {
            step();
        }

        Assert.True(condition(), $"Still waiting after 100 seconds: {what}.");
    }

    /// <summary>
    /// The orders of a siege of one player's starting Capitol: Ballistae in a row seven tiles
    /// from it, where they are in range of it (9 tiles, not nearer than 3), and a Tower beside
    /// it, in range of the workers that stand at it.
    /// </summary>
    private sealed class Siege
    {
        private readonly int _attacker;
        private readonly int _tower;
        private readonly TileSquare _towerTiles;

        public Siege(Match match, int attacker, int defender, int guns)
        {
            _attacker = attacker;
            Capitol = Enumerable.Range(0, match.Structures.Count)
                .Select(match.Structures.EntityAt)
                .Single(entity => match.Bodies.Get(entity).Owner == defender);
            var tiles = match.Structures.Get(Capitol).Tiles;
            var ballista = match.Data.Chassis.Single(chassis => chassis.Id == "ballista").Index;
            var tower = match.Data.Structures.Single(structure => structure.Attack is not null);
            _tower = tower.Index;

            // The workers stand in the row above the Capitol, or below it in the mirrored half
            // of the map. The guns go on the other side, the Tower beside it.
            var workersAbove = Enumerable.Range(0, match.Workers.Count)
                .Select(match.Workers.EntityAt)
                .Where(worker => match.Bodies.Get(worker).Owner == defender)
                .All(worker => match.Bodies.Get(worker).Position.Y > Fixed.FromInt(tiles.Y));
            var row = workersAbove ? tiles.Y - 7 : tiles.Y + tiles.Size + 6;
            var towerX = new[] { tiles.X + tiles.Size + 2, tiles.X - 4 }.First(x => match.HasRoomFor(tower, x, tiles.Y + 1));
            _towerTiles = new TileSquare(towerX, tiles.Y + 1, tower.Footprint);

            var making = new List<Command>();
            for (var i = 0; i < guns; i++)
            {
                var point = MapGrid.TileCenter(tiles.X - 1 + i, row);
                Assert.True(match.Grid.IsPassable(point), $"A gun cannot stand at {point}.");
                making.Add(new Command(CommandType.DebugSpawn, attacker, targetX: point.X, targetY: point.Y, contentId: ballista));
            }

            var corner = MapGrid.TileCenter(_towerTiles.X, _towerTiles.Y);
            making.Add(new Command(CommandType.DebugPlace, attacker, targetX: corner.X, targetY: corner.Y, contentId: _tower));
            Making = [.. making];
        }

        public EntityId Capitol { get; }

        /// <summary>The middle of the Tower, where its shots are fired from.</summary>
        public FixedVector2 TowerCenter => _towerTiles.Center;

        /// <summary>The development spawns of the guns and the development placement of the Tower.</summary>
        public Command[] Making { get; }

        /// <summary>The order of every gun there is to attack the Capitol.</summary>
        public Command Attack(Match match) => new(CommandType.Attack, _attacker, GunsOf(match, _attacker), targetEntity: Capitol);

        /// <summary>The Tower of the siege on a machine, or none when it is not placed there yet.</summary>
        public EntityId TowerIn(Match match)
        {
            for (var i = 0; i < match.Structures.Count; i++)
            {
                if (match.Structures.ValueAt(i).Type == _tower && match.Structures.ValueAt(i).Tiles == _towerTiles)
                {
                    return match.Structures.EntityAt(i);
                }
            }

            return EntityId.None;
        }

        public static EntityId[] GunsOf(Match match, int player) =>
        [
            .. Enumerable.Range(0, match.Combatants.Count)
                .Select(match.Combatants.EntityAt)
                .Where(entity => match.Bodies.Get(entity).Owner == player),
        ];

        public static EntityId[] UnitsOf(Match match, int player) =>
        [
            .. Enumerable.Range(0, match.Movers.Count)
                .Select(match.Movers.EntityAt)
                .Where(entity => match.Bodies.Get(entity).Owner == player),
        ];
    }

    /// <summary>Everything one machine's match reported, tick by tick.</summary>
    private sealed class Report
    {
        public List<SimEvent> Events { get; } = [];

        public static Report Of(MatchClient machine)
        {
            var report = new Report();
            machine.Session.TickCompleted += _ => report.Events.AddRange(machine.Match!.Simulation.Events);
            return report;
        }

        /// <summary>
        /// What this machine saw of the siege: the Capitol took every one of its hit points from
        /// shells that hit it, the last in the tick it fell; it was a death and then a structure
        /// removed in that tick; and before that the Tower had shot at the workers beside it.
        /// </summary>
        public void AssertTheFall(Siege siege, int fall, int defender)
        {
            var onTheCapitol = Events.Where(reported => reported.Entity == siege.Capitol).ToArray();
            var shells = onTheCapitol.Where(reported => reported.Kind == SimEventKind.ShotHit).ToArray();
            Assert.All(shells, shell => Assert.Equal(144_000, shell.Amount));
            Assert.True(shells.Where(shell => shell.Tick < fall).Sum(shell => shell.Amount) < CapitolHitPoints, "The Capitol stood after the hits that took all it had.");
            Assert.True(shells.Where(shell => shell.Tick == fall).Sum(shell => shell.Amount) > 0, "No shell hit the Capitol in the tick it fell.");
            Assert.True(shells.Sum(shell => shell.Amount) >= CapitolHitPoints, "The Capitol fell to less than its hit points.");
            Assert.DoesNotContain(shells, shell => shell.Tick > fall);

            var end = onTheCapitol.Where(reported => reported.Kind is SimEventKind.Death or SimEventKind.StructureRemoved).ToArray();
            Assert.Equal([SimEventKind.Death, SimEventKind.StructureRemoved], end.Select(reported => reported.Kind));
            Assert.All(end, reported => Assert.Equal((fall, defender), (reported.Tick, reported.Player)));

            var shotsOfTheTower = Events.Where(reported => reported.Kind == SimEventKind.ShotFired && reported.Tick < fall && reported.Position == siege.TowerCenter).ToArray();
            Assert.NotEmpty(shotsOfTheTower);
        }
    }
}
