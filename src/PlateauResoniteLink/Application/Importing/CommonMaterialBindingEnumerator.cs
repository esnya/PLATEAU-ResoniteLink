using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CommonMaterialBindingEnumerator
{
    public static MaterialBinding[] CreateSharedBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);
        ArgumentNullException.ThrowIfNull(materialResolver);

        ParsedSurface[] projectionSurfaces =
            cityObject.Surfaces.Select(static surface => surface).ToArray();
        HashSet<string> culledSurfaceIds = BottomBandSurfaceCuller.GetCulledSurfaceIds(
            cityObject.PackageName,
            projectionSurfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = projectionSurfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => SurfaceMaterialResolver.Resolve(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        return SurfaceMaterialGrouping.Create(cityObject.ActualMeshCode, resolvedSurfaces)
            .Select(static group => group.Binding)
            .Where(static material => material.ReuseScope == MaterialReuseScope.Shared)
            .ToArray();
    }
}
