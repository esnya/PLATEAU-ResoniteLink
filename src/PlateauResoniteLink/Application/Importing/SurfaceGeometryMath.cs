using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class SurfaceGeometryMath
{
    public static Float3? ComputeNewellNormal(IEnumerable<Float3> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        Float3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            Float3 current = points[index];
            Float3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }
}
