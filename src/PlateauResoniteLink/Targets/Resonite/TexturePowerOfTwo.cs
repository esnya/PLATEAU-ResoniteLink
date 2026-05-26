using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class TexturePowerOfTwo
{
    public static int RoundUp(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
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
