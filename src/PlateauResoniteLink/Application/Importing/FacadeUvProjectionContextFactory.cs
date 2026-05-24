using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class FacadeUvProjectionContextFactory
{
    public static FacadeUvProjectionContext? TryCreate(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);

        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return null;
        }

        SurfaceProjectionSnapshot[] surfaceInfos = surfaces
            .Select(surface => SurfaceProjectionSnapshotFactory.Create(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return null;
        }

        SurfaceProjectionSnapshot[] contextSurfaceInfos = surfaceInfos
            .Where(static info => !Lod1RoofGenerator.IsGeneratedSurface(info.Surface))
            .ToArray();
        if (contextSurfaceInfos.Length == 0)
        {
            contextSurfaceInfos = surfaceInfos;
        }

        (double minimumY, double maximumY) = ResolveVerticalRange(contextSurfaceInfos, surfaceInfos);
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

    private static (double MinimumY, double MaximumY) ResolveVerticalRange(
        SurfaceProjectionSnapshot[] contextSurfaceInfos,
        SurfaceProjectionSnapshot[] allSurfaceInfos)
    {
        double minimumY = contextSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double maximumY = contextSurfaceInfos.Max(static info => info.MaximumY!.Value);
        if (maximumY - minimumY > 1e-6 || contextSurfaceInfos.Length == allSurfaceInfos.Length)
        {
            return (minimumY, maximumY);
        }

        double fallbackMinimumY = allSurfaceInfos.Min(static info => info.MinimumY!.Value);
        double fallbackMaximumY = allSurfaceInfos.Max(static info => info.MaximumY!.Value);
        return fallbackMaximumY - fallbackMinimumY > maximumY - minimumY
            ? (fallbackMinimumY, fallbackMaximumY)
            : (minimumY, maximumY);
    }
}
