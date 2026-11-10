using System.Collections.Immutable;
using Sim.Core;

namespace Net;

/// <summary>
/// One message of the session protocol. <see cref="MessageCodec"/> puts it on bytes and an
/// <see cref="IConnection"/> carries those, complete, once each and in the order they were sent.
/// </summary>
/// <remarks>
/// The protocol has two sides. A client sends <see cref="JoinRequest"/>, <see cref="ReadyNotice"/>,
/// <see cref="CommandRequest"/>, <see cref="HashReport"/> and <see cref="Pong"/>. The host sends
/// <see cref="JoinAccept"/>, <see cref="Ping"/>, <see cref="MatchStart"/>, <see cref="TickBundle"/>,
/// <see cref="PeerLeft"/> and <see cref="SessionEnd"/>. A message that arrives from the wrong side,
/// or at a point where it makes no sense, ends the sender's part in the session.
/// </remarks>
public abstract record NetMessage;

/// <summary>Client to host, first: asks for the seat the host attached this connection to.</summary>
/// <param name="ProtocolVersion">The client's <see cref="ProtocolInfo.Version"/>.</param>
/// <param name="CommandEncodingVersion">The client's <c>CommandEncoding.Version</c>.</param>
public sealed record JoinRequest(int ProtocolVersion, int CommandEncodingVersion) : NetMessage;

/// <summary>Host to client: the join is accepted.</summary>
/// <param name="Player">The client's player number, counted from 0.</param>
/// <param name="PlayerCount">The number of seats in the match.</param>
public sealed record JoinAccept(int Player, int PlayerCount) : NetMessage;

/// <summary>Client to host: the client has built its world and can start.</summary>
public sealed record ReadyNotice : NetMessage;

/// <summary>Host to client, at intervals: measures the round trip.</summary>
public sealed record Ping(int Sequence) : NetMessage;

/// <summary>Client to host: the answer to the <see cref="Ping"/> with the same number.</summary>
public sealed record Pong(int Sequence) : NetMessage;

/// <summary>Host to every client: the match starts. The bundles follow.</summary>
/// <param name="FirstTick">The tick every world must be at, and the first one to run.</param>
/// <param name="CommandDelay">How many ticks after the host receives a command it runs, 1 or more.</param>
/// <param name="HashInterval">Clients report their state hash before every tick that this divides.</param>
/// <param name="TickInterval">The time between ticks at which the host issues bundles.</param>
public sealed record MatchStart(int FirstTick, int CommandDelay, int HashInterval, TimeSpan TickInterval) : NetMessage;

/// <summary>Client to host: a command the player gave. The host decides the tick it runs in.</summary>
public sealed record CommandRequest(Command Command) : NetMessage;

/// <summary>
/// Host to every client: the commands of one tick, in the order they run. One is sent for every
/// tick, empty or not, and a client runs a tick only once it holds that tick's bundle.
/// </summary>
public sealed record TickBundle(int Tick, ImmutableArray<Command> Commands) : NetMessage;

/// <summary>
/// Client to host: the hash of the client's world before <paramref name="Tick"/> runs, which is
/// <c>World.ComputeHash()</c> when <c>World.Tick</c> equals it.
/// </summary>
public sealed record HashReport(int Tick, ulong Hash) : NetMessage;

/// <summary>Host to the remaining clients: a player's connection is gone or was dropped.</summary>
public sealed record PeerLeft(int Player, SessionEndReason Reason) : NetMessage;

/// <summary>
/// Host to client: the session is over for the receiver. No message follows it.
/// </summary>
/// <param name="Reason">Why.</param>
/// <param name="FinalTick">
/// The number of ticks that make up the match: the receiver runs up to but not including this
/// tick, so that every peer stops in the same state. -1 when the receiver stops where it is.
/// </param>
/// <param name="DesyncTick">The tick whose hashes differed, or -1 when that is not the reason.</param>
public sealed record SessionEnd(SessionEndReason Reason, int FinalTick, int DesyncTick) : NetMessage;
