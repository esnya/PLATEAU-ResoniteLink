using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing.Plateau;

internal static class TerrainOverlayMeshCodeResolver
{
    private const double BoundaryTolerance = 1e-8;

    internal static bool IsRequestedOverlay(
        TerrainTextureOverlay terrainOverlay,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        return requestedMeshCodeBounds.Count == 0
            || requestedMeshCodeBounds.Any(area => BoundsApproximatelyEqual(area, terrainOverlay.MeshCode.Bounds)
                || ContainsBounds(area, terrainOverlay.GeographicBounds));
    }

    internal static bool BoundsOverlap(MeshCodeBounds meshBounds, GeographicRectangle geographicBounds)
    {
        return meshBounds.NorthLatitude - geographicBounds.MinLatitude > BoundaryTolerance
            && geographicBounds.MaxLatitude - meshBounds.SouthLatitude > BoundaryTolerance
            && meshBounds.EastLongitude - geographicBounds.MinLongitude > BoundaryTolerance
            && geographicBounds.MaxLongitude - meshBounds.WestLongitude > BoundaryTolerance;
    }

    internal static bool BoundsOverlap(GeographicRectangle left, GeographicRectangle right)
    {
        return left.MaxLatitude - right.MinLatitude > BoundaryTolerance
            && right.MaxLatitude - left.MinLatitude > BoundaryTolerance
            && left.MaxLongitude - right.MinLongitude > BoundaryTolerance
            && right.MaxLongitude - left.MinLongitude > BoundaryTolerance;
    }

    internal static bool ContainsBounds(GeographicRectangle outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.MinLatitude
            && inner.MaxLatitude <= outer.MaxLatitude
            && inner.MinLongitude >= outer.MinLongitude
            && inner.MaxLongitude <= outer.MaxLongitude;
    }

    private static bool BoundsApproximatelyEqual(
        MeshCodeBounds meshBounds,
        JisRegionalMeshBounds regionalMeshBounds)
    {
        return Math.Abs(meshBounds.SouthLatitude - regionalMeshBounds.SouthLatitude) <= BoundaryTolerance
            && Math.Abs(meshBounds.NorthLatitude - regionalMeshBounds.NorthLatitude) <= BoundaryTolerance
            && Math.Abs(meshBounds.WestLongitude - regionalMeshBounds.WestLongitude) <= BoundaryTolerance
            && Math.Abs(meshBounds.EastLongitude - regionalMeshBounds.EastLongitude) <= BoundaryTolerance;
    }

    private static bool ContainsBounds(MeshCodeBounds outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.SouthLatitude
            && inner.MaxLatitude <= outer.NorthLatitude
            && inner.MinLongitude >= outer.WestLongitude
            && inner.MaxLongitude <= outer.EastLongitude;
    }
}
