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
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(IsDemTerrainOverlaySurface)
            .ToArray();
        if (generatedSurfaces.Length == 0 || demTerrainTextureOverlays.Count == 0)
        {
            return true;
        }

        DemClippingBounds requestedMeshBounds = CreateDemClippingBounds(parsedCityObject, requestedMeshCodeBounds);
        if (requestedMeshBounds.ExcludesAll)
        {
            return true;
        }

        TerrainTextureOverlay[] scopedTerrainTextureOverlays = CreateDemTerrainTextureOverlays(
            parsedCityObject,
            demTerrainTextureOverlays);

        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            ParsedSurface[] requestedMeshClippedSurfaces =
                ClipGeneratedSurfaceToRequestedMeshCodeBounds(generatedSurface, requestedMeshBounds.Bounds);
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
                TerrainOverlayCoverage coverage = ResolveTerrainOverlayCoverage(
                    surfaceBounds,
                    scopedTerrainTextureOverlays);
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

    public static bool HasOverlayCoverage(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return HasOverlayCoverage(parsedCityObject, demTerrainTextureOverlays, []);
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        bool allowMissingGeneratedDemOverlayCoverage = false,
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
        DemClippingBounds requestedMeshBounds = CreateDemClippingBounds(parsedCityObject, requestedMeshCodeBounds);
        if (requestedMeshBounds.ExcludesAll)
        {
            yield break;
        }

        TerrainTextureOverlay[] scopedTerrainTextureOverlays = CreateDemTerrainTextureOverlays(
            parsedCityObject,
            demTerrainTextureOverlays);

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(IsDemTerrainOverlaySurface)
            .ToArray();

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Assigning DEM city object '{parsedCityObject.SlotKey}' "
                + $"(terrain_texture_surfaces={generatedSurfaces.Length}, non_roof_surfaces={parsedCityObject.Surfaces.Length - generatedSurfaces.Length}, "
                + $"overlays={scopedTerrainTextureOverlays.Length}, requested_mesh_code_bounds={requestedMeshBounds.Bounds.Length})."));

        ParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !IsDemTerrainOverlaySurface(surface))
            .SelectMany(surface => parsedCityObject.SharedAcrossMeshCodes
                ? ClipSurfaceToRequestedMeshCodeBounds(surface, requestedMeshBounds.Bounds, progressReporter, cancellationToken)
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

        ParsedSurface[] clippedGeneratedSurfaces = generatedSurfaces
            .SelectMany(generatedSurface => ClipGeneratedSurfaceToRequestedMeshCodeBounds(
                generatedSurface,
                requestedMeshBounds.Bounds,
                progressReporter,
                cancellationToken))
            .ToArray();

        ParsedSurface[] assignedSurfaces = [.. clippedGeneratedSurfaces, .. nonGeneratedSurfaces];
        if (assignedSurfaces.Length == 0)
        {
            yield break;
        }

        if (scopedTerrainTextureOverlays.Length > 0 && !allowMissingGeneratedDemOverlayCoverage)
        {
            EnsureGeneratedDemOverlayCoverage(clippedGeneratedSurfaces, scopedTerrainTextureOverlays);
        }

        TerrainTextureOverlay? overlay = SelectDemTerrainTextureOverlay(
            clippedGeneratedSurfaces,
            scopedTerrainTextureOverlays);
        yield return (parsedCityObject with { Surfaces = assignedSurfaces, GeodeticOriginOverride = sharedOrigin }, overlay);
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return SplitParsedCityObject(parsedCityObject, demTerrainTextureOverlays, []);
    }

    private static GeographicRectangle[] CreateRequestedMeshBounds(
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        return requestedMeshCodeBounds
            .Select(static area => new GeographicRectangle(
                area.SouthLatitude,
                area.NorthLatitude,
                area.WestLongitude,
                area.EastLongitude))
            .Distinct()
            .ToArray();
    }

    private static DemClippingBounds CreateDemClippingBounds(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        GeographicRectangle[] requestedMeshBounds = CreateRequestedMeshBounds(requestedMeshCodeBounds);
        if (!parsedCityObject.SharedAcrossMeshCodes
            || !ThirdRegionalMeshCode.TryParse(parsedCityObject.ActualMeshCode, out ThirdRegionalMeshCode actualMeshCode))
        {
            return new DemClippingBounds(requestedMeshBounds, ExcludesAll: false);
        }

        GeographicRectangle actualMeshBounds = CreateGeographicRectangle(actualMeshCode.Bounds);
        if (requestedMeshBounds.Length == 0)
        {
            return new DemClippingBounds([actualMeshBounds], ExcludesAll: false);
        }

        GeographicRectangle[] intersectingBounds = requestedMeshBounds
            .Select(bounds => IntersectBounds(bounds, actualMeshBounds))
            .Where(static bounds => bounds is not null)
            .Select(static bounds => bounds!)
            .Distinct()
            .ToArray();
        return new DemClippingBounds(
            intersectingBounds,
            ExcludesAll: intersectingBounds.Length == 0);
    }

    private static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        if (!parsedCityObject.SharedAcrossMeshCodes
            || !ThirdRegionalMeshCode.TryParse(parsedCityObject.ActualMeshCode, out ThirdRegionalMeshCode actualMeshCode))
        {
            return demTerrainTextureOverlays.ToArray();
        }

        return demTerrainTextureOverlays
            .Where(overlay => overlay.MeshCode == actualMeshCode)
            .ToArray();
    }

    private static GeographicRectangle CreateGeographicRectangle(JisRegionalMeshBounds bounds)
    {
        return new GeographicRectangle(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    private static GeographicRectangle? IntersectBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        double minLatitude = Math.Max(left.MinLatitude, right.MinLatitude);
        double maxLatitude = Math.Min(left.MaxLatitude, right.MaxLatitude);
        double minLongitude = Math.Max(left.MinLongitude, right.MinLongitude);
        double maxLongitude = Math.Min(left.MaxLongitude, right.MaxLongitude);
        return maxLatitude > minLatitude && maxLongitude > minLongitude
            ? new GeographicRectangle(minLatitude, maxLatitude, minLongitude, maxLongitude)
            : null;
    }

    private sealed record DemClippingBounds(
        GeographicRectangle[] Bounds,
        bool ExcludesAll);

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

    private static void EnsureGeneratedDemOverlayCoverage(
        ParsedSurface[] generatedSurfaces,
        TerrainTextureOverlay[] demTerrainTextureOverlays)
    {
        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            if (!generatedSurface.Vertices.Any())
            {
                throw new InvalidOperationException(
                    "Mesh-code-bounds-clipped DEM surface has no vertices.");
            }

            GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(generatedSurface);
            TerrainOverlayCoverage coverage = ResolveTerrainOverlayCoverage(
                surfaceBounds,
                demTerrainTextureOverlays);
            if (coverage.Kind == TerrainOverlayCoverageKind.None)
            {
                throw new InvalidOperationException(
                    "Mesh-code-bounds-clipped DEM surface has no matching terrain overlay coverage.");
            }
        }
    }

    private static TerrainTextureOverlay? SelectDemTerrainTextureOverlay(
        ParsedSurface[] generatedSurfaces,
        TerrainTextureOverlay[] demTerrainTextureOverlays)
    {
        if (generatedSurfaces.Length == 0 || demTerrainTextureOverlays.Length == 0)
        {
            return null;
        }

        GeographicRectangle[] surfaceBounds = generatedSurfaces
            .Where(static surface => surface.Vertices.Any())
            .Select(GetSurfaceGeographicBounds)
            .ToArray();
        if (surfaceBounds.Length == 0)
        {
            return null;
        }

        return demTerrainTextureOverlays
            .OrderBy(static overlay => overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .FirstOrDefault(overlay => surfaceBounds.Any(bounds => Intersects(overlay.GeographicBounds, bounds)));
    }

    private static TerrainOverlayCoverage ResolveTerrainOverlayCoverage(
        GeographicRectangle surfaceBounds,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            Contains(overlay.GeographicBounds, surfaceBounds));
        if (containingOverlay is not null)
        {
            return TerrainOverlayCoverage.Contained(containingOverlay);
        }

        TerrainTextureOverlay[] intersectingOverlays = demTerrainTextureOverlays
            .Where(overlay => Intersects(overlay.GeographicBounds, surfaceBounds))
            .ToArray();
        return intersectingOverlays.Length == 0
            ? TerrainOverlayCoverage.None
            : TerrainOverlayCoverage.Intersecting(intersectingOverlays);
    }

    private static bool Contains(
        GeographicRectangle container,
        GeographicRectangle subject)
    {
        return subject.MinLatitude >= container.MinLatitude
            && subject.MaxLatitude <= container.MaxLatitude
            && subject.MinLongitude >= container.MinLongitude
            && subject.MaxLongitude <= container.MaxLongitude;
    }

    private static bool Intersects(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return right.MaxLatitude >= left.MinLatitude
            && right.MinLatitude <= left.MaxLatitude
            && right.MaxLongitude >= left.MinLongitude
            && right.MinLongitude <= left.MaxLongitude;
    }

    private static bool IsDemTerrainOverlaySurface(ParsedSurface surface)
    {
        return surface.TexturePayload is null;
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

    private readonly record struct TerrainOverlayCoverage(
        TerrainOverlayCoverageKind Kind,
        TerrainTextureOverlay? ContainingOverlay,
        IReadOnlyList<TerrainTextureOverlay> IntersectingOverlays)
    {
        public static TerrainOverlayCoverage None { get; } = new(
            TerrainOverlayCoverageKind.None,
            ContainingOverlay: null,
            IntersectingOverlays: []);

        public static TerrainOverlayCoverage Contained(TerrainTextureOverlay containingOverlay)
        {
            return new TerrainOverlayCoverage(
                TerrainOverlayCoverageKind.Contained,
                containingOverlay,
                IntersectingOverlays: []);
        }

        public static TerrainOverlayCoverage Intersecting(IReadOnlyList<TerrainTextureOverlay> intersectingOverlays)
        {
            return new TerrainOverlayCoverage(
                TerrainOverlayCoverageKind.Intersecting,
                ContainingOverlay: null,
                intersectingOverlays);
        }
    }

    private enum TerrainOverlayCoverageKind
    {
        None,
        Contained,
        Intersecting,
    }

}
