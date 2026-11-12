using System.Reflection;
using Sim.Core;

namespace Game;

/// <summary>
/// Finds the fog of war in the view of a match, so that a sound is played only for what the
/// local player may know of (design 5.3): the same question the effects of a fight ask before
/// they are drawn.
/// </summary>
/// <remarks>
/// <para>
/// The fog comes with the fog view, 17v (pull request 48), which was still open when sound was
/// built. It gives the view of a match a property <c>Sight</c> whose method
/// <c>Knows(in SimEvent)</c> answers for one event. This branch has neither, and no fog: every
/// player is shown the whole match, so everything is known. So the property is looked for by
/// name when a match opens. A build with the fog view asks it for every event; a build
/// without it has nothing to hide.
/// </para>
/// <para>
/// A build that has a <c>Sight</c> without that method has changed under this piece. Then no
/// event of another player's is known, so that no sound gives away what the fog hides, and
/// <see cref="Problem"/> says so, which the sound part logs as an error.
/// </para>
/// <para>
/// Once the fog view is on <c>main</c>, this file can go: the sound part then asks
/// <c>View.Sight.Knows</c> itself.
/// </para>
/// </remarks>
public sealed class SightOfTheView
{
    /// <summary>The property of the view of a match that holds the local player's sight.</summary>
    public const string SightProperty = "Sight";

    /// <summary>The method of the sight that says whether the player may know of an event.</summary>
    public const string KnowsMethod = "Knows";

    private SightOfTheView(KnowsEvent knows, bool hasFog, string? problem)
    {
        Knows = knows;
        HasFog = hasFog;
        Problem = problem;
    }

    /// <summary>Whether the local player may know of an event.</summary>
    public KnowsEvent Knows { get; }

    /// <summary>True when the build has a fog of war, and <see cref="Knows"/> asks it.</summary>
    public bool HasFog { get; }

    /// <summary>What is wrong with the sight that was found, or null.</summary>
    public string? Problem { get; }

    /// <summary>Finds the sight of a view.</summary>
    /// <param name="view">The view of a match.</param>
    /// <param name="player">The local player's number.</param>
    public static SightOfTheView Of(object view, int player)
    {
        ArgumentNullException.ThrowIfNull(view);
        var property = view.GetType().GetProperty(SightProperty, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            return new SightOfTheView(Everything, hasFog: false, problem: null);
        }

        var sight = property.GetValue(view);
        var method = sight?.GetType().GetMethod(KnowsMethod, BindingFlags.Public | BindingFlags.Instance, [typeof(SimEvent).MakeByRefType()]);
        if (sight is null || method is null || method.ReturnType != typeof(bool))
        {
            return new SightOfTheView(
                (in SimEvent happened) => happened.Player == player,
                hasFog: true,
                $"The view of a match has a '{SightProperty}' without a method '{KnowsMethod}(in SimEvent)' that returns a truth value. Sounds are played for the local player's own events only.");
        }

        return new SightOfTheView(method.CreateDelegate<KnowsEvent>(sight), hasFog: true, problem: null);
    }

    private static bool Everything(in SimEvent happened) => true;
}
