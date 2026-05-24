using System;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record SurfaceGeneratedUvProjection(
    Float3 AxisU,
    Float3 AxisV,
    double OffsetV)
{
    public static SurfaceGeneratedUvProjection? TryCreate(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        FacadeUvProjectionContext? facadeUvProjectionContext)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);

        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => SceneAxisMapper.CreatePosition(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObjectCartesian))
            .ToArray();
        if (positions.Length < 3)
        {
            return null;
        }

        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return null;
        }

        SurfaceUvAxes? surfaceAxes = TryCreatePathAlignedSurfaceUvAxes(packageName, positions, normal)
            ?? TryCreateSurfaceUvAxes(normal);
        if (surfaceAxes is null)
        {
            return null;
        }

        double uvScale = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? 1.0 / Math.Max(facadeUvProjectionContext?.FloorHeightMeters ?? FacadeFloorMetrics.DefaultFloorUnitMeters, 1e-6)
            : 1.0;
        double vOffset = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? facadeUvProjectionContext is { } context
                ? -(context.MinimumY * uvScale)
                : -(positions.Min(static position => position.Y) * uvScale)
            : 0.0;
        return new SurfaceGeneratedUvProjection(
            Scale(surfaceAxes.AxisU, uvScale),
            Scale(surfaceAxes.AxisV, uvScale),
            vOffset);
    }

    public Float2 CreateUv(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);

        Float3 position = SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            cityObjectCartesian);
        double u = Dot(position, AxisU);
        double v = Dot(position, AxisV) + OffsetV;
        return new Float2(u, v);
    }

    private static SurfaceUvAxes? TryCreateSurfaceUvAxes(Float3 normal)
    {
        Float3 verticalAxis = new(0.0, 1.0, 0.0);
        Float3 facadeAxisU = Cross(verticalAxis, normal);
        if (Magnitude(facadeAxisU) >= 1e-8)
        {
            return new SurfaceUvAxes(Normalize(facadeAxisU), verticalAxis);
        }

        Float3[] referenceAxes =
        [
            new Float3(1.0, 0.0, 0.0),
            new Float3(0.0, 0.0, 1.0),
            verticalAxis,
        ];

        foreach (Float3 referenceAxis in referenceAxes.OrderBy(axis => Math.Abs(Dot(normal, axis))))
        {
            Float3 axisU = Cross(referenceAxis, normal);
            if (Magnitude(axisU) < 1e-8)
            {
                continue;
            }

            axisU = Normalize(axisU);
            Float3 axisV = Cross(normal, axisU);
            if (Magnitude(axisV) < 1e-8)
            {
                continue;
            }

            return new SurfaceUvAxes(axisU, Normalize(axisV));
        }

        return null;
    }

    private static SurfaceUvAxes? TryCreatePathAlignedSurfaceUvAxes(
        string packageName,
        Float3[] positions,
        Float3 normal)
    {
        if (!PlateauPackageCatalog.IsPathLikePackage(packageName)
            || positions.Length < 2
            || Math.Abs(normal.Y) < 0.7)
        {
            return null;
        }

        Float3 axisU = Subtract(positions[1], positions[0]);
        double axisULength = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            Float3 start = positions[index];
            Float3 end = positions[(index + 1) % positions.Length];
            Float3 edge = Subtract(end, start);
            Float3 planarEdge = Subtract(edge, Multiply(normal, Dot(edge, normal)));
            double edgeLength = Magnitude(planarEdge);
            if (edgeLength <= axisULength)
            {
                continue;
            }

            axisU = planarEdge;
            axisULength = edgeLength;
        }

        if (axisULength < 1e-8)
        {
            return null;
        }

        axisU = Normalize(axisU);
        Float3 axisV = Cross(normal, axisU);
        if (Magnitude(axisV) < 1e-8)
        {
            return null;
        }

        return new SurfaceUvAxes(axisU, Normalize(axisV));
    }

    private static Float3? ComputePolygonNormal(Float3[] positions)
    {
        if (positions.Length < 3)
        {
            return null;
        }

        Float3 origin = positions[0];
        for (int index = 1; index + 1 < positions.Length; index++)
        {
            Float3 first = Subtract(positions[index], origin);
            Float3 second = Subtract(positions[index + 1], origin);
            Float3 normal = Cross(first, second);
            double magnitude = Magnitude(normal);
            if (magnitude > 1e-8)
            {
                return Normalize(normal);
            }
        }

        return null;
    }

    private static Float3 Scale(Float3 value, double scalar)
    {
        return new Float3(
            value.X * scalar,
            value.Y * scalar,
            value.Z * scalar);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static Float3 Multiply(Float3 vector, double scalar)
    {
        return new Float3(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static double Magnitude(Float3 vector)
    {
        return Math.Sqrt(Dot(vector, vector));
    }

    private static Float3 Normalize(Float3 vector)
    {
        double magnitude = Magnitude(vector);
        if (magnitude < 1e-12)
        {
            return new Float3(0.0, 0.0, 0.0);
        }

        return new Float3(
            vector.X / magnitude,
            vector.Y / magnitude,
            vector.Z / magnitude);
    }

    private sealed record SurfaceUvAxes(
        Float3 AxisU,
        Float3 AxisV);
}
