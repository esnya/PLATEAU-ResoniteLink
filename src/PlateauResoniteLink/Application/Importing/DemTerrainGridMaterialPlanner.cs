using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record DemTerrainGridMaterialPlan(
    MaterialBinding[] Materials,
    TextureUvRect? OccupiedUvRect);

internal static class DemTerrainGridMaterialPlanner
{
    public static DemTerrainGridMaterialPlan Create(
        ParsedCityObject cityObject,
        IReadOnlySet<string> culledSurfaceIds,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(culledSurfaceIds);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedMeshCode);
        ArgumentNullException.ThrowIfNull(materialResolver);

        ParsedCityObject ParsedCityObject = cityObject;
        double cityObjectMinAltitude = ParsedCityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Min(static vertex => vertex.Altitude);
        ResolvedSurfaceMaterial[] resolvedSurfaces = cityObject.Surfaces
            .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
            .Select(surface => SurfaceMaterialResolver.Resolve(new SurfaceMaterialResolutionRequest(
                ParsedCityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                surface,
                cityObjectMinAltitude,
                demTerrainTextureOverlay,
                materialResolver)))
            .ToArray();

        MaterialBinding[] materials = SurfaceMaterialGrouping.CreateForTerrain(
                cityObject.ActualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                resolvedSurfaces)
            .Select(static group => group.Binding)
            .ToArray();
        TextureUvRect? occupiedUvRect = TryCreateOccupiedUvRect(
            cityObject,
            demTerrainTextureOverlay,
            resolvedSurfaces);
        return new DemTerrainGridMaterialPlan(
            materials,
            occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect);
    }

    private static TextureUvRect? TryCreateOccupiedUvRect(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IEnumerable<ResolvedSurfaceMaterial> resolvedSurfaces)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        ResolvedSurfaceMaterial? representativeSurface = resolvedSurfaces
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);
        if (representativeSurface is null)
        {
            return null;
        }

        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
        return DemTerrainOverlayAssignment.TryCreateTerrainGridOccupiedUvRect(
            cityObject,
            representativeSurface,
            demTerrainTextureOverlay,
            demObjectBounds);
    }

    private static GeographicRectangle? TryGetDemObjectGeographicBounds(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return GetCityObjectGeographicBounds(cityObject);
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        List<GeodeticPoint> vertices = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        return new GeographicRectangle(
            MinLatitude: vertices.Min(static point => point.Latitude),
            MaxLatitude: vertices.Max(static point => point.Latitude),
            MinLongitude: vertices.Min(static point => point.Longitude),
            MaxLongitude: vertices.Max(static point => point.Longitude));
    }
}
