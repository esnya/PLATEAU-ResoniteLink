using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemSourceDiscoverySupport
{
    internal static DemDiscoveryAggregation AggregateDemParsedSourceFiles(
        IReadOnlyList<ParsedSourceFileResult> demParsedSourceFiles)
    {
        ArgumentNullException.ThrowIfNull(demParsedSourceFiles);

        CachedSourceFileDescriptor[] cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new CachedSourceFileDescriptor(
                parsed.SourceFile,
                parsed.CityObjects,
                parsed.ReferenceSystem))
            .ToArray();
        TerrainHeightTriangle[] terrainTriangles = demParsedSourceFiles
            .SelectMany(static parsed => parsed.TerrainTriangles)
            .ToArray();

        return new DemDiscoveryAggregation(
            cachedDemSourceFiles,
            terrainTriangles,
            demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length));
    }

    internal static TerrainHeightTriangle[] CreateTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

        List<TerrainHeightTriangle> terrainTriangles = [];
        foreach (ParsedSurface surface in cityObjects.SelectMany(static cityObject => cityObject.Surfaces))
        {
            GeodeticPoint[] vertices = surface.Vertices.ToArray();
            if (vertices.Length < 3)
            {
                continue;
            }

            GeodeticPoint triangleAnchor = vertices[0];
            for (int index = 1; index + 1 < vertices.Length; index++)
            {
                terrainTriangles.Add(new TerrainHeightTriangle(triangleAnchor, vertices[index], vertices[index + 1]));
            }
        }

        return terrainTriangles.ToArray();
    }

    internal static DemTerrainBounds? ResolveDemTerrainBounds(
        IEnumerable<ParsedSourceFileResult> demParsedSourceFiles,
        DemTerrainBounds? fallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(demParsedSourceFiles);

        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? bounds = null;
        foreach (ParsedSourceFileResult parsedSourceFile in demParsedSourceFiles)
        {
            if (parsedSourceFile.CityObjects.Length == 0 || !HasAnyVertices(parsedSourceFile.CityObjects))
            {
                continue;
            }

            bounds = MergeBounds(bounds, GetBounds(parsedSourceFile.CityObjects));
        }

        return bounds is null
            ? fallbackBounds
            : new DemTerrainBounds(
                bounds.Value.minLatitude,
                bounds.Value.maxLatitude,
                bounds.Value.minLongitude,
                bounds.Value.maxLongitude);
    }

    internal static DemTerrainOverlayRegion[] CreateDemTerrainOverlayRegions(
        DemTerrainBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        ArgumentNullException.ThrowIfNull(demBounds);
        ArgumentNullException.ThrowIfNull(requestedMeshCodes);

        List<DemTerrainOverlayRegion> overlays = [];
        foreach (string meshCode in ExpandToThirdMeshCodes(requestedMeshCodes))
        {
            if (!ThirdRegionalMeshCode.TryParse(meshCode, out ThirdRegionalMeshCode? thirdMeshCode))
            {
                continue;
            }

            JisRegionalMeshBounds bounds = thirdMeshCode.Bounds;
            if (bounds.NorthLatitude < demBounds.SouthLatitude
                || bounds.SouthLatitude > demBounds.NorthLatitude
                || bounds.EastLongitude < demBounds.WestLongitude
                || bounds.WestLongitude > demBounds.EastLongitude)
            {
                continue;
            }

            overlays.Add(CreateDemTerrainOverlayRegion(thirdMeshCode));
        }

        return overlays
            .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MaxLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MaxLongitude)
            .ToArray();
    }

    internal static DemTerrainOverlayRegion[] CreateDemTerrainOverlayRegions(
        IReadOnlyList<string> requestedMeshCodes)
    {
        ArgumentNullException.ThrowIfNull(requestedMeshCodes);

        List<DemTerrainOverlayRegion> overlays = [];
        foreach (string meshCode in ExpandToThirdMeshCodes(requestedMeshCodes))
        {
            if (!ThirdRegionalMeshCode.TryParse(meshCode, out ThirdRegionalMeshCode? thirdMeshCode))
            {
                continue;
            }

            overlays.Add(CreateDemTerrainOverlayRegion(thirdMeshCode));
        }

        return overlays
            .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MaxLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MaxLongitude)
            .ToArray();
    }

    internal static DemTerrainOverlayRegion[] CreateDemTerrainOverlayRegionsForMeshCodes(
        IReadOnlyList<string> meshCodes)
    {
        return CreateDemTerrainOverlayRegions(meshCodes);
    }

    private static DemTerrainOverlayRegion CreateDemTerrainOverlayRegion(
        ThirdRegionalMeshCode meshCode)
    {
        JisRegionalMeshBounds bounds = meshCode.Bounds;
        return new DemTerrainOverlayRegion(
            meshCode,
            new GeographicRectangle(
            MinLatitude: bounds.SouthLatitude,
            MaxLatitude: bounds.NorthLatitude,
            MinLongitude: bounds.WestLongitude,
            MaxLongitude: bounds.EastLongitude));
    }

    private static IEnumerable<string> ExpandToThirdMeshCodes(IEnumerable<string> requestedMeshCodes)
    {
        HashSet<string> yieldedMeshCodes = new(StringComparer.Ordinal);
        foreach (string meshCode in requestedMeshCodes
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(static code => code, StringComparer.Ordinal))
        {
            if (meshCode.Length == 8)
            {
                if (yieldedMeshCodes.Add(meshCode))
                {
                    yield return meshCode;
                }

                continue;
            }

            if (meshCode.Length != 6)
            {
                continue;
            }

            for (int latitudeIndex = 0; latitudeIndex < 10; latitudeIndex++)
            {
                for (int longitudeIndex = 0; longitudeIndex < 10; longitudeIndex++)
                {
                    string thirdMeshCode = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{meshCode}{latitudeIndex}{longitudeIndex}");
                    if (yieldedMeshCodes.Add(thirdMeshCode))
                    {
                        yield return thirdMeshCode;
                    }
                }
            }
        }
    }

    private static (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) GetBounds(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        List<GeodeticPoint> allPoints = cityObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        return (
            allPoints.Min(static point => point.Latitude),
            allPoints.Max(static point => point.Latitude),
            allPoints.Min(static point => point.Longitude),
            allPoints.Max(static point => point.Longitude),
            allPoints.Min(static point => point.Altitude));
    }

    private static bool HasAnyVertices(IEnumerable<ParsedCityObject> cityObjects)
    {
        return cityObjects.Any(static cityObject => cityObject.Surfaces.Any(static surface => surface.Vertices.Any()));
    }

    private static (
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        double minAltitude) MergeBounds(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? current,
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) next)
    {
        if (current is null)
        {
            return next;
        }

        return (
            Math.Min(current.Value.minLatitude, next.minLatitude),
            Math.Max(current.Value.maxLatitude, next.maxLatitude),
            Math.Min(current.Value.minLongitude, next.minLongitude),
            Math.Max(current.Value.maxLongitude, next.maxLongitude),
            Math.Min(current.Value.minAltitude, next.minAltitude));
    }
}

internal sealed record DemDiscoveryAggregation(
    CachedSourceFileDescriptor[] CachedDemSourceFiles,
    TerrainHeightTriangle[] TerrainTriangles,
    int ParsedCityObjectCount);

internal sealed record DemTerrainOverlayRegion(
    ThirdRegionalMeshCode MeshCode,
    GeographicRectangle GeographicBounds);
