using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainOverlayProjectionSplitPolicy
{
    internal static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in DemTerrainOverlayAssignment.SplitParsedCityObject(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (splitCityObject.Overlay is not null
                || string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            {
                yield return splitCityObject;
                continue;
            }

            foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) nonDemSplit
                     in SplitNonDemCityObjectByTerrainOverlay(
                         splitCityObject.CityObject,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         progressReporter,
                         cancellationToken))
            {
                yield return nonDemSplit;
            }
        }
    }

    internal static bool ShouldProjectSplit(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null)
        {
            return true;
        }

        if (requestedMeshCodeBounds.Count > 0)
        {
            return TerrainOverlayMeshCodeResolver.IsRequestedOverlay(terrainOverlay, requestedMeshCodeBounds);
        }

        return TerrainOverlayMeshCodeResolver.ResolveForOverlay(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshCodeBounds,
                terrainOverlay)
            is not null;
    }

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitNonDemCityObjectByTerrainOverlay(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (demTerrainTextureOverlays.Count == 0 || !PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName))
        {
            yield return (cityObject, null);
            yield break;
        }

        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        GeodeticPoint[] cityObjectVertices =
        [
            .. cityObject.Surfaces.SelectMany(static surface => surface.Vertices)
        ];
        if (cityObjectVertices.Length == 0)
        {
            yield return (cityObject, null);
            yield break;
        }

        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(cityObjectVertices);

        List<ParsedSurface> untexturedSurfaces = [];
        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> terrainOverlaySurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsNonDemTerrainTextureSurface(surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian)
                || !TryCreateSurfaceGeographicBounds(surface, out GeographicRectangle surfaceBounds))
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            ParsedSurface terrainProjectionSurface = NormalizeNonDemTerrainTextureSurface(surface);

            TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                .Where(overlay => TerrainOverlayMeshCodeResolver.IsRequestedOverlay(overlay, requestedMeshCodeBounds)
                    || TerrainOverlayMeshCodeResolver.ResolveMeshCode(cityObject.ActualMeshCode, overlay) is not null)
                .Where(overlay => TerrainOverlayMeshCodeResolver.BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                .ToArray();
            if (candidateOverlays.Length == 0)
            {
                TerrainTextureOverlay? overlappingOverlay = demTerrainTextureOverlays
                    .FirstOrDefault(overlay => TerrainOverlayMeshCodeResolver.BoundsOverlap(surfaceBounds, overlay.GeographicBounds));
                if (overlappingOverlay is not null)
                {
                    throw CreateTerrainOverlayMeshCodeMismatchException(
                        "non-dem-terrain-candidate",
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshCodeBounds,
                        overlappingOverlay);
                }

                untexturedSurfaces.Add(surface);
                continue;
            }

            if (candidateOverlays.Length == 1)
            {
                terrainOverlaySurfaces.Add((terrainProjectionSurface, candidateOverlays[0]));
                continue;
            }

            TerrainTextureOverlay? containingOverlay = candidateOverlays.FirstOrDefault(overlay =>
                TerrainOverlayMeshCodeResolver.ContainsBounds(overlay.GeographicBounds, surfaceBounds));
            if (containingOverlay is not null)
            {
                terrainOverlaySurfaces.Add((terrainProjectionSurface, containingOverlay));
                continue;
            }

            IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                    terrainProjectionSurface,
                    candidateOverlays,
                    progressReporter,
                    cancellationToken);
            if (clippedSurfaces.Count == 0)
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            terrainOverlaySurfaces.AddRange(clippedSurfaces);
        }

        IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] terrainGroups =
            terrainOverlaySurfaces
                .GroupBy(static entry => entry.Overlay)
                .OrderBy(static group => group.Key.GeographicBounds.MinLatitude)
                .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
                .ToArray();
        int splitCount = terrainGroups.Length + (untexturedSurfaces.Count == 0 ? 0 : 1);
        if (splitCount == 0)
        {
            yield break;
        }

        if (splitCount == 1)
        {
            if (terrainGroups.Length == 1)
            {
                string terrainMeshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshCodeBounds,
                        terrainGroups[0].Key)
                    ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                        "non-dem-terrain-single-split",
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshCodeBounds,
                        terrainGroups[0].Key);
                yield return (
                    cityObject with
                    {
                        ActualMeshCode = terrainMeshCode,
                        Surfaces = terrainGroups[0].Select(static entry => MarkUsesGeneratedDemTexture(entry.Surface)).ToArray(),
                        GeodeticOriginOverride = cityObjectOrigin,
                    },
                    terrainGroups[0].Key);
                yield break;
            }

            yield return (
                cityObject with
                {
                    Surfaces = untexturedSurfaces.ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                null);
            yield break;
        }

        int splitIndex = 0;
        foreach (IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)> group in terrainGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string terrainMeshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
                    cityObject.ActualMeshCode,
                    cityObject.ActualMeshCode,
                    requestedMeshCodeBounds,
                    group.Key)
                ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                    "non-dem-terrain-split",
                    cityObject.ActualMeshCode,
                    cityObject.ActualMeshCode,
                    requestedMeshCodeBounds,
                    group.Key);
            yield return (
                cityObject with
                {
                    ActualMeshCode = terrainMeshCode,
                    SlotKey = $"{cityObject.SlotKey}_terrain_{terrainMeshCode}",
                    DisplayName = $"{cityObject.DisplayName} ({splitIndex + 1})",
                    Surfaces = group.Select(static entry => MarkUsesGeneratedDemTexture(entry.Surface)).ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                group.Key);
            splitIndex++;
        }

        if (untexturedSurfaces.Count != 0)
        {
            yield return (
                cityObject with
                {
                    SlotKey = $"{cityObject.SlotKey}_terrain_none",
                    DisplayName = $"{cityObject.DisplayName} ({splitIndex + 1})",
                    Surfaces = untexturedSurfaces.ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                null);
        }
    }

    private static ParsedSurface MarkUsesGeneratedDemTexture(ParsedSurface surface)
    {
        return surface with { UsesGeneratedDemTexture = true };
    }

    private static ParsedSurface NormalizeNonDemTerrainTextureSurface(ParsedSurface surface)
    {
        return surface.Semantic == ParsedSurfaceSemantic.Roof
            ? surface
            : surface with { Semantic = ParsedSurfaceSemantic.Roof };
    }

    private static bool IsNonDemTerrainTextureSurface(
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        // Generated no-wall slab parts are extruded from the top roof surface, so terrain imagery follows the slab.
        return surface.TexturePayload is null
            && !surface.UsesGeneratedDemTexture
            && RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
                surface,
                cityObjectMinAltitude,
                cityObjectOrigin,
                cityObjectCartesian);
    }

    private static bool TryCreateSurfaceGeographicBounds(
        ParsedSurface surface,
        out GeographicRectangle bounds)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length == 0)
        {
            bounds = new GeographicRectangle(0.0, 0.0, 0.0, 0.0);
            return false;
        }

        bounds = new GeographicRectangle(
            vertices.Min(static vertex => vertex.Latitude),
            vertices.Max(static vertex => vertex.Latitude),
            vertices.Min(static vertex => vertex.Longitude),
            vertices.Max(static vertex => vertex.Longitude));
        return true;
    }

    private static GeodeticPoint GetCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static InvalidOperationException CreateTerrainOverlayMeshCodeMismatchException(
        string phase,
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        return TerrainOverlayDiagnostics.CreateMeshCodeMismatchException(
            phase,
            actualMeshCode,
            requestedMeshCode,
            requestedMeshCodeBounds,
            terrainOverlay);
    }
}
