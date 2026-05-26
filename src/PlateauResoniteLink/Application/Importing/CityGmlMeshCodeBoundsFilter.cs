using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlMeshCodeBoundsFilter
{
    private static readonly Regex ConcreteMeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string ResolveActualMeshCode(
        string packageName,
        string displayName,
        string objectId,
        string fallbackActualMeshCode,
        bool sharedAcrossMeshCodes)
    {
        return sharedAcrossMeshCodes && string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? ResolveConcreteActualMeshCode(displayName, objectId, fallbackActualMeshCode)
            : fallbackActualMeshCode;
    }

    internal static bool IntersectsRequestedMeshCodeBounds(
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        CoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IEnumerable<ParsedSurface> surfaces)
    {
        if (requestedMeshCodeBounds is not { Count: > 0 }
            || !coordinateReferenceSystem.IsGeographic)
        {
            return true;
        }

        if (sharedAcrossMeshCodes
            && MeshCodeBounds.TryParse(actualMeshCode) is { } actualMeshCodeBounds)
        {
            return IntersectsMeshCodeBounds(actualMeshCodeBounds, requestedMeshCodeBounds);
        }

        return IntersectsMeshCodeBounds(surfaces, requestedMeshCodeBounds);
    }

    private static bool IntersectsMeshCodeBounds(
        IEnumerable<ParsedSurface> surfaces,
        IReadOnlyList<MeshCodeBounds> meshCodeAreas)
    {
        List<GeodeticPoint> vertices = surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        double minLatitude = vertices.Min(static point => point.Latitude);
        double maxLatitude = vertices.Max(static point => point.Latitude);
        double minLongitude = vertices.Min(static point => point.Longitude);
        double maxLongitude = vertices.Max(static point => point.Longitude);

        return IntersectsMeshCodeBounds(minLatitude, maxLatitude, minLongitude, maxLongitude, meshCodeAreas);
    }

    private static bool IntersectsMeshCodeBounds(
        MeshCodeBounds meshCodeArea,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        return IntersectsMeshCodeBounds(
            meshCodeArea.SouthLatitude,
            meshCodeArea.NorthLatitude,
            meshCodeArea.WestLongitude,
            meshCodeArea.EastLongitude,
            requestedMeshCodeBounds);
    }

    private static bool IntersectsMeshCodeBounds(
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        IReadOnlyList<MeshCodeBounds> meshCodeAreas)
    {
        const double overlapTolerance = 1e-10;

        return meshCodeAreas.Any(meshCodeArea =>
        {
            double latitudeOverlap = Math.Min(maxLatitude, meshCodeArea.NorthLatitude)
                - Math.Max(minLatitude, meshCodeArea.SouthLatitude);
            if (latitudeOverlap <= overlapTolerance)
            {
                return false;
            }

            double longitudeOverlap = Math.Min(maxLongitude, meshCodeArea.EastLongitude)
                - Math.Max(minLongitude, meshCodeArea.WestLongitude);
            return longitudeOverlap > overlapTolerance;
        });
    }

    private static string ResolveConcreteActualMeshCode(
        string displayName,
        string objectId,
        string fallbackActualMeshCode)
    {
        return TryResolveConcreteMeshCode(displayName, out string? displayNameMeshCode)
            ? displayNameMeshCode!
            : TryResolveConcreteMeshCode(objectId, out string? objectIdMeshCode)
                ? objectIdMeshCode!
                : fallbackActualMeshCode;
    }

    private static bool TryResolveConcreteMeshCode(string value, out string? meshCode)
    {
        meshCode = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match match = ConcreteMeshCodeTokenRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        string candidate = match.Groups[1].Value;
        if (!PlateauMeshCode.TryGetBounds(candidate, out _))
        {
            return false;
        }

        meshCode = candidate;
        return true;
    }
}
