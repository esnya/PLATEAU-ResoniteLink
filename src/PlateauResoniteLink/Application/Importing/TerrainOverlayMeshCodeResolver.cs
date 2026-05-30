using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainOverlayMeshCodeResolver
{
    internal static ThirdRegionalMeshCode? ResolveMeshCode(
        string actualMeshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        if (ThirdRegionalMeshCode.TryParse(actualMeshCode, out ThirdRegionalMeshCode actualThirdMeshCode)
            && BoundsApproximatelyEqual(actualThirdMeshCode.Bounds, terrainOverlay.GeographicBounds))
        {
            return actualThirdMeshCode;
        }

        if (!SecondRegionalMeshCode.TryParse(actualMeshCode, out SecondRegionalMeshCode secondMeshCode))
        {
            return null;
        }

        for (int latitudeIndex = 0; latitudeIndex < 10; latitudeIndex++)
        {
            for (int longitudeIndex = 0; longitudeIndex < 10; longitudeIndex++)
            {
                string thirdMeshCode = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{secondMeshCode.Value}{latitudeIndex}{longitudeIndex}");
                if (ThirdRegionalMeshCode.TryParse(thirdMeshCode, out ThirdRegionalMeshCode candidateThirdMeshCode)
                    && BoundsApproximatelyEqual(candidateThirdMeshCode.Bounds, terrainOverlay.GeographicBounds))
                {
                    return candidateThirdMeshCode;
                }
            }
        }

        return null;
    }

    internal static string ResolveMaterialMeshCodeSource(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null
            || ResolveMeshCode(actualMeshCode, terrainOverlay) is not null)
        {
            return actualMeshCode;
        }

        return ResolveMeshCode(requestedMeshCode, terrainOverlay)?.Value
            ?? ResolveFromRequestedMeshCodeBounds(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshCodeBounds,
                terrainOverlay)?.Value
            ?? requestedMeshCode;
    }

    internal static ThirdRegionalMeshCode? ResolveForOverlay(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay terrainOverlay)
    {
        return ResolveMeshCode(actualMeshCode, terrainOverlay)
            ?? ResolveMeshCode(requestedMeshCode, terrainOverlay)
            ?? ResolveFromRequestedMeshCodeBounds(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshCodeBounds,
                terrainOverlay);
    }

    internal static bool IsRequestedOverlay(
        TerrainTextureOverlay terrainOverlay,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds)
    {
        return requestedMeshCodeBounds is { Count: > 0 }
            && requestedMeshCodeBounds.Any(area => BoundsApproximatelyEqual(area, terrainOverlay.GeographicBounds)
                || ContainsBounds(area, terrainOverlay.GeographicBounds));
    }

    internal static bool BoundsOverlap(MeshCodeBounds meshBounds, GeographicRectangle geographicBounds)
    {
        return meshBounds.NorthLatitude >= geographicBounds.MinLatitude
            && meshBounds.SouthLatitude <= geographicBounds.MaxLatitude
            && meshBounds.EastLongitude >= geographicBounds.MinLongitude
            && meshBounds.WestLongitude <= geographicBounds.MaxLongitude;
    }

    internal static bool BoundsOverlap(GeographicRectangle left, GeographicRectangle right)
    {
        return left.MaxLatitude >= right.MinLatitude
            && left.MinLatitude <= right.MaxLatitude
            && left.MaxLongitude >= right.MinLongitude
            && left.MinLongitude <= right.MaxLongitude;
    }

    internal static bool ContainsBounds(GeographicRectangle outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.MinLatitude
            && inner.MaxLatitude <= outer.MaxLatitude
            && inner.MinLongitude >= outer.MinLongitude
            && inner.MaxLongitude <= outer.MaxLongitude;
    }

    private static ThirdRegionalMeshCode? ResolveFromRequestedMeshCodeBounds(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay terrainOverlay)
    {
        if (!IsRequestedOverlay(terrainOverlay, requestedMeshCodeBounds))
        {
            return null;
        }

        if (ResolveThirdMeshCodeFromOverlayBounds(terrainOverlay) is { } resolvedMeshCode)
        {
            return resolvedMeshCode;
        }

        foreach (string parentMeshCode in EnumerateCandidateParentMeshCodes(actualMeshCode, requestedMeshCode))
        {
            for (int latitudeIndex = 0; latitudeIndex < 10; latitudeIndex++)
            {
                for (int longitudeIndex = 0; longitudeIndex < 10; longitudeIndex++)
                {
                    string thirdMeshCode = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{parentMeshCode}{latitudeIndex}{longitudeIndex}");
                    if (ResolveMeshCode(thirdMeshCode, terrainOverlay) is { } candidateMeshCode)
                    {
                        return candidateMeshCode;
                    }
                }
            }
        }

        return null;
    }

    private static ThirdRegionalMeshCode? ResolveThirdMeshCodeFromOverlayBounds(TerrainTextureOverlay terrainOverlay)
    {
        const double firstLatitudeSpan = 40.0 / 60.0;
        const double firstLongitudeSpan = 1.0;
        const double tolerance = 1e-8;

        GeographicRectangle bounds = terrainOverlay.GeographicBounds;
        int firstLatitudeIndex = (int)Math.Floor((bounds.MinLatitude * 1.5) + tolerance);
        int firstLongitudeIndex = (int)Math.Floor((bounds.MinLongitude - 100.0) + tolerance);
        if (firstLatitudeIndex < 0 || firstLongitudeIndex < 0)
        {
            return null;
        }

        double firstSouthLatitude = firstLatitudeIndex / 1.5;
        double firstWestLongitude = 100.0 + firstLongitudeIndex;
        double secondLatitudeSpan = firstLatitudeSpan / 8.0;
        double secondLongitudeSpan = firstLongitudeSpan / 8.0;
        int secondLatitudeIndex = (int)Math.Floor(((bounds.MinLatitude - firstSouthLatitude) / secondLatitudeSpan) + tolerance);
        int secondLongitudeIndex = (int)Math.Floor(((bounds.MinLongitude - firstWestLongitude) / secondLongitudeSpan) + tolerance);
        if (secondLatitudeIndex is < 0 or > 7 || secondLongitudeIndex is < 0 or > 7)
        {
            return null;
        }

        double secondSouthLatitude = firstSouthLatitude + (secondLatitudeIndex * secondLatitudeSpan);
        double secondWestLongitude = firstWestLongitude + (secondLongitudeIndex * secondLongitudeSpan);
        double thirdLatitudeSpan = secondLatitudeSpan / 10.0;
        double thirdLongitudeSpan = secondLongitudeSpan / 10.0;
        int thirdLatitudeIndex = (int)Math.Floor(((bounds.MinLatitude - secondSouthLatitude) / thirdLatitudeSpan) + tolerance);
        int thirdLongitudeIndex = (int)Math.Floor(((bounds.MinLongitude - secondWestLongitude) / thirdLongitudeSpan) + tolerance);
        if (thirdLatitudeIndex is < 0 or > 9 || thirdLongitudeIndex is < 0 or > 9)
        {
            return null;
        }

        string meshCode = string.Create(
            CultureInfo.InvariantCulture,
            $"{firstLatitudeIndex:00}{firstLongitudeIndex:00}{secondLatitudeIndex}{secondLongitudeIndex}{thirdLatitudeIndex}{thirdLongitudeIndex}");
        return ResolveMeshCode(meshCode, terrainOverlay);
    }

    private static IEnumerable<string> EnumerateCandidateParentMeshCodes(string actualMeshCode, string requestedMeshCode)
    {
        return new[] { actualMeshCode, requestedMeshCode }
            .Where(static meshCode => meshCode.Length >= 6 && meshCode.All(static character => character is >= '0' and <= '9'))
            .Select(static meshCode => meshCode[..6])
            .Distinct(StringComparer.Ordinal);
    }

    private static bool BoundsApproximatelyEqual(
        JisRegionalMeshBounds meshBounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(meshBounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(meshBounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(meshBounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(meshBounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }

    private static bool BoundsApproximatelyEqual(
        MeshCodeBounds meshBounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(meshBounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(meshBounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(meshBounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(meshBounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }

    private static bool ContainsBounds(MeshCodeBounds outer, GeographicRectangle inner)
    {
        return inner.MinLatitude >= outer.SouthLatitude
            && inner.MaxLatitude <= outer.NorthLatitude
            && inner.MinLongitude >= outer.WestLongitude
            && inner.MaxLongitude <= outer.EastLongitude;
    }
}
