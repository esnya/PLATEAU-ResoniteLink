using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using GeographicLib;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    private const double BoundarySliverMaxThicknessMeters = 0.10;
    private const double BoundarySliverMaxAreaRatio = 0.01;
    private const double BoundarySliverMaxAreaSquareMeters = 4.0;

    public static bool HasOverlayCoverage(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();
        if (generatedSurfaces.Length == 0 || demTerrainTextureOverlays.Count == 0)
        {
            return true;
        }

        GeographicRectangle[] requestedMeshBounds = requestedMeshAreas is null
            ? []
            : requestedMeshAreas
                .Select(static area => new GeographicRectangle(
                    area.SouthLatitude,
                    area.NorthLatitude,
                    area.WestLongitude,
                    area.EastLongitude))
                .Distinct()
                .ToArray();

        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            ParsedSurface[] requestedMeshClippedSurfaces =
                ClipGeneratedSurfaceToRequestedMeshAreas(generatedSurface, requestedMeshBounds);
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (ParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                if (!requestedMeshClippedSurface.Vertices.Any())
                {
                    return false;
                }

                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                bool hasContainingOverlay = demTerrainTextureOverlays.Any(overlay =>
                    surfaceBounds.MinLatitude >= overlay.GeographicBounds.MinLatitude
                    && surfaceBounds.MaxLatitude <= overlay.GeographicBounds.MaxLatitude
                    && surfaceBounds.MinLongitude >= overlay.GeographicBounds.MinLongitude
                    && surfaceBounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude);
                if (hasContainingOverlay)
                {
                    continue;
                }

                TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                    .Where(overlay =>
                        surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
                        && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
                        && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
                        && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude)
                    .ToArray();
                if (candidateOverlays.Length == 0)
                {
                    return false;
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        candidateOverlays);
                if (clippedSurfaces.Count == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        GeodeticPoint sharedOrigin = GetCityObjectOrigin(parsedCityObject);
        GeographicRectangle[] requestedMeshBounds = requestedMeshAreas is null
            ? []
            : requestedMeshAreas
                .Select(static area => new GeographicRectangle(
                    area.SouthLatitude,
                    area.NorthLatitude,
                    area.WestLongitude,
                    area.EastLongitude))
                .Distinct()
                .ToArray();

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Splitting DEM city object '{parsedCityObject.SlotKey}' "
                + $"(generated_surfaces={generatedSurfaces.Length}, non_generated_surfaces={parsedCityObject.Surfaces.Length - generatedSurfaces.Length}, "
                + $"overlays={demTerrainTextureOverlays.Count}, requested_mesh_areas={requestedMeshBounds.Length})."));

        ParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !surface.UsesGeneratedDemTexture)
            .SelectMany(surface => parsedCityObject.SharedAcrossMeshCodes
                ? ClipSurfaceToRequestedMeshAreas(surface, requestedMeshBounds, progressReporter, cancellationToken)
                : [surface])
            .ToArray();

        if (generatedSurfaces.Length == 0)
        {
            if (nonGeneratedSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = nonGeneratedSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
            yield break;
        }

        if (demTerrainTextureOverlays.Count == 0)
        {
            ParsedSurface[] texturelessGeneratedSurfaces = generatedSurfaces
                .SelectMany(generatedSurface => ClipGeneratedSurfaceToRequestedMeshAreas(
                    generatedSurface,
                    requestedMeshBounds,
                    progressReporter,
                    cancellationToken))
                .ToArray();
            ParsedSurface[] texturelessSurfaces = [.. texturelessGeneratedSurfaces, .. nonGeneratedSurfaces];
            if (texturelessSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = texturelessSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
            yield break;
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> splitGeneratedSurfaces = [];
        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedSurface[] requestedMeshClippedSurfaces =
                ClipGeneratedSurfaceToRequestedMeshAreas(
                    generatedSurface,
                    requestedMeshBounds,
                    progressReporter,
                    cancellationToken);
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (ParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
                    surfaceBounds.MinLatitude >= overlay.GeographicBounds.MinLatitude
                    && surfaceBounds.MaxLatitude <= overlay.GeographicBounds.MaxLatitude
                    && surfaceBounds.MinLongitude >= overlay.GeographicBounds.MinLongitude
                    && surfaceBounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude);
                if (containingOverlay is not null)
                {
                    splitGeneratedSurfaces.Add((requestedMeshClippedSurface, containingOverlay));
                    continue;
                }

                TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                    .Where(overlay =>
                        surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
                        && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
                        && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
                        && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude)
                    .ToArray();
                if (candidateOverlays.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Requested-mesh-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' has no matching terrain overlay coverage.");
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        candidateOverlays,
                        progressReporter,
                        cancellationToken);
                if (clippedSurfaces.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Requested-mesh-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' did not produce any terrain-overlay-clipped geometry.");
                }

                splitGeneratedSurfaces.AddRange(clippedSurfaces);
            }
        }

        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> groupedGeneratedSurfaces =
            TryPruneBoundarySliverGroups(
                splitGeneratedSurfaces,
                out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedGeneratedSurfaces)
                ? prunedGeneratedSurfaces
                : splitGeneratedSurfaces;

        IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] groups = groupedGeneratedSurfaces
            .GroupBy(static surface => surface.Overlay)
            .OrderBy(static group => group.Key.PackageName, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.GeographicBounds.MinLatitude)
            .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
            .ToArray();

        if (groups.Length == 1
            && nonGeneratedSurfaces.Length == 0
            && groupedGeneratedSurfaces.Count > 0)
        {
            yield return (
                parsedCityObject with
                {
                    Surfaces = groups[0].Select(static entry => entry.Surface).ToArray(),
                    GeodeticOriginOverride = sharedOrigin,
                },
                groups[0].First().Overlay);
            yield break;
        }

        bool suffixGeneratedObjects = groups.Length > 1 || nonGeneratedSurfaces.Length > 0;

        for (int index = 0; index < groups.Length; index++)
        {
            IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)> group = groups[index];
            cancellationToken.ThrowIfCancellationRequested();
            yield return (
                parsedCityObject with
                {
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = suffixGeneratedObjects
                        ? $"{parsedCityObject.DisplayName} ({index + 1})"
                        : parsedCityObject.DisplayName,
                    Surfaces = group.Select(static entry => entry.Surface).ToArray(),
                    GeodeticOriginOverride = sharedOrigin,
                },
                group.First().Overlay);
        }

        ParsedSurface[] untexturedSurfaces = nonGeneratedSurfaces;
        if (untexturedSurfaces.Length == 0)
        {
            yield break;
        }

        yield return (parsedCityObject with { Surfaces = untexturedSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitForTerrainProjection(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in SplitParsedCityObject(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedMeshAreas,
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
                         requestedMeshAreas,
                         progressReporter,
                         cancellationToken))
            {
                yield return nonDemSplit;
            }
        }
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitForValidatedTerrainProjection(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        string validationContext,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(demTerrainTextureOverlays);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedMeshCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(validationContext);

        IReadOnlyList<MeshCodeBounds> requestedAreas = requestedMeshAreas ?? [];
        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in SplitForTerrainProjection(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedAreas,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldProjectTerrainOverlaySplit(
                    splitCityObject.CityObject.ActualMeshCode,
                    requestedMeshCode,
                    requestedAreas,
                    splitCityObject.Overlay))
            {
                throw TerrainTextureMeshCodeResolver.CreateMismatchException(
                    validationContext,
                    splitCityObject.CityObject.ActualMeshCode,
                    requestedMeshCode,
                    requestedMeshAreas,
                    splitCityObject.Overlay);
            }

            yield return splitCityObject;
        }
    }

    public static bool ShouldProjectTerrainOverlaySplit(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null)
        {
            return true;
        }

        if (requestedMeshAreas.Count > 0)
        {
            return TerrainTextureMeshCodeResolver.IsRequestedOverlay(terrainOverlay, requestedMeshAreas);
        }

        return TerrainTextureMeshCodeResolver.ResolveForMaterialOverlay(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                terrainOverlay)
            is not null;
    }

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitNonDemCityObjectByTerrainOverlay(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (demTerrainTextureOverlays.Count == 0 || !PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName))
        {
            yield return (cityObject, null);
            yield break;
        }

        GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(cityObject);
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

        double cityObjectMinAltitude = cityObjectVertices.Min(static vertex => vertex.Altitude);

        List<ParsedSurface> untexturedSurfaces = [];
        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> terrainOverlaySurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsNonDemTerrainTextureSurface(cityObject, surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian)
                || !TryCreateSurfaceGeographicBounds(surface, out GeographicRectangle surfaceBounds))
            {
                untexturedSurfaces.Add(surface);
                continue;
            }

            ParsedSurface terrainProjectionSurface = NormalizeNonDemTerrainTextureSurface(surface);

            TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                .Where(overlay => TerrainTextureMeshCodeResolver.IsRequestedOverlay(overlay, requestedMeshAreas)
                    || TerrainTextureMeshCodeResolver.ResolveForOverlay(cityObject.ActualMeshCode, overlay) is not null)
                .Where(overlay => BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                .ToArray();
            if (candidateOverlays.Length == 0)
            {
                TerrainTextureOverlay? overlappingOverlay = demTerrainTextureOverlays
                    .FirstOrDefault(overlay => BoundsOverlap(surfaceBounds, overlay.GeographicBounds));
                if (overlappingOverlay is not null)
                {
                    throw TerrainTextureMeshCodeResolver.CreateMismatchException(
                        "non-dem-terrain-candidate",
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshAreas,
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
                ContainsBounds(overlay.GeographicBounds, surfaceBounds));
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
                string terrainMeshCode = TerrainTextureMeshCodeResolver.ResolveForMaterialOverlay(
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshAreas,
                        terrainGroups[0].Key)
                    ?? throw TerrainTextureMeshCodeResolver.CreateMismatchException(
                        "non-dem-terrain-single-split",
                        cityObject.ActualMeshCode,
                        cityObject.ActualMeshCode,
                        requestedMeshAreas,
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
            string terrainMeshCode = TerrainTextureMeshCodeResolver.ResolveForMaterialOverlay(
                    cityObject.ActualMeshCode,
                    cityObject.ActualMeshCode,
                    requestedMeshAreas,
                    group.Key)
                ?? throw TerrainTextureMeshCodeResolver.CreateMismatchException(
                    "non-dem-terrain-split",
                    cityObject.ActualMeshCode,
                    cityObject.ActualMeshCode,
                    requestedMeshAreas,
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


    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshAreas(
        ParsedSurface generatedSurface,
        GeographicRectangle[] requestedMeshBounds,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [generatedSurface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            requestedMeshBounds,
            progressReporter,
            cancellationToken).ToArray();
    }

    private static ParsedSurface[] ClipSurfaceToRequestedMeshAreas(
        ParsedSurface surface,
        GeographicRectangle[] requestedMeshBounds,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [surface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipSurfaceToBounds(
            surface,
            requestedMeshBounds,
            progressReporter,
            cancellationToken).ToArray();
    }

    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshAreas(
        ParsedSurface generatedSurface,
        GeographicRectangle[] requestedMeshBounds)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [generatedSurface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            requestedMeshBounds).ToArray();
    }

    public static (Float2? TextureScale, Float2? TextureOffset) TryCreateTerrainGridTextureTransform(
        ParsedCityObject cityObject,
        ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        TextureUvRect? occupiedUvRect = TryCreateTerrainGridOccupiedUvRect(
            cityObject,
            materializedSurface,
            demTerrainTextureOverlay,
            cityObjectGeographicBounds);
        return occupiedUvRect is null
            ? (null, null)
            : (
                new Float2(occupiedUvRect.Value.ScaleValue.X, occupiedUvRect.Value.ScaleValue.Y),
                new Float2(occupiedUvRect.Value.OffsetValue.X, occupiedUvRect.Value.OffsetValue.Y));
    }

    public static TextureUvRect? TryCreateTerrainGridOccupiedUvRect(
        ParsedCityObject cityObject,
        ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || !materializedSurface.Surface.UsesGeneratedDemTexture)
        {
            return null;
        }

        GeographicRectangle overlayBounds = demTerrainTextureOverlay.GeographicBounds;
        GeographicRectangle objectBounds = IntersectGeographicBounds(
            cityObjectGeographicBounds ?? GetCityObjectGeographicBounds(cityObject),
            overlayBounds);
        if (objectBounds.MaxLongitude <= objectBounds.MinLongitude
            || objectBounds.MaxLatitude <= objectBounds.MinLatitude)
        {
            return null;
        }

        double overlayWest = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MinLongitude);
        double overlayEast = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MaxLongitude);
        double overlayNorth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MaxLatitude);
        double overlaySouth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MinLatitude);
        double overlayWidth = overlayEast - overlayWest;
        double overlayHeight = overlaySouth - overlayNorth;
        if (overlayWidth <= 1e-12 || overlayHeight <= 1e-12)
        {
            return null;
        }

        double objectWest = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MinLongitude);
        double objectEast = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MaxLongitude);
        double objectNorth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MaxLatitude);
        double objectSouth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MinLatitude);

        double uMin = Math.Clamp((objectWest - overlayWest) / overlayWidth, 0.0, 1.0);
        double uMax = Math.Clamp((objectEast - overlayWest) / overlayWidth, 0.0, 1.0);
        double vMin = Math.Clamp((overlaySouth - objectSouth) / overlayHeight, 0.0, 1.0);
        double vMax = Math.Clamp((overlaySouth - objectNorth) / overlayHeight, 0.0, 1.0);

        return new TextureUvRect(
            uMin,
            vMin,
            Math.Max(uMax - uMin, 1e-6),
            Math.Max(vMax - vMin, 1e-6));
    }

    private static bool TryPruneBoundarySliverSplit(
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces,
        out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
    {
        prunedSurfaces = [];
        if (clippedSurfaces.Count <= 1)
        {
            return false;
        }

        SurfaceMetrics[] metrics = clippedSurfaces
            .Select(static entry => ComputeSurfaceMetrics(entry.Surface))
            .ToArray();
        double totalArea = metrics.Sum(static metric => metric.AreaSquareMeters);
        if (totalArea <= 1e-9)
        {
            return false;
        }

        int dominantIndex = 0;
        for (int index = 1; index < metrics.Length; index++)
        {
            if (metrics[index].AreaSquareMeters > metrics[dominantIndex].AreaSquareMeters)
            {
                dominantIndex = index;
            }
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces =
        [
            clippedSurfaces[dominantIndex],
        ];
        bool prunedBoundarySliver = false;
        for (int index = 0; index < metrics.Length; index++)
        {
            if (index == dominantIndex)
            {
                continue;
            }

            double areaRatio = metrics[index].AreaSquareMeters / totalArea;
            bool isBoundarySliver = IsBoundarySliver(metrics[index], areaRatio);
            if (!isBoundarySliver)
            {
                keptSurfaces.Add(clippedSurfaces[index]);
                continue;
            }

            prunedBoundarySliver = true;
        }

        if (!prunedBoundarySliver)
        {
            return false;
        }

        if (keptSurfaces.Count == 1)
        {
            prunedSurfaces = [keptSurfaces[0]];
            return true;
        }

        prunedSurfaces = keptSurfaces
            .OrderBy(static entry => entry.Overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLongitude)
            .ToArray();
        return true;
    }

    private static bool TryPruneBoundarySliverGroups(
        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> surfaces,
        out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
    {
        prunedSurfaces = [];
        if (surfaces.Count <= 1)
        {
            return false;
        }

        GroupMetrics[] groups = surfaces
            .GroupBy(static surface => surface.Overlay)
            .Select(static group =>
            {
                (ParsedSurface Surface, TerrainTextureOverlay Overlay)[] groupSurfaces = group.ToArray();
                SurfaceMetrics[] metrics = groupSurfaces
                    .Select(static entry => ComputeSurfaceMetrics(entry.Surface))
                    .ToArray();
                return new GroupMetrics(
                    group.Key,
                    groupSurfaces,
                    metrics.Sum(static metric => metric.AreaSquareMeters),
                    metrics);
            })
            .ToArray();
        if (groups.Length <= 1)
        {
            return false;
        }

        double totalArea = groups.Sum(static group => group.AreaSquareMeters);
        if (totalArea <= 1e-9)
        {
            return false;
        }

        int dominantIndex = 0;
        for (int index = 1; index < groups.Length; index++)
        {
            if (groups[index].AreaSquareMeters > groups[dominantIndex].AreaSquareMeters)
            {
                dominantIndex = index;
            }
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces = [];
        bool prunedBoundarySliver = false;
        for (int index = 0; index < groups.Length; index++)
        {
            GroupMetrics group = groups[index];
            if (index != dominantIndex)
            {
                double areaRatio = group.AreaSquareMeters / totalArea;
                bool isBoundarySliverGroup = group.SurfaceMetrics.Count > 0
                    && group.SurfaceMetrics.All(metric => IsBoundarySliver(metric, areaRatio));
                if (isBoundarySliverGroup)
                {
                    prunedBoundarySliver = true;
                    continue;
                }
            }

            keptSurfaces.AddRange(group.Surfaces);
        }

        if (!prunedBoundarySliver || keptSurfaces.Count == surfaces.Count || keptSurfaces.Count == 0)
        {
            return false;
        }

        prunedSurfaces = keptSurfaces
            .OrderBy(static entry => entry.Overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLongitude)
            .ThenBy(static entry => entry.Surface.PolygonId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static GeodeticPoint GetCityObjectOrigin(
        ParsedCityObject cityObject)
    {
        if (cityObject.GeodeticOriginOverride is not null)
        {
            return cityObject.GeodeticOriginOverride!;
        }

        bool hasPoint = false;
        GeodeticPoint? origin = null;
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            foreach (GeodeticPoint point in surface.Vertices)
            {
                if (!hasPoint
                    || point.Latitude < origin!.Latitude
                    || (point.Latitude.Equals(origin.Latitude) && point.Longitude < origin.Longitude)
                    || (point.Latitude.Equals(origin.Latitude)
                        && point.Longitude.Equals(origin.Longitude)
                        && point.Altitude < origin.Altitude))
                {
                    origin = point;
                    hasPoint = true;
                }
            }
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("DEM city object has no vertices.");
        }

        return origin!;
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
        ParsedCityObject cityObject,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return surface.TexturePayload is null
            && !surface.UsesGeneratedDemTexture
            && SurfaceMaterialResolver.IsRoofTerrainTextureSurface(
                surface,
                cityObjectMinAltitude,
                cityObjectOrigin,
                cityObjectCartesian);
    }

    private static bool BoundsOverlap(GeographicRectangle left, GeographicRectangle right)
    {
        return left.MaxLatitude >= right.MinLatitude
            && left.MinLatitude <= right.MaxLatitude
            && left.MaxLongitude >= right.MinLongitude
            && left.MinLongitude <= right.MaxLongitude;
    }

    private static bool ContainsBounds(GeographicRectangle outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.MinLatitude
            && inner.MaxLatitude <= outer.MaxLatitude
            && inner.MinLongitude >= outer.MinLongitude
            && inner.MaxLongitude <= outer.MaxLongitude;
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

    private static GeographicRectangle GetCityObjectGeographicBounds(
        ParsedCityObject cityObject)
    {
        bool hasPoint = false;
        double minLatitude = 0.0;
        double maxLatitude = 0.0;
        double minLongitude = 0.0;
        double maxLongitude = 0.0;
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            foreach (GeodeticPoint point in surface.Vertices)
            {
                if (!hasPoint)
                {
                    minLatitude = maxLatitude = point.Latitude;
                    minLongitude = maxLongitude = point.Longitude;
                    hasPoint = true;
                    continue;
                }

                minLatitude = Math.Min(minLatitude, point.Latitude);
                maxLatitude = Math.Max(maxLatitude, point.Latitude);
                minLongitude = Math.Min(minLongitude, point.Longitude);
                maxLongitude = Math.Max(maxLongitude, point.Longitude);
            }
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("DEM city object has no vertices.");
        }

        return new GeographicRectangle(
            MinLatitude: minLatitude,
            MaxLatitude: maxLatitude,
            MinLongitude: minLongitude,
            MaxLongitude: maxLongitude);
    }

    private static GeographicRectangle GetSurfaceGeographicBounds(
        ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.Vertices.Max(static point => point.Longitude));
    }

    private static GeographicRectangle IntersectGeographicBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return new GeographicRectangle(
            MinLatitude: Math.Max(left.MinLatitude, right.MinLatitude),
            MaxLatitude: Math.Min(left.MaxLatitude, right.MaxLatitude),
            MinLongitude: Math.Max(left.MinLongitude, right.MinLongitude),
            MaxLongitude: Math.Min(left.MaxLongitude, right.MaxLongitude));
    }

    private static SurfaceMetrics ComputeSurfaceMetrics(ParsedSurface surface)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length < 3)
        {
            return new SurfaceMetrics(0.0, 0.0);
        }

        double referenceLatitude = vertices.Average(static point => point.Latitude) * (Math.PI / 180.0);
        double referenceLongitude = vertices.Average(static point => point.Longitude);
        double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitude);

        ProjectedPoint[] projected = vertices
            .Select(point => new ProjectedPoint(
                (point.Longitude - referenceLongitude) * metersPerLongitudeDegree,
                (point.Latitude - vertices[0].Latitude) * metersPerLatitudeDegree))
            .ToArray();

        double signedArea = 0.0;
        double maxDistance = 0.0;
        for (int index = 0; index < projected.Length; index++)
        {
            ProjectedPoint current = projected[index];
            ProjectedPoint next = projected[(index + 1) % projected.Length];
            signedArea += (current.X * next.Y) - (next.X * current.Y);
            maxDistance = Math.Max(maxDistance, Distance(current, next));
        }

        double areaSquareMeters = Math.Abs(signedArea) * 0.5;
        double estimatedThicknessMeters = maxDistance <= 1e-9
            ? 0.0
            : (2.0 * areaSquareMeters) / maxDistance;
        return new SurfaceMetrics(areaSquareMeters, estimatedThicknessMeters);
    }

    private static bool IsBoundarySliver(SurfaceMetrics metrics, double areaRatio)
    {
        return metrics.EstimatedThicknessMeters <= BoundarySliverMaxThicknessMeters
            && (areaRatio <= BoundarySliverMaxAreaRatio
                || metrics.AreaSquareMeters <= BoundarySliverMaxAreaSquareMeters);
    }

    private static double Distance(ProjectedPoint left, ProjectedPoint right)
    {
        double deltaX = right.X - left.X;
        double deltaY = right.Y - left.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct ProjectedPoint(double X, double Y);

    private readonly record struct SurfaceMetrics(double AreaSquareMeters, double EstimatedThicknessMeters);

    private readonly record struct GroupMetrics(
        TerrainTextureOverlay Overlay,
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> Surfaces,
        double AreaSquareMeters,
        IReadOnlyList<SurfaceMetrics> SurfaceMetrics);
}
