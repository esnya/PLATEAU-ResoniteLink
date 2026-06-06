using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlSurfaceProjectionPolicy
{
    private const double BuildingBottomCullBandMeters = 0.1;

    internal static bool IsFacadeSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) < 0.45;
    }

    internal static bool IsNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    internal static HashSet<ParsedSurface> GetCulledSurfacesBeforeProjection(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName))
        {
            return new HashSet<ParsedSurface>(ReferenceEqualityComparer.Instance);
        }

        SurfaceProjectionInfo[] candidates = CreateSurfaceProjectionInfos(
            cityObject.Faces,
            cityObjectOrigin,
            cityObjectCartesian);

        if (candidates.Length == 0)
        {
            return new HashSet<ParsedSurface>(ReferenceEqualityComparer.Instance);
        }

        double objectMinimumY = candidates.Min(static info => info.MinimumY);
        double objectMaximumY = candidates.Max(static info => info.MaximumY);

        return candidates
            .Where(static info => CanCullBottomFace(info.Role))
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY <= objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => objectMaximumY > info.MaximumY + BuildingBottomCullBandMeters)
            .Select(static info => info.Surface)
            .ToHashSet<ParsedSurface>(ReferenceEqualityComparer.Instance);
    }

    internal static FacadeUvProjectionContext? TryCreateFacadeUvProjectionContext(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName))
        {
            return null;
        }

        SurfaceProjectionInfo[] surfaceInfos = CreateSurfaceProjectionInfos(
            cityObject.Faces,
            cityObjectOrigin,
            cityObjectCartesian);
        if (surfaceInfos.Length == 0)
        {
            return null;
        }

        SurfaceProjectionInfo[] referenceSurfaceInfos = CreateSurfaceProjectionInfos(
            cityObject.FacadeUvReferenceFaces,
            cityObjectOrigin,
            cityObjectCartesian);
        SurfaceProjectionInfo[] contextSurfaceInfos = referenceSurfaceInfos;
        if (contextSurfaceInfos.Length == 0)
        {
            contextSurfaceInfos = surfaceInfos;
        }

        (double minimumY, double maximumY) = ResolveFacadeUvVerticalRange(contextSurfaceInfos, surfaceInfos);
        double geometryHeightMeters = Math.Max(maximumY - minimumY, 0.0);
        int floorCount = Math.Max(
            1,
            (int)Math.Ceiling(Math.Max(geometryHeightMeters, FacadeFloorMetrics.DefaultFloorUnitMeters) / FacadeFloorMetrics.DefaultFloorUnitMeters));
        double floorHeightMeters = Math.Max(
            geometryHeightMeters / floorCount,
            1e-6);

        return new FacadeUvProjectionContext(
            minimumY,
            maximumY,
            floorHeightMeters,
            floorCount);
    }

    internal static Float3? ComputeSurfaceNormal(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return ComputePolygonNormal(positions);
    }

    private static SurfaceProjectionInfo[] CreateSurfaceProjectionInfos(
        IEnumerable<ConstructionFace> faces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        List<SurfaceProjectionInfo> infos = [];
        foreach (ConstructionFace face in faces)
        {
            if (TryCreateSurfaceProjectionInfo(face, cityObjectOrigin, cityObjectCartesian, out SurfaceProjectionInfo info))
            {
                infos.Add(info);
            }
        }

        return [.. infos];
    }

    private static bool TryCreateSurfaceProjectionInfo(
        ConstructionFace face,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        out SurfaceProjectionInfo info)
    {
        info = default;
        ParsedSurface surface = face.Surface;
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return false;
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        info = new SurfaceProjectionInfo(
            surface,
            face.Role,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal);
        return true;
    }

    private static bool CanCullBottomFace(ConstructionFaceRole role)
    {
        return role is ConstructionFaceRole.Unknown
            or ConstructionFaceRole.Ground
            or ConstructionFaceRole.OuterFloor;
    }

    private static (double MinimumY, double MaximumY) ResolveFacadeUvVerticalRange(
        IReadOnlyList<SurfaceProjectionInfo> contextSurfaceInfos,
        IReadOnlyList<SurfaceProjectionInfo> allSurfaceInfos)
    {
        double minimumY = contextSurfaceInfos.Min(static info => info.MinimumY);
        double maximumY = contextSurfaceInfos.Max(static info => info.MaximumY);
        if (maximumY - minimumY > 1e-6 || contextSurfaceInfos.Count == allSurfaceInfos.Count)
        {
            return (minimumY, maximumY);
        }

        double fallbackMinimumY = allSurfaceInfos.Min(static info => info.MinimumY);
        double fallbackMaximumY = allSurfaceInfos.Max(static info => info.MaximumY);
        return fallbackMaximumY - fallbackMinimumY > maximumY - minimumY
            ? (fallbackMinimumY, fallbackMaximumY)
            : (minimumY, maximumY);
    }

    private static Float3? ComputePolygonNormal(IEnumerable<Float3> positions)
    {
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

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private readonly record struct SurfaceProjectionInfo(
        ParsedSurface Surface,
        ConstructionFaceRole Role,
        double MinimumY,
        double MaximumY,
        bool IsNearHorizontal);
}
