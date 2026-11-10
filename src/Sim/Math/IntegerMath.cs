namespace Sim.Numerics;

/// <summary>Integer routines that stand in for the platform math library, which the simulation never calls.</summary>
public static class IntegerMath
{
    /// <summary>
    /// The square root rounded down: the largest r with r * r not above <paramref name="value"/>.
    /// The square root of a squared distance between two <see cref="Fixed"/> positions, summed in a
    /// 64-bit integer, is the distance in raw <see cref="Fixed"/> units.
    /// </summary>
    public static uint Sqrt(ulong value)
    {
        // Digit-by-digit method in base 4: exact, with no division and a fixed bound of 32 steps.
        ulong result = 0;
        var bit = 1UL << 62;
        while (bit > value)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return (uint)result;
    }
}
