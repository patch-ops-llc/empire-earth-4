using Sim.Core;

namespace Sim.Tests.Core;

public class CommandTypeTests
{
    /// <summary>
    /// Replays and the network protocol store the number of a command type. Two names with one
    /// number would make two kinds of command indistinguishable.
    /// </summary>
    [Fact]
    public void EveryCommandTypeHasItsOwnNumber()
    {
        var names = Enum.GetNames<CommandType>();
        var numbers = Enum.GetValues<CommandType>().Select(type => (int)type).ToArray();

        Assert.Equal(names.Length, numbers.Distinct().Count());
        Assert.Equal(0, (int)CommandType.None);
        Assert.All(numbers, number => Assert.InRange(number, 0, ushort.MaxValue));
    }
}
