using System.Globalization;

using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class LocalCityGmlDemBootstrapSupport
{
    internal static DemBootstrapAggregation AggregateDemParsedSourceFiles(
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult> demParsedSourceFiles)
    {
        ArgumentNullException.ThrowIfNull(demParsedSourceFiles);

        LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor[] cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(parsed.SourceFile, parsed.CityObjects))
            .ToArray();
        LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle[] terrainTriangles = demParsedSourceFiles
            .SelectMany(static parsed => parsed.TerrainTriangles)
            .ToArray();

        return new DemBootstrapAggregation(
            cachedDemSourceFiles,
            terrainTriangles,
            demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length));
    }

    internal static LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle[] CreateTerrainHeightTriangles(
        IEnumerable<LocalCityGmlResonitePlanBuilder.ParsedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

        List<LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle> terrainTriangles = [];
        foreach (LocalCityGmlResonitePlanBuilder.ParsedSurface surface in cityObjects.SelectMany(static cityObject => cityObject.Surfaces))
        {
            LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices = surface.Vertices.ToArray();
            if (vertices.Length < 3)
            {
                continue;
            }

            LocalCityGmlResonitePlanBuilder.GeodeticPoint origin = vertices[0];
            for (int index = 1; index + 1 < vertices.Length; index++)
            {
                terrainTriangles.Add(new LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle(origin, vertices[index], vertices[index + 1]));
            }
        }

        return terrainTriangles.ToArray();
    }

    internal static LocalCityGmlResonitePlanBuilder.MeshCodeArea? ResolveDemTerrainBounds(
        IEnumerable<LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult> demParsedSourceFiles,
        LocalCityGmlResonitePlanBuilder.MeshCodeArea? fallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(demParsedSourceFiles);

        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? bounds = null;
        foreach (LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedSourceFile in demParsedSourceFiles)
        {
            if (parsedSourceFile.CityObjects.Length == 0)
            {
                continue;
            }

            bounds = MergeBounds(bounds, GetBounds(parsedSourceFile.CityObjects));
        }

        return bounds is null
            ? fallbackBounds
            : new LocalCityGmlResonitePlanBuilder.MeshCodeArea(
                bounds.Value.minLatitude,
                bounds.Value.maxLatitude,
                bounds.Value.minLongitude,
                bounds.Value.maxLongitude);
    }

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        LocalCityGmlResonitePlanBuilder.MeshCodeArea demBounds)
    {
        ArgumentNullException.ThrowIfNull(demBounds);

        double leftPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.WestLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double rightPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.EastLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double topPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.NorthLatitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double bottomPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.SouthLatitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);

        List<TerrainTextureOverlay> overlays = [];
        int row = 0;
        for (double currentTop = topPixel; currentTop < bottomPixel - 1e-6; currentTop += LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, row++)
        {
            double currentBottom = Math.Min(currentTop + LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, bottomPixel);
            int column = 0;
            for (double currentLeft = leftPixel; currentLeft < rightPixel - 1e-6; currentLeft += LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, column++)
            {
                double currentRight = Math.Min(currentLeft + LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, rightPixel);
                overlays.Add(CreateDemTerrainTextureOverlay(row, column, currentLeft, currentRight, currentTop, currentBottom));
            }
        }

        if (overlays.Count == 0)
        {
            overlays.Add(CreateDemTerrainTextureOverlay(row: 0, column: 0, leftPixel, rightPixel, topPixel, bottomPixel));
        }

        return overlays.ToArray();
    }

    internal static LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? CreateTerrainHeightSampler(
        bool isGeographicReferenceSystem,
        IReadOnlyCollection<LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle> terrainTriangles,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        Geocentric? geocentric)
    {
        if (!isGeographicReferenceSystem || terrainTriangles.Count == 0 || geocentric is null)
        {
            return null;
        }

        return LocalCityGmlResonitePlanBuilder.TerrainHeightSampler.Create(terrainTriangles, globalOriginPoint, geocentric);
    }

    private static TerrainTextureOverlay CreateDemTerrainTextureOverlay(
        int row,
        int column,
        double leftPixel,
        double rightPixel,
        double topPixel,
        double bottomPixel)
    {
        return new TerrainTextureOverlay(
            TexturePath: CreateDemTerrainTexturePath(leftPixel, rightPixel, topPixel, bottomPixel),
            PackageName: "dem",
            UrlTemplate: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureUrlTemplate,
            ZoomLevel: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: WebMercatorTileMath.PixelYToLatitude(bottomPixel, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel),
                MaxLatitude: WebMercatorTileMath.PixelYToLatitude(topPixel, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel),
                MinLongitude: WebMercatorTileMath.PixelXToLongitude(leftPixel, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel),
                MaxLongitude: WebMercatorTileMath.PixelXToLongitude(rightPixel, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel)),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);
    }

    private static string CreateDemTerrainTexturePath(
        double leftPixel,
        double rightPixel,
        double topPixel,
        double bottomPixel)
    {
        int globalRow = (int)Math.Floor(topPixel / LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);
        int globalColumn = (int)Math.Floor(leftPixel / LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);
        long leftKey = (long)Math.Round(leftPixel * 1000.0, MidpointRounding.AwayFromZero);
        long rightKey = (long)Math.Round(rightPixel * 1000.0, MidpointRounding.AwayFromZero);
        long topKey = (long)Math.Round(topPixel * 1000.0, MidpointRounding.AwayFromZero);
        long bottomKey = (long)Math.Round(bottomPixel * 1000.0, MidpointRounding.AwayFromZero);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath}/{globalRow:D5}-{globalColumn:D5}-{leftKey:D12}-{rightKey:D12}-{topKey:D12}-{bottomKey:D12}");
    }

    private static (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) GetBounds(
        IEnumerable<LocalCityGmlResonitePlanBuilder.ParsedCityObject> cityObjects)
    {
        List<LocalCityGmlResonitePlanBuilder.GeodeticPoint> allPoints = cityObjects
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
    LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor[] CachedDemSourceFiles,
    LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle[] TerrainTriangles,
    int ParsedCityObjectCount);
