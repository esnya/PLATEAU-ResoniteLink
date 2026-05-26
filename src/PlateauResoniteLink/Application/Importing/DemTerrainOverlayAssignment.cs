using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    public static bool HasOverlayCoverage(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds = null)
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

        GeographicRectangle[] requestedMeshBounds = requestedMeshCodeBounds is null
            ? []
            : requestedMeshCodeBounds
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
                ClipGeneratedSurfaceToRequestedMeshCodeBounds(generatedSurface, requestedMeshBounds);
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
                TerrainOverlayCoverage coverage = DemTerrainOverlayCoverageResolver.Resolve(
                    surfaceBounds,
                    demTerrainTextureOverlays);
                if (coverage.Kind == TerrainOverlayCoverageKind.Contained)
                {
                    continue;
                }

                if (coverage.Kind == TerrainOverlayCoverageKind.None)
                {
                    return false;
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        coverage.IntersectingOverlays);
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
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds = null,
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
        GeographicRectangle[] requestedMeshBounds = requestedMeshCodeBounds is null
            ? []
            : requestedMeshCodeBounds
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
                + $"overlays={demTerrainTextureOverlays.Count}, requested_mesh_code_bounds={requestedMeshBounds.Length})."));

        ParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !surface.UsesGeneratedDemTexture)
            .SelectMany(surface => parsedCityObject.SharedAcrossMeshCodes
                ? ClipSurfaceToRequestedMeshCodeBounds(surface, requestedMeshBounds, progressReporter, cancellationToken)
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
                .SelectMany(generatedSurface => ClipGeneratedSurfaceToRequestedMeshCodeBounds(
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
                ClipGeneratedSurfaceToRequestedMeshCodeBounds(
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
                TerrainOverlayCoverage coverage = DemTerrainOverlayCoverageResolver.Resolve(
                    surfaceBounds,
                    demTerrainTextureOverlays);
                if (coverage.Kind == TerrainOverlayCoverageKind.Contained)
                {
                    splitGeneratedSurfaces.Add((requestedMeshClippedSurface, coverage.ContainingOverlay!));
                    continue;
                }

                if (coverage.Kind == TerrainOverlayCoverageKind.None)
                {
                    throw new InvalidOperationException(
                        $"Mesh-code-bounds-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' has no matching terrain overlay coverage.");
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        coverage.IntersectingOverlays,
                        progressReporter,
                        cancellationToken);
                if (clippedSurfaces.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Mesh-code-bounds-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' did not produce any terrain-overlay-clipped geometry.");
                }

                splitGeneratedSurfaces.AddRange(clippedSurfaces);
            }
        }

        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> groupedGeneratedSurfaces =
            DemTerrainOverlayBoundarySliverPruner.TryPruneGroups(
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

    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshCodeBounds(
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

    private static ParsedSurface[] ClipSurfaceToRequestedMeshCodeBounds(
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

    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshCodeBounds(
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

    private static GeographicRectangle GetSurfaceGeographicBounds(
        ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.Vertices.Max(static point => point.Longitude));
    }

}
