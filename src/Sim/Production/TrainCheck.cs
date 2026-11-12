namespace Sim.Production;

/// <summary>
/// Whether an order to train can be placed, and when it cannot, the first reason in this order.
/// The numbers go out in events and nowhere into the state.
/// </summary>
public enum TrainCheck
{
    /// <summary>The order can be placed.</summary>
    Allowed = 0,

    /// <summary>The structure named is not a living, finished structure of the player's that trains.</summary>
    NoProducer = 1,

    /// <summary>The structure does not train this: not a worker here, no chassis of the data, or one of a line trained elsewhere.</summary>
    NotTrainedHere = 2,

    /// <summary>The player has not unlocked the chassis (design 2.2).</summary>
    Locked = 3,

    /// <summary>The structure already holds as many orders as it can (design 5.4).</summary>
    QueueFull = 4,

    /// <summary>The stockpile does not cover the price of one unit.</summary>
    TooExpensive = 5,

    /// <summary>The free supply does not cover one unit.</summary>
    NoSupply = 6,
}
