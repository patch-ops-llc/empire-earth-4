using Sim.Core;
using Sim.Numerics;

namespace Game.Tests.Sound;

/// <summary>
/// How the sound finds the fog of war (<see cref="SightOfTheView"/>). The fog view was not on
/// this branch when sound was built, so the views here are stand-ins with the shape the fog
/// view gives the game's own: a property <c>Sight</c> with a method <c>Knows(in SimEvent)</c>.
/// </summary>
public class SightOfTheViewTests
{
    private const int Me = 0;
    private const int Other = 1;

    private static SimEvent Hit(int player) =>
        new(SimEventKind.MeleeHit, Tick: 1, player, new EntityId(1), new EntityId(2), FixedVector2.FromInts(3, 4), Amount: 0);

    private sealed class ViewWithoutFog
    {
        public int Player { get; } = Me;
    }

    private sealed class FogSight
    {
        public int Asked { get; private set; }

        /// <summary>The player sees the west of the map: x below 10.</summary>
        public bool Knows(in SimEvent happened)
        {
            Asked++;
            return happened.Player == Me || happened.Position.X < Fixed.FromInt(10);
        }
    }

    private sealed class ViewWithFog
    {
        public FogSight Sight { get; } = new();
    }

    private sealed class SightThatAnswersSomethingElse
    {
        private readonly EntityId _nobody = EntityId.None;

        public string Knows(in SimEvent happened) => happened.Kind + " of " + _nobody;

        public bool Knows(EntityId entity) => entity != _nobody;
    }

    private sealed class ViewWithAChangedSight
    {
        public SightThatAnswersSomethingElse Sight { get; } = new();
    }

    private sealed class ViewWithNoSightYet
    {
        public FogSight? Sight { get; }
    }

    [Fact]
    public void AViewWithoutASightHasNoFogAndEverythingIsKnown()
    {
        var sight = SightOfTheView.Of(new ViewWithoutFog(), Me);

        Assert.False(sight.HasFog);
        Assert.Null(sight.Problem);
        Assert.True(sight.Knows(Hit(Me)));
        Assert.True(sight.Knows(Hit(Other)));
    }

    [Fact]
    public void AViewWithASightIsAskedAboutEveryEvent()
    {
        var view = new ViewWithFog();
        var sight = SightOfTheView.Of(view, Me);

        var inSight = Hit(Other);
        var inTheDark = Hit(Other) with { Position = FixedVector2.FromInts(40, 4) };
        var ownInTheDark = Hit(Me) with { Position = FixedVector2.FromInts(40, 4) };

        Assert.True(sight.HasFog);
        Assert.Null(sight.Problem);
        Assert.True(sight.Knows(in inSight));
        Assert.False(sight.Knows(in inTheDark));
        Assert.True(sight.Knows(in ownInTheDark));
        Assert.Equal(3, view.Sight.Asked);
    }

    [Fact]
    public void UnderAFogAFightOutOfSightCallsForNoSound()
    {
        var sight = SightOfTheView.Of(new ViewWithFog(), Me);
        var requests = new List<SoundRequest>();

        new EventSounds(Me, new SoundEventKinds(_ => null)).Collect(
            [
                Hit(Other),
                Hit(Other) with { Position = FixedVector2.FromInts(40, 4) },
                Hit(Other) with { Kind = SimEventKind.Death, Position = FixedVector2.FromInts(50, 50) },
                Hit(Me) with { Kind = SimEventKind.ShotHit, Position = FixedVector2.FromInts(60, 4) },
            ],
            sight.Knows,
            requests);

        Assert.Equal([SoundCue.MeleeHit, SoundCue.ShotHit], requests.Select(request => request.Cue));
    }

    [Fact]
    public void ASightThatNoLongerAnswersForAnEventIsAProblemAndOnlyThePlayersOwnIsKnown()
    {
        var sight = SightOfTheView.Of(new ViewWithAChangedSight(), Me);

        Assert.True(sight.HasFog);
        Assert.NotNull(sight.Problem);
        Assert.True(sight.Knows(Hit(Me)));
        Assert.False(sight.Knows(Hit(Other)));
    }

    [Fact]
    public void ASightThatIsNotThereIsAProblemAndOnlyThePlayersOwnIsKnown()
    {
        var sight = SightOfTheView.Of(new ViewWithNoSightYet(), Me);

        Assert.True(sight.HasFog);
        Assert.NotNull(sight.Problem);
        Assert.True(sight.Knows(Hit(Me)));
        Assert.False(sight.Knows(Hit(Other)));
    }

    [Fact]
    public void TheGamesOwnViewHasNoSightOrOneThatAnswersForAnEvent()
    {
        // On a branch without the fog view the first half holds. Once the fog view is joined
        // the second must: if its sight is renamed or reshaped, this is where it shows.
        var property = typeof(MatchView).GetProperty(SightOfTheView.SightProperty);
        if (property is null)
        {
            return;
        }

        var knows = property.PropertyType.GetMethod(SightOfTheView.KnowsMethod, [typeof(SimEvent).MakeByRefType()]);
        Assert.NotNull(knows);
        Assert.Equal(typeof(bool), knows.ReturnType);
    }
}
