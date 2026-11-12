using Sim.Construction;
using Sim.Core;
using Sim.Economy;

namespace Ai.Tests;

/// <summary>
/// Expanding under the fog (design 5.5): an Outpost stands on ground its player has explored,
/// so when the computer player wants to expand and knows no such ground beside a deposit, it
/// sends a worker to look first, and orders the Outpost when it has seen where it goes.
/// </summary>
public class AiExplorerTests
{
    [Fact]
    public void FromItsStartItKnowsNoDepositItCouldClaim()
    {
        var fixture = new AiFixture();

        // The two deposits of its start are in its territory. The others are in the dark.
        Assert.Equal(2, fixture.Sight.Sites.Count(site => site.Kind == WorkSiteKind.Deposit));
        Assert.All(fixture.Sight.Sites, site => Assert.True(site.IsHeld));
        Assert.True(fixture.Match.WorkSites.Count > fixture.Sight.Sites.Count);
    }

    [Fact]
    public void WithNoPlaceKnownItSendsOneWorkerToTheNearestGroundItHasNotSeen()
    {
        var fixture = new AiFixture();
        var explorer = new AiExplorer(fixture.Sight, new AiSearch(fixture.Sight));
        var (homeX, homeY, _) = fixture.Home;

        explorer.Think(wantsToExpand: false, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        Assert.Empty(fixture.Commands);
        Assert.Equal(EntityId.None, fixture.Sight.Explorer);

        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        var worker = Assert.Single(order.Entities.ToArray());
        Assert.Equal(worker, fixture.Sight.Explorer);
        Assert.Contains(fixture.Sight.Workers, candidate => candidate.Entity == worker);
        var (x, y) = (order.TargetX.FloorToInt(), order.TargetY.FloorToInt());
        Assert.False(fixture.Sight.IsExplored(x, y));

        // On its way it is not told again, also when the stockpile has dropped below the mark meanwhile.
        fixture.Carry();
        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        explorer.Think(wantsToExpand: false, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        Assert.Empty(fixture.Commands);
        Assert.Equal(worker, fixture.Sight.Explorer);

        // It walks until it has seen the ground it was sent to, and is then free again.
        for (var round = 0; round < 200 && !fixture.Sight.IsExplored(x, y); round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
        }

        Assert.True(fixture.Sight.IsExplored(x, y));
        explorer.Think(wantsToExpand: false, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        Assert.Empty(fixture.Commands);
        Assert.Equal(EntityId.None, fixture.Sight.Explorer);
    }

    [Fact]
    public void ADepositItKnowsOfIsLookedAtBeforeGroundItHasNotSeen()
    {
        var fixture = new AiFixture();
        var explorer = new AiExplorer(fixture.Sight, new AiSearch(fixture.Sight));
        var (homeX, homeY, _) = fixture.Home;
        var deposit = new KnownSite(new EntityId(9999), WorkSiteKind.Deposit, 30, 60, 4, Places: 8, Holders: 0, IsHeld: false, IsDepleted: false);

        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit, homeX, homeY, fixture.Commands);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal((32, 62), (order.TargetX.FloorToInt(), order.TargetY.FloorToInt()));
        Assert.False(explorer.HasLookedAt(deposit.Entity));

        // When the worker stands beside it, it has been looked at, whatever was found there.
        fixture.Carry();
        var worker = fixture.Sight.Explorer;
        for (var round = 0; round < 200 && !explorer.HasLookedAt(deposit.Entity); round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
            explorer.Think(wantsToExpand: false, knowsAPlace: false, deposit, homeX, homeY, fixture.Commands);
        }

        Assert.True(explorer.HasLookedAt(deposit.Entity));
        var stands = fixture.Sight.Workers.Single(candidate => candidate.Entity == worker);
        Assert.True(AiSearch.DistanceToSquare(deposit.X, deposit.Y, deposit.Size, stands.TileX, stands.TileY) <= AiExplorer.NearDeposit * AiExplorer.NearDeposit);
    }

    [Fact]
    public void WhenAPlaceIsKnownTheWorkerIsFreeAtOnce()
    {
        var fixture = new AiFixture();
        var explorer = new AiExplorer(fixture.Sight, new AiSearch(fixture.Sight));
        var (homeX, homeY, _) = fixture.Home;
        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        Assert.NotEqual(EntityId.None, fixture.Sight.Explorer);

        explorer.Think(wantsToExpand: true, knowsAPlace: true, deposit: null, homeX, homeY, fixture.Commands);

        Assert.Equal(EntityId.None, fixture.Sight.Explorer);
    }

    [Fact]
    public void GroundItsWorkerDoesNotComeToSeeIsGivenUpForOtherGround()
    {
        var fixture = new AiFixture();
        var search = new AiSearch(fixture.Sight);
        var explorer = new AiExplorer(fixture.Sight, search);
        var (homeX, homeY, _) = fixture.Home;
        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);
        var first = Assert.Single(fixture.Commands);
        var worker = fixture.Sight.Explorer;

        // The order is never carried out, so the worker stays where it is.
        fixture.Commands.Clear();
        fixture.Run(AiSearch.DeadlineFor(0, 96 * 96));
        explorer.Think(wantsToExpand: true, knowsAPlace: false, deposit: null, homeX, homeY, fixture.Commands);

        var second = Assert.Single(fixture.Commands);
        Assert.Equal(worker, Assert.Single(second.Entities.ToArray()));
        Assert.NotEqual((first.TargetX, first.TargetY), (second.TargetX, second.TargetY));
        Assert.True(search.TryFindUnexplored(homeX, homeY, out var x, out var y));
        Assert.Equal((second.TargetX.FloorToInt(), second.TargetY.FloorToInt()), (x, y));
    }

    /// <summary>
    /// The whole of it, as the match takes it: the computer player wants another deposit, sends
    /// a worker out, orders the Outpost on ground it has seen, and the match refuses nothing.
    /// </summary>
    [Fact]
    public void ItLooksBeforeItOrdersAnOutpostAndTheOutpostIsBuilt()
    {
        var fixture = new AiFixture();
        var outpost = AiFixture.Structure("outpost");
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(expandAt: 600));
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);

        var looked = new List<EntityId>();
        Command? build = null;
        var refused = 0;
        for (var round = 0; round < 6 * 60 * 2 && fixture.Sight.Structures.All(structure => structure.Type != outpost.Index || !structure.IsFinished); round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
            ai.Think(fixture.Commands);
            foreach (var command in fixture.Commands)
            {
                if (command.Type == CommandType.Move)
                {
                    looked.Add(Assert.Single(command.Entities.ToArray()));
                }
                else if (command.Type == CommandType.Build && command.ContentId == outpost.Index)
                {
                    // Every tile it is to stand on has been seen.
                    Assert.Null(build);
                    build = command;
                    var x = command.TargetX.FloorToInt();
                    var y = command.TargetY.FloorToInt();
                    Assert.Equal(BuildProblem.None, fixture.Sight.CheckSite(outpost, x, y));
                    for (var tileY = y; tileY < y + outpost.Footprint; tileY++)
                    {
                        for (var tileX = x; tileX < x + outpost.Footprint; tileX++)
                        {
                            Assert.True(fixture.Sight.IsExplored(tileX, tileY));
                        }
                    }
                }
                else if (command.Type == CommandType.Gather)
                {
                    // While it looks, the worker is not sent back to work.
                    Assert.DoesNotContain(fixture.Sight.Explorer, command.Entities.ToArray());
                }
            }

            fixture.Carry();
            refused += fixture.Match.Simulation.Events.Count(simEvent => simEvent.Kind == SimEventKind.BuildRefused);
        }

        Assert.NotEmpty(looked);
        Assert.NotNull(build);
        Assert.Equal(0, refused);
        Assert.Contains(fixture.Sight.Structures, structure => structure.Type == outpost.Index && structure.IsFinished);

        // With the ground known, the worker that looked is one of the workers again.
        Assert.Equal(EntityId.None, fixture.Sight.Explorer);
    }

    /// <summary>With the ground beside a deposit known, nobody is sent to look: the Outpost is ordered in the first round.</summary>
    [Fact]
    public void WithAPlaceKnownNobodyIsSentToLook()
    {
        var fixture = new AiFixture();
        fixture.ExploreAll();
        var outpost = AiFixture.Structure("outpost");
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(expandAt: 600));
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);

        fixture.Run(AiPlayer.ThinkInterval);
        ai.Think(fixture.Commands);

        Assert.Empty(fixture.Orders(CommandType.Move));
        Assert.Single(fixture.Commands, command => command.Type == CommandType.Build && command.ContentId == outpost.Index);
        Assert.Equal(EntityId.None, fixture.Sight.Explorer);
    }
}
