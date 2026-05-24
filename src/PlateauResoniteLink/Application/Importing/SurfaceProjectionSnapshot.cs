using System;
using System.Linq;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal readonly record struct SurfaceProjectionSnapshot(
    ParsedSurface Surface,
    double? MinimumY,
    double? MaximumY,
    bool IsNearHorizontal,
    bool IsDownwardNearHorizontal);

internal static class SurfaceProjectionSnapshotFactory
{
    public static SurfaceProjectionSnapshot Create(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);

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
        if (positions.Length == 0)
        {
            return new SurfaceProjectionSnapshot(surface, null, null, false, false);
        }

        Float3? normal = SurfaceGeometryMath.ComputeNewellNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;
        bool isDownwardNearHorizontal = isNearHorizontal && normal is not null && normal.Y <= -0.98;

        return new SurfaceProjectionSnapshot(
            surface,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal,
            isDownwardNearHorizontal);
    }
}
