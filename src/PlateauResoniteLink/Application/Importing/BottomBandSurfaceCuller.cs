using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class BottomBandSurfaceCuller
{
    public static HashSet<string> GetCulledSurfaceIds(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return [];
        }

        SurfaceProjectionSnapshot[] candidates = surfaces
            .Select(surface => SurfaceProjectionSnapshotFactory.Create(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static snapshot => snapshot.MinimumY.HasValue && snapshot.MaximumY.HasValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        double objectMinimumY = candidates.Min(static snapshot => snapshot.MinimumY!.Value);
        double objectMaximumY = candidates.Max(static snapshot => snapshot.MaximumY!.Value);

        return candidates
            .Where(static snapshot => snapshot.IsNearHorizontal)
            .Where(snapshot => snapshot.MaximumY!.Value <= objectMinimumY + LocalCityGmlObjectProjection.BuildingBottomCullBandMeters)
            .Where(snapshot => objectMaximumY > snapshot.MaximumY!.Value + LocalCityGmlObjectProjection.BuildingBottomCullBandMeters)
            .Select(static snapshot => snapshot.Surface.PolygonId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
