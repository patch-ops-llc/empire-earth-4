using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Net;

/// <summary>
/// A player's side of a match the host describes: a <see cref="ClientSession"/> that, when the
/// description arrives, checks it against this machine's build and data, builds the match and
/// reports ready, or leaves and says why it could not.
/// </summary>
/// <remarks>
/// The host's own player uses one too, over the loopback, so every machine builds its match from
/// the same bytes.
/// </remarks>
public sealed class MatchClient
{
    private readonly GameData _data;
    private readonly string _gameVersion;
    private readonly bool _readyWhenBuilt;

    /// <param name="connection">The connection to the host.</param>
    /// <param name="data">This machine's game data.</param>
    /// <param name="gameVersion">The text that names this machine's build. The host's must be the same.</param>
    /// <param name="options">The session's options. Null for the defaults.</param>
    /// <param name="readyWhenBuilt">
    /// True reports ready as soon as the match is built. False waits for <see cref="Ready"/>, so
    /// that the person at the machine says when.
    /// </param>
    public MatchClient(IConnection connection, GameData data, string gameVersion, ClientOptions? options = null, bool readyWhenBuilt = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(gameVersion);

        _data = data;
        _gameVersion = gameVersion;
        _readyWhenBuilt = readyWhenBuilt;
        Session = new ClientSession(connection, options);
        Session.MatchDescribed += Build;
    }

    /// <summary>The match was built from the host's description. <see cref="Match"/> is set.</summary>
    public event Action? MatchBuilt;

    public ClientSession Session { get; }

    /// <summary>The match as the host described it, once it has.</summary>
    public MatchDescription? Description => Session.Description;

    /// <summary>The match, built and before its first tick, once the host has described it and this machine could build it.</summary>
    public Match? Match { get; private set; }

    /// <summary>
    /// Why this machine could not build the match the host described, as a sentence for the
    /// player. The session then ended with <see cref="SessionEndReason.SetupMismatch"/>.
    /// </summary>
    public string? Refusal { get; private set; }

    /// <summary>True once the host was told that this player is ready.</summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Why a machine with this build and this data cannot play the match, as a sentence for the
    /// player, or null when it can.
    /// </summary>
    public static string? FindProblem(MatchDescription description, string gameVersion, GameData data)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(gameVersion);
        ArgumentNullException.ThrowIfNull(data);

        if (!string.Equals(description.GameVersion, gameVersion, StringComparison.Ordinal))
        {
            return $"The host runs version '{description.GameVersion}' of the game and this is version '{gameVersion}'. Both players need the same build.";
        }

        if (!string.Equals(description.ContentHash, data.ContentHash, StringComparison.Ordinal))
        {
            return "The host's game data is not the same as this machine's, although both builds have the same version. "
                + $"The host's content hash starts with {description.ContentHash[..12]} and this one's with {data.ContentHash[..12]}.";
        }

        return description.FindProblem(data);
    }

    /// <summary>Tells the host this player is ready. The match must have been built.</summary>
    public void Ready()
    {
        if (Match is null)
        {
            throw new InvalidOperationException("There is no match yet to be ready for.");
        }

        if (!IsReady && Session.Phase == SessionPhase.Joining)
        {
            IsReady = true;
            Session.ReportReady(Match.Simulation);
        }
    }

    /// <summary>Takes in what the host sent and runs the ticks that are due. Returns the number of ticks it ran.</summary>
    public int Advance(TimeSpan elapsed) => Session.Advance(elapsed);

    /// <summary>Leaves the session on purpose.</summary>
    public void Leave() => Session.Leave();

    private void Build(MatchDescription description)
    {
        var problem = FindProblem(description, _gameVersion, _data);
        if (problem is null)
        {
            try
            {
                Match = description.CreateMatch(_data);
            }
            catch (InvalidDataException refused)
            {
                problem = refused.Message;
            }
        }

        if (problem is not null)
        {
            Refusal = problem;
            Session.Leave(SessionEndReason.SetupMismatch);
            return;
        }

        MatchBuilt?.Invoke();
        if (_readyWhenBuilt)
        {
            Ready();
        }
    }
}
