using Godot;
using Net;
using Sim.Core;
using Sim.Setup;

namespace Game;

/// <summary>
/// One part of the smoke test's script: a stretch of ticks and the orders given in it. Each piece
/// that adds a kind of order adds a part, as a class in its own folder, and one line in the list
/// of <see cref="SmokeTest"/>.
/// </summary>
/// <remarks>
/// The parts run one after another in the order of the list, each for its own number of ticks,
/// and a part counts its ticks from its own start. A part that is added changes nothing in the
/// ticks of the parts before it. It must hold whatever ran before it: it reads the match as it
/// stands when its own first tick comes, and it gives only orders the simulation accepts then,
/// because one dropped order fails the test.
/// </remarks>
public abstract class SmokePart
{
    /// <summary>The part in a word or two, for the error that names it.</summary>
    public abstract string Name { get; }

    /// <summary>The number of ticks this part runs for.</summary>
    public abstract int Ticks { get; }

    /// <summary>
    /// Gives the orders that fall due before one tick of this part. Called once for every tick of
    /// the part, from 0 to <see cref="Ticks"/> - 1, before that tick runs.
    /// </summary>
    /// <param name="tick">The number of ticks this part has run.</param>
    /// <param name="orders">The match to read, and the way to give an order.</param>
    public abstract void GiveOrders(int tick, SmokeOrders orders);
}

/// <summary>
/// What a <see cref="SmokePart"/> works with: the match to read, the session to give orders
/// through, the way a player's orders go, and the view the match is shown through.
/// </summary>
public sealed class SmokeOrders
{
    private readonly ClientSession _client;
    private readonly HostSession _host;

    internal SmokeOrders(Match match, MatchView view, ClientSession client, HostSession host)
    {
        Match = match;
        View = view;
        _client = client;
        _host = host;
    }

    public Match Match { get; }

    /// <summary>
    /// The view of the match, as the main scene shows it. It has read every tick that has run,
    /// so a part can test what the player is shown: which units are drawn, and what the local
    /// player's <see cref="PlayerSight"/> answers. A part can also select units as a player does
    /// and ask a piece's part of the interface what it then shows. Selecting gives no order and
    /// changes nothing in the match.
    /// </summary>
    public MatchView View { get; }

    /// <summary>The local player's number.</summary>
    public int Player => _client.Player;

    /// <summary>The number of orders given so far, by every part.</summary>
    public int Count { get; private set; }

    /// <summary>True once something went wrong. The test then fails.</summary>
    public bool Failed { get; private set; }

    /// <summary>The local player's units as they stand now, in ID order.</summary>
    public EntityId[] OwnUnits()
    {
        var units = new List<EntityId>();
        for (var i = 0; i < Match.Bodies.Count; i++)
        {
            var entity = Match.Bodies.EntityAt(i);
            if (Match.Bodies.ValueAt(i).Owner == Player && Match.Movers.Contains(entity))
            {
                units.Add(entity);
            }
        }

        units.Sort();
        return [.. units];
    }

    /// <summary>Gives an order. The test fails when the session refuses it.</summary>
    public void Submit(in Command command)
    {
        if (_client.Submit(command))
        {
            Count++;
        }
        else
        {
            Fail($"the session refused a {command.Type} order at tick {Match.World.Tick}.");
        }
    }

    /// <summary>
    /// Gives an order for any seat. The local player's goes as <see cref="Submit"/> sends it.
    /// Nobody sits at the other seats of the smoke test's match, so the host gives theirs, the
    /// way it gives an AI player's. The test fails when the session refuses the order.
    /// </summary>
    public void SubmitFor(in Command command)
    {
        if (command.Player == Player)
        {
            Submit(command);
        }
        else if (_host.Submit(command))
        {
            Count++;
        }
        else
        {
            Fail($"the host refused a {command.Type} order for player {command.Player} at tick {Match.World.Tick}.");
        }
    }

    /// <summary>Fails the test, with the reason as the rest of a sentence.</summary>
    public void Fail(string reason)
    {
        Failed = true;
        GD.PrintErr($"Smoke test: {reason}");
    }
}
