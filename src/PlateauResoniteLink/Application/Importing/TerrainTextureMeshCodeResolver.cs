using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainTextureMeshCodeResolver
{
    internal static string ResolveMaterialMeshCodeSource(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainTextureOverlay? terrainOverlay)
    {
        if (terrainOverlay is null
            || ResolveForOverlay(actualMeshCode, terrainOverlay) is not null)
        {
            return actualMeshCode;
        }

        return ResolveForOverlay(requestedMeshCode, terrainOverlay)
            ?? ResolveFromRequestedAreas(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                terrainOverlay)
            ?? requestedMeshCode;
    }

    internal static string? ResolveForMaterialOverlay(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainTextureOverlay terrainOverlay)
    {
        return ResolveForOverlay(actualMeshCode, terrainOverlay)
            ?? ResolveForOverlay(requestedMeshCode, terrainOverlay)
            ?? ResolveFromRequestedAreas(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                terrainOverlay);
    }

    internal static string? ResolveForOverlay(
        string actualMeshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        if (actualMeshCode.Length == 8
            && MeshCodeBounds.TryParse(actualMeshCode) is { } actualMeshBounds
            && BoundsApproximatelyEqual(actualMeshBounds, terrainOverlay.GeographicBounds))
        {
            return actualMeshCode;
        }

        if (actualMeshCode.Length != 6
            || !actualMeshCode.All(static character => character is >= '0' and <= '9'))
        {
            return null;
        }

        for (int latitudeIndex = 0; latitudeIndex < 10; latitudeIndex++)
        {
            for (int longitudeIndex = 0; longitudeIndex < 10; longitudeIndex++)
            {
                string thirdMeshCode = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{actualMeshCode}{latitudeIndex}{longitudeIndex}");
                if (MeshCodeBounds.TryParse(thirdMeshCode) is { } thirdMeshBounds
                    && BoundsApproximatelyEqual(thirdMeshBounds, terrainOverlay.GeographicBounds))
                {
                    return thirdMeshCode;
                }
            }
        }

        return null;
    }

    internal static bool IsRequestedOverlay(
        TerrainTextureOverlay terrainOverlay,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas)
    {
        return requestedMeshAreas is { Count: > 0 }
            && requestedMeshAreas.Any(area => BoundsApproximatelyEqual(area, terrainOverlay.GeographicBounds)
                || ContainsBounds(area, terrainOverlay.GeographicBounds));
    }

    internal static InvalidOperationException CreateMismatchException(
        string phase,
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainTextureOverlay? terrainOverlay)
    {
        string requestedAreaSummary = requestedMeshAreas is { Count: > 0 }
            ? string.Join(
                ",",
                requestedMeshAreas.Select(static area => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatRounded(area.SouthLatitude)}-{FormatRounded(area.NorthLatitude)}-{FormatRounded(area.WestLongitude)}-{FormatRounded(area.EastLongitude)}")))
            : "<none>";
        string overlaySummary = terrainOverlay is null
            ? "<null>"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"package='{terrainOverlay.PackageName}', bounds='{FormatBounds(terrainOverlay.GeographicBounds)}', sources='{terrainOverlay.SourceDescriptorKey}'");
        return new InvalidOperationException(
            $"Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds. "
            + $"phase='{phase}', actual_mesh='{actualMeshCode}', requested_mesh='{requestedMeshCode}', "
            + $"requested_areas='{requestedAreaSummary}', overlay={overlaySummary}.");
    }

    private static string? ResolveFromRequestedAreas(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainTextureOverlay terrainOverlay)
    {
        if (!IsRequestedOverlay(terrainOverlay, requestedMeshAreas))
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
                    if (ResolveForOverlay(thirdMeshCode, terrainOverlay) is { } candidateMeshCode)
                    {
                        return candidateMeshCode;
                    }
                }
            }
        }

        return null;
    }

    private static string? ResolveThirdMeshCodeFromOverlayBounds(TerrainTextureOverlay terrainOverlay)
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
        return ResolveForOverlay(meshCode, terrainOverlay);
    }

    private static IEnumerable<string> EnumerateCandidateParentMeshCodes(string actualMeshCode, string requestedMeshCode)
    {
        return new[] { actualMeshCode, requestedMeshCode }
            .Where(static meshCode => meshCode.Length >= 6 && meshCode.All(static character => character is >= '0' and <= '9'))
            .Select(static meshCode => meshCode[..6])
            .Distinct(StringComparer.Ordinal);
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

    private static string FormatBounds(GeographicRectangle bounds) =>
        $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}";

    private static string FormatRounded(double value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
