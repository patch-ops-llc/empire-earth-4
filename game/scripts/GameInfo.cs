using System.Reflection;

namespace Game;

/// <summary>Facts about this build of the game.</summary>
public static class GameInfo
{
    /// <summary>The port a host listens on unless the player chooses another.</summary>
    public const int DefaultPort = 24642;

    /// <summary>
    /// The text that names this build. Two players can only share a match when theirs are equal.
    /// The export workflow sets it through the <c>GAME_VERSION</c> environment variable, to the
    /// tag or to <c>dev-</c> and the commit; a build made any other way is <c>dev</c>.
    /// </summary>
    public static string Version { get; } =
        typeof(GameInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
}
