using System;
using System.Numerics;

namespace PlateauResoniteLink.Core;

public static class TexturePowerOfTwo
{
    private const int MaxIntPowerOfTwo = 1 << 30;

    public static int RoundUp(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxIntPowerOfTwo);

        return checked((int)BitOperations.RoundUpToPowerOf2((uint)value));
    }

    public static int RoundDown(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while ((rounded << 1) > 0 && (rounded << 1) <= value)
        {
            rounded <<= 1;
        }

        return rounded;
    }
}
