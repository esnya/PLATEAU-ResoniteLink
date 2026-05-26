using System;

namespace PlateauResoniteLink.Application.Importing;

internal static class PolygonNormal
{
    internal static Float3? Compute(ReadOnlySpan<Float3> positions)
    {
        if (positions.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            Float3 current = positions[index];
            Float3 next = positions[(index + 1) % positions.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double length = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        return length < 1e-8
            ? null
            : new Float3(normalX / length, normalY / length, normalZ / length);
    }
}
