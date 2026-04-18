using System.Globalization;

using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class LocalCityGmlDemBootstrapSupport
{
    internal static DemBootstrapAggregation AggregateDemParsedSourceFiles(
        IReadOnlyList<ParsedSourceFileResult> demParsedSourceFiles)
    {
        ArgumentNullException.ThrowIfNull(demParsedSourceFiles);

        CachedSourceFileDescriptor[] cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new CachedSourceFileDescriptor(parsed.SourceFile, parsed.CityObjects))
            .ToArray();
        TerrainHeightTriangle[] terrainTriangles = demParsedSourceFiles
            .SelectMany(static parsed => parsed.TerrainTriangles)
            .ToArray();

        return new DemBootstrapAggregation(
            cachedDemSourceFiles,
            terrainTriangles,
            demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length));
    }

    internal static TerrainHeightTriangle[] CreateTerrainHeightTriangles(
        IEnumerable<BootstrapParsedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

        List<TerrainHeightTriangle> terrainTriangles = [];
        foreach (BootstrapParsedSurface surface in cityObjects.SelectMany(static cityObject => cityObject.Surfaces))
        {
            GeodeticPoint[] vertices = surface.Vertices.ToArray();
            if (vertices.Length < 3)
            {
                continue;
            }

            GeodeticPoint origin = vertices[0];
            for (int index = 1; index + 1 < vertices.Length; index++)
            {
                terrainTriangles.Add(new TerrainHeightTriangle(origin, vertices[index], vertices[index + 1]));
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
            if (parsedSourceFile.CityObjects.Length == 0)
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

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        DemTerrainBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        ArgumentNullException.ThrowIfNull(demBounds);
        ArgumentNullException.ThrowIfNull(requestedMeshCodes);

        List<TerrainTextureOverlay> overlays = [];
        foreach (string meshCode in ExpandToThirdMeshCodes(requestedMeshCodes))
        {
            if (!PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds))
            {
                continue;
            }

            if (bounds.NorthLatitude < demBounds.SouthLatitude
                || bounds.SouthLatitude > demBounds.NorthLatitude
                || bounds.EastLongitude < demBounds.WestLongitude
                || bounds.WestLongitude > demBounds.EastLongitude)
            {
                continue;
            }

            overlays.Add(CreateDemTerrainTextureOverlay(meshCode, bounds));
        }

        if (overlays.Count > 0)
        {
            return overlays
                .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MaxLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MaxLongitude)
                .ToArray();
        }

        return
        [
            CreateDemTerrainTextureOverlay(
                "dem-fallback",
                (demBounds.SouthLatitude, demBounds.NorthLatitude, demBounds.WestLongitude, demBounds.EastLongitude)),
        ];
    }

    internal static TerrainHeightSampler? CreateTerrainHeightSampler(
        bool isGeographicReferenceSystem,
        IReadOnlyCollection<TerrainHeightTriangle> terrainTriangles,
        GeodeticPoint globalOriginPoint,
        Geocentric? geocentric)
    {
        if (!isGeographicReferenceSystem || terrainTriangles.Count == 0 || geocentric is null)
        {
            return null;
        }

        return TerrainHeightSampler.Create(
            terrainTriangles,
            globalOriginPoint,
            geocentric);
    }

    private static TerrainTextureOverlay CreateDemTerrainTextureOverlay(
        string meshCode,
        (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            UrlTemplate: LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
            FallbackUrlTemplate: LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
            ZoomLevel: LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: bounds.SouthLatitude,
                MaxLatitude: bounds.NorthLatitude,
                MinLongitude: bounds.WestLongitude,
                MaxLongitude: bounds.EastLongitude),
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
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
        IEnumerable<BootstrapParsedCityObject> cityObjects)
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

internal sealed record DemBootstrapAggregation(
    CachedSourceFileDescriptor[] CachedDemSourceFiles,
    TerrainHeightTriangle[] TerrainTriangles,
    int ParsedCityObjectCount);
