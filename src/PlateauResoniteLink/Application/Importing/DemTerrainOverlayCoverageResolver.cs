using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayCoverageResolver
{
    public static TerrainOverlayCoverage Resolve(
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
}

internal readonly record struct TerrainOverlayCoverage(
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

internal enum TerrainOverlayCoverageKind
{
    None,
    Contained,
    Intersecting,
}
