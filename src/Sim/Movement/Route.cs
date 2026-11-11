namespace Sim.Movement;

/// <summary>A walk between two tiles, as <see cref="RouteFinder"/> found it.</summary>
/// <param name="Tiles">The tiles from start to goal, both included, each as <c>y * width + x</c>.</param>
/// <param name="Expansions">What the search cost, in tiles expanded.</param>
public sealed record Route(IReadOnlyList<int> Tiles, int Expansions);
