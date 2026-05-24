using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record DemTerrainGridBounds(
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ);

internal static class DemTerrainGridBoundsFactory
{
    public static DemTerrainGridBounds Create(
        IReadOnlyList<Float3> positions,
        GeographicRectangle cityObjectGeographicBounds,
        double referenceLatitude,
        double referenceLongitude,
        double referenceAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        Func<double, double, double, Float3> projectLocalPosition)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(cityObjectGeographicBounds);
        ArgumentNullException.ThrowIfNull(projectLocalPosition);

        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);
        DemTerrainGridBounds rawBounds = new(rawMinX, rawMaxX, rawMinZ, rawMaxZ);

        if (demTerrainTextureOverlay is null)
        {
            return rawBounds;
        }

        GeographicRectangle clippedBounds = IntersectGeographicBounds(
            cityObjectGeographicBounds,
            demTerrainTextureOverlay.GeographicBounds);
        Float3 westPosition = projectLocalPosition(referenceLatitude, clippedBounds.MinLongitude, referenceAltitude);
        Float3 eastPosition = projectLocalPosition(referenceLatitude, clippedBounds.MaxLongitude, referenceAltitude);
        Float3 southPosition = projectLocalPosition(clippedBounds.MinLatitude, referenceLongitude, referenceAltitude);
        Float3 northPosition = projectLocalPosition(clippedBounds.MaxLatitude, referenceLongitude, referenceAltitude);

        double clippedMinX = Math.Min(westPosition.X, eastPosition.X);
        double clippedMaxX = Math.Max(westPosition.X, eastPosition.X);
        double clippedMinZ = Math.Min(southPosition.Z, northPosition.Z);
        double clippedMaxZ = Math.Max(southPosition.Z, northPosition.Z);

        return (clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6
            ? rawBounds
            : new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ);
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
}
