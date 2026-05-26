using System;
using System.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal static class RoofTerrainTextureSurfacePolicy
{
    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;

    internal static bool IsRoofTerrainTextureSurface(
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.Semantic == ParsedSurfaceSemantic.Roof)
        {
            return true;
        }

        if (surface.Semantic is not (ParsedSurfaceSemantic.Unknown
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor))
        {
            return false;
        }

        Float3? normal = ComputeSurfaceNormal(
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && surface.Vertices.Min(static vertex => vertex.Altitude) > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static Float3? ComputeSurfaceNormal(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => SceneAxisMapper.CreatePosition(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObjectCartesian))
            .ToArray();
        return ComputePolygonNormal(positions);
    }

    private static Float3? ComputePolygonNormal(Float3[] points)
    {
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
