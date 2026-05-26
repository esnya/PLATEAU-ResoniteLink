using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

using ProjectionPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;

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

    internal static bool IsFacadeSurface(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
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

    internal static bool IsNearHorizontalSurface(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    internal static HashSet<string> GetCulledSurfaceIdsBeforeProjection(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return [];
        }

        SurfaceProjectionInfo[] candidates = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        double objectMinimumY = candidates.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = candidates.Max(static info => info.MaximumY!.Value);

        return candidates
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value <= objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => objectMaximumY > info.MaximumY!.Value + BuildingBottomCullBandMeters)
            .Select(static info => info.PolygonId)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static HashSet<string> GetCulledSurfaceIdsBeforeProjection(
        string packageName,
        IEnumerable<ProjectionSurface> surfaces,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return [];
        }

        SurfaceProjectionInfo[] candidates = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        double objectMinimumY = candidates.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = candidates.Max(static info => info.MaximumY!.Value);

        return candidates
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value <= objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => objectMaximumY > info.MaximumY!.Value + BuildingBottomCullBandMeters)
            .Select(static info => info.PolygonId)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static FacadeUvProjectionContext? TryCreateFacadeUvProjectionContext(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return null;
        }

        SurfaceProjectionInfo[] surfaceInfos = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return null;
        }

        SurfaceProjectionInfo[] contextSurfaceInfos = surfaceInfos
            .Where(static info => !info.IsGeneratedLod1RoofSurface)
            .ToArray();
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

    internal static FacadeUvProjectionContext? TryCreateFacadeUvProjectionContext(
        string packageName,
        IEnumerable<ProjectionSurface> surfaces,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return null;
        }

        SurfaceProjectionInfo[] surfaceInfos = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return null;
        }

        SurfaceProjectionInfo[] contextSurfaceInfos = surfaceInfos
            .Where(static info => !info.IsGeneratedLod1RoofSurface)
            .ToArray();
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

    internal static Float3? ComputeSurfaceNormal(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return ComputePolygonNormal(positions);
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface.PolygonId, null, null, false, IsGeneratedLod1RoofSurface(surface.PolygonId));
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        return new SurfaceProjectionInfo(
            surface.PolygonId,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal,
            IsGeneratedLod1RoofSurface(surface.PolygonId));
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface.PolygonId, null, null, false, IsGeneratedLod1RoofSurface(surface.PolygonId));
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        return new SurfaceProjectionInfo(
            surface.PolygonId,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal,
            IsGeneratedLod1RoofSurface(surface.PolygonId));
    }

    private static (double MinimumY, double MaximumY) ResolveFacadeUvVerticalRange(
        IReadOnlyList<SurfaceProjectionInfo> contextSurfaceInfos,
        IReadOnlyList<SurfaceProjectionInfo> allSurfaceInfos)
    {
        double minimumY = contextSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double maximumY = contextSurfaceInfos.Max(static info => info.MaximumY!.Value);
        if (maximumY - minimumY > 1e-6 || contextSurfaceInfos.Count == allSurfaceInfos.Count)
        {
            return (minimumY, maximumY);
        }

        double fallbackMinimumY = allSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double fallbackMaximumY = allSurfaceInfos.Max(static info => info.MaximumY!.Value);
        return fallbackMaximumY - fallbackMinimumY > maximumY - minimumY
            ? (fallbackMinimumY, fallbackMaximumY)
            : (minimumY, maximumY);
    }

    private static bool IsGeneratedLod1RoofSurface(string polygonId)
    {
        return polygonId.Contains("_generated_shed-", StringComparison.Ordinal)
            || polygonId.Contains("_generated_gable-", StringComparison.Ordinal)
            || polygonId.Contains("_generated_hip-", StringComparison.Ordinal);
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

    private static Float3 CreateScenePosition(
        ProjectionPoint point,
        ProjectionPoint origin,
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
        string PolygonId,
        double? MinimumY,
        double? MaximumY,
        bool IsNearHorizontal,
        bool IsGeneratedLod1RoofSurface);
}
