namespace Sim.Data;

/// <summary>
/// One problem found in the data. <see cref="Location"/> is a JSON path such as <c>$.base.hp</c>, a
/// line and column for a syntax error, or empty when the problem is the file as a whole.
/// </summary>
public sealed record DataError(string File, string Location, string Message)
{
    public override string ToString() =>
        Location.Length == 0 ? $"{File}: {Message}" : $"{File}: {Location}: {Message}";
}
