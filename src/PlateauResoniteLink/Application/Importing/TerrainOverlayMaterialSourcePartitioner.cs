using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainOverlayMaterialSourcePartitioner
{
    internal static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> PartitionParsedCityObject(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        bool allowMissingGeneratedDemOverlayCoverage = false,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) partitionedCityObject
                 in DemTerrainOverlayAssignment.SplitParsedCityObject(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     allowMissingGeneratedDemOverlayCoverage,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (partitionedCityObject.Overlay is not null
                || string.Equals(partitionedCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            {
                yield return partitionedCityObject;
                continue;
            }

            foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) nonDemPartition
                     in PartitionBuildingByTerrainOverlayMaterialSource(
                         partitionedCityObject.CityObject,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         progressReporter,
                         cancellationToken))
            {
                yield return nonDemPartition;
            }
        }
    }

    internal static bool IsPartitionCompatibleWithRequest(
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

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> PartitionBuildingByTerrainOverlayMaterialSource(
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
        string materialSourceMeshCode = string.IsNullOrWhiteSpace(cityObject.SourceMeshCode)
            ? cityObject.ActualMeshCode
            : cityObject.SourceMeshCode;

        List<ParsedSurface> untexturedSurfaces = [];
        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> terrainOverlayMaterialSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConstructionFace face = new(surface, ConstructionCityObjectDraft.ResolveRole(surface));
            if (!CanUseTerrainOverlayMaterialSource(face, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian)
                || !TryCreateSurfaceGeographicBounds(surface, out GeographicRectangle surfaceBounds))
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            ParsedSurface terrainMaterialSurface = PrepareTerrainOverlayMaterialSurface(face);

            TerrainTextureOverlay[] materialSourceOverlays = demTerrainTextureOverlays
                .Where(overlay => TerrainOverlayMeshCodeResolver.ResolveMeshCode(materialSourceMeshCode, overlay) is not null)
                .ToArray();
            TerrainTextureOverlay[] candidateOverlays = materialSourceOverlays.Length == 0
                ? demTerrainTextureOverlays
                    .Where(overlay => TerrainOverlayMeshCodeResolver.IsRequestedOverlay(overlay, requestedMeshCodeBounds))
                    .Where(overlay => TerrainOverlayMeshCodeResolver.BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                    .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                    .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                    .ToArray()
                : IsConcreteThirdMeshCode(materialSourceMeshCode)
                    ? materialSourceOverlays
                        .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                        .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                        .ToArray()
                : materialSourceOverlays
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
                        materialSourceMeshCode,
                        materialSourceMeshCode,
                        requestedMeshCodeBounds,
                        overlappingOverlay);
                }

                untexturedSurfaces.Add(surface);
                continue;
            }

            if (candidateOverlays.Length == 1)
            {
                terrainOverlayMaterialSurfaces.Add((terrainMaterialSurface, candidateOverlays[0]));
                continue;
            }

            TerrainTextureOverlay? containingOverlay = candidateOverlays.FirstOrDefault(overlay =>
                TerrainOverlayMeshCodeResolver.ContainsBounds(overlay.GeographicBounds, surfaceBounds));
            if (containingOverlay is not null)
            {
                terrainOverlayMaterialSurfaces.Add((terrainMaterialSurface, containingOverlay));
                continue;
            }

            IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                    terrainMaterialSurface,
                    candidateOverlays,
                    progressReporter,
                    cancellationToken);
            if (clippedSurfaces.Count == 0)
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            terrainOverlayMaterialSurfaces.AddRange(clippedSurfaces);
        }

        IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] terrainMaterialGroups =
            terrainOverlayMaterialSurfaces
                .GroupBy(static entry => entry.Overlay)
                .OrderBy(static group => group.Key.GeographicBounds.MinLatitude)
                .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
                .ToArray();
        int partitionCount = terrainMaterialGroups.Length + (untexturedSurfaces.Count == 0 ? 0 : 1);
        if (partitionCount == 0)
        {
            yield break;
        }

        if (partitionCount == 1)
        {
            if (terrainMaterialGroups.Length == 1)
            {
                ThirdRegionalMeshCode terrainMeshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
                        materialSourceMeshCode,
                        materialSourceMeshCode,
                        requestedMeshCodeBounds,
                        terrainMaterialGroups[0].Key)
                    ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                        "non-dem-terrain-single-partition",
                        materialSourceMeshCode,
                        materialSourceMeshCode,
                        requestedMeshCodeBounds,
                        terrainMaterialGroups[0].Key);
                yield return (
                    cityObject with
                    {
                        ActualMeshCode = terrainMeshCode.Value,
                        Surfaces = terrainMaterialGroups[0].Select(static entry => MarkTerrainOverlayMaterialSource(entry.Surface)).ToArray(),
                        GeodeticOriginOverride = cityObjectOrigin,
                    },
                    terrainMaterialGroups[0].Key);
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

        int partitionIndex = 0;
        foreach (IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)> group in terrainMaterialGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThirdRegionalMeshCode terrainMeshCode = TerrainOverlayMeshCodeResolver.ResolveForOverlay(
                    materialSourceMeshCode,
                    materialSourceMeshCode,
                    requestedMeshCodeBounds,
                    group.Key)
                ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                    "non-dem-terrain-partition",
                    materialSourceMeshCode,
                    materialSourceMeshCode,
                    requestedMeshCodeBounds,
                    group.Key);
            yield return (
                cityObject with
                {
                    ActualMeshCode = terrainMeshCode.Value,
                    SlotKey = $"{cityObject.SlotKey}_terrain_{terrainMeshCode}",
                    DisplayName = $"{cityObject.DisplayName} ({partitionIndex + 1})",
                    Surfaces = group.Select(static entry => MarkTerrainOverlayMaterialSource(entry.Surface)).ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                group.Key);
            partitionIndex++;
        }

        if (untexturedSurfaces.Count != 0)
        {
            yield return (
                cityObject with
                {
                    SlotKey = $"{cityObject.SlotKey}_terrain_none",
                    DisplayName = $"{cityObject.DisplayName} ({partitionIndex + 1})",
                    Surfaces = untexturedSurfaces.ToArray(),
                    GeodeticOriginOverride = cityObjectOrigin,
                },
                null);
        }
    }

    private static ParsedSurface MarkTerrainOverlayMaterialSource(ParsedSurface surface)
    {
        return surface with { UsesGeneratedDemTexture = true };
    }

    private static ParsedSurface PrepareTerrainOverlayMaterialSurface(ConstructionFace face)
    {
        ParsedSurface surface = face.Surface;
        if (face.Role is ConstructionFaceRole.RoofSlab)
        {
            return surface;
        }

        return surface.Semantic == ParsedSurfaceSemantic.Roof
            ? surface
            : surface with { Semantic = ParsedSurfaceSemantic.Roof };
    }

    private static bool IsConcreteThirdMeshCode(string meshCode)
    {
        return meshCode.Length == 8
            && meshCode.All(static character => character is >= '0' and <= '9');
    }

    private static bool CanUseTerrainOverlayMaterialSource(
        ConstructionFace face,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ParsedSurface surface = face.Surface;
        return surface.TexturePayload is null
            && !surface.UsesGeneratedDemTexture
            && RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
                face,
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
