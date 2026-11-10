using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

public class CommandQueueTests
{
    private static Command Numbered(int number) => new((CommandType)1, player: 0, contentId: number);

    private static int[] Take(CommandQueue queue, int tick)
    {
        var due = new List<Command>();
        queue.TakeDue(tick, due);
        return [.. due.Select(command => command.ContentId)];
    }

    [Fact]
    public void CommandsComeOutAtTheirTickInTheOrderTheyWentIn()
    {
        var queue = new CommandQueue();
        queue.Enqueue(2, Numbered(20));
        queue.Enqueue(0, Numbered(1));
        queue.Enqueue(2, Numbered(21));
        queue.Enqueue(1, Numbered(10));
        queue.Enqueue(0, Numbered(2));
        queue.Enqueue(2, Numbered(22));
        queue.Enqueue(0, Numbered(3));
        Assert.Equal(7, queue.Count);

        Assert.Equal([1, 2, 3], Take(queue, 0));
        Assert.Equal([10], Take(queue, 1));
        Assert.Equal([20, 21, 22], Take(queue, 2));
        Assert.Equal([], Take(queue, 3));
        Assert.Equal(0, queue.Count);
        Assert.Equal(4, queue.NextTick);
    }

    [Fact]
    public void ATickThatHasRunIsClosed()
    {
        var queue = new CommandQueue();
        queue.Enqueue(0, Numbered(1));
        Take(queue, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Enqueue(0, Numbered(2)));
        queue.Enqueue(1, Numbered(3));
        Assert.Equal([3], Take(queue, 1));
    }

    [Fact]
    public void TicksAreTakenInOrder()
    {
        var queue = new CommandQueue(nextTick: 5);
        Assert.Equal(5, queue.NextTick);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Enqueue(4, Numbered(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.TakeDue(6, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.TakeDue(4, []));
        Assert.Throws<ArgumentNullException>(() => queue.TakeDue(5, null!));
        Assert.Equal([], Take(queue, 5));
        Assert.Equal(6, queue.NextTick);
    }

    [Fact]
    public void TakeDueAddsToWhatTheListAlreadyHolds()
    {
        var queue = new CommandQueue();
        queue.Enqueue(0, Numbered(2));
        var due = new List<Command> { Numbered(1) };

        queue.TakeDue(0, due);

        Assert.Equal([1, 2], due.Select(command => command.ContentId));
    }

    [Fact]
    public void ACommandKeepsItsOwnCopyOfItsData()
    {
        EntityId[] entities = [new(3), new(1), new(2)];
        var command = new Command(
            (CommandType)1,
            player: 1,
            entities,
            targetX: Fixed.FromInt(4),
            targetY: Fixed.Half,
            targetEntity: new EntityId(9),
            contentId: 12);

        entities[0] = new EntityId(99);

        Assert.Equal((CommandType)1, command.Type);
        Assert.Equal(1, command.Player);
        Assert.Equal([new EntityId(3), new EntityId(1), new EntityId(2)], command.Entities.ToArray());
        Assert.Equal(Fixed.FromInt(4), command.TargetX);
        Assert.Equal(Fixed.Half, command.TargetY);
        Assert.Equal(new EntityId(9), command.TargetEntity);
        Assert.Equal(12, command.ContentId);

        Assert.True(default(Command).Entities.IsEmpty);
        Assert.Equal(CommandType.None, default(Command).Type);
        Assert.Equal(EntityId.None, new Command((CommandType)1, 0).TargetEntity);
    }
}
