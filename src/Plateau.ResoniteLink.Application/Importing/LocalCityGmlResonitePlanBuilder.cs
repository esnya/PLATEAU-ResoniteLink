using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Concurrent;

using GeographicLib;

using LibTessDotNet;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class LocalCityGmlResonitePlanBuilder
{
    public const string DefaultDemTerrainTexturePath = "terrain://dem/gsi-seamlessphoto";
    public const string DefaultDemTerrainTextureUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg";
    public const int DefaultDemTerrainTextureZoomLevel = 18;
    public const int DefaultDemTerrainTextureMaxSize = 4096;
    public const double DefaultGeneratedRoadMarkingWidthMeters = 0.15;
    public const double DefaultGeneratedRoadMarkingSegmentLengthMeters = 5.0;
    public static readonly ResoniteMaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = new(-1.0, -1.0);
    private static readonly Regex MeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8}|\d{6})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly ResoniteColor DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ResoniteColor DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);
    private static readonly ResoniteColor DefaultRoadMarkingColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    public static Task<IResoniteConstructionSource> CreateConstructionSourceAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateConstructionSourceCoreAsync(request, progressReporter, cancellationToken);
    }

    public static IResoniteConstructionSource CreateConstructionSource(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateConstructionSourceAsync(request, progressReporter).GetAwaiter().GetResult();
    }

    private static async Task<IResoniteConstructionSource> CreateConstructionSourceCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind != DatasetSourceKind.Local || string.IsNullOrWhiteSpace(request.LocalSourcePath))
        {
            throw new PlateauImportValidationException(
                ["Local CityGML import requires --source local and a local source path via --local-source-path."]);
        }

        string datasetRoot = PlateauDatasetPathResolver.ResolveDatasetRoot(request.LocalSourcePath);
        MeshCodeArea? requestedMeshArea = MeshCodeArea.TryParse(request.MeshCode);
        string[] sourceFileSearchCodes = GetSourceFileSearchCodes(request.MeshCode);
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch scanStopwatch = Stopwatch.StartNew();
        HashSet<string>? requestedPackageNames = request.PackageNames is null
            ? null
            : new HashSet<string>(request.PackageNames, StringComparer.OrdinalIgnoreCase);
        SourceFileDescriptor[] sourceFiles = Directory
            .EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Select(path => CreateSourceFileDescriptor(datasetRoot, path, sourceFileSearchCodes, requestedPackageNames))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(static descriptor => GetPackageSendPriority(descriptor.PackageName))
            .ThenBy(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();
        scanStopwatch.Stop();
        progressReporter?.Invoke(
            $"[import] Scanned {sourceFiles.Length} matching CityGML files in {scanStopwatch.Elapsed.TotalSeconds:F3}s.");

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [$"No local PLATEAU CityGML files were found for mesh code '{request.MeshCode}' under udx/<package>/<mesh-code>/."]);
        }

        Stopwatch bootstrapStopwatch = Stopwatch.StartNew();
        SourceFilePipeline[] sourceFilePipelines = await CreateSourceFilePipelinesAsync(
            sourceFiles,
            datasetRoot,
            requestedMeshArea,
            progressReporter,
            cancellationToken);
        bootstrapStopwatch.Stop();

        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();
        CoordinateReferenceSystem? referenceSystem = null;
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? globalBounds = null;
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? demBounds = null;

        foreach (SourceFilePipeline pipeline in sourceFilePipelines)
        {
            SourceFileMetadataResult? fileMetadata = pipeline.Metadata;
            if (fileMetadata is null || fileMetadata.ReferenceSystem is null || fileMetadata.Bounds is null)
            {
                continue;
            }

            CoordinateReferenceSystem fileReferenceSystem = fileMetadata.ReferenceSystem;
            if (referenceSystem is null)
            {
                referenceSystem = fileReferenceSystem;
            }
            else if (!referenceSystem.IsCompatibleWith(fileReferenceSystem))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{fileReferenceSystem.SrsName}'."]);
            }

            globalBounds = MergeBounds(globalBounds, fileMetadata.Bounds.Value);
            if (string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            {
                demBounds = MergeBounds(demBounds, fileMetadata.Bounds.Value);
            }
        }

        List<SourceFilePipeline> demPipelines = sourceFilePipelines
            .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Stopwatch demStopwatch = Stopwatch.StartNew();
        ParsedSourceFileResult[] demParsedSourceFiles = demPipelines.Count == 0
            ? []
            : await Task.WhenAll(demPipelines.Select(static pipeline => pipeline.ParseTask));
        demStopwatch.Stop();

        List<CachedSourceFileDescriptor> cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new CachedSourceFileDescriptor(parsed.SourceFile, parsed.CityObjects))
            .ToList();
        List<TerrainHeightTriangle> demTerrainTriangles = [];
        if (demParsedSourceFiles.Length > 0)
        {
            demBounds = null;
        }

        foreach (ParsedSourceFileResult demParsedSourceFile in demParsedSourceFiles)
        {
            if (demParsedSourceFile.Bounds is not null)
            {
                demBounds = MergeBounds(demBounds, demParsedSourceFile.Bounds.Value);
            }

            demTerrainTriangles.AddRange(demParsedSourceFile.TerrainTriangles);
        }

        progressReporter?.Invoke(
            $"[import] Bootstrapped {sourceFilePipelines.Length} files in {bootstrapStopwatch.Elapsed.TotalSeconds:F3}s.");
        if (demPipelines.Count > 0)
        {
            progressReporter?.Invoke(
                $"[import] DEM bootstrap parsed {demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length)} city objects "
                + $"from {demPipelines.Count} files in {demStopwatch.Elapsed.TotalSeconds:F3}s.");
        }

        if (globalBounds is null || referenceSystem is null)
        {
            throw new PlateauImportValidationException(
                [$"No CityGML metadata bounds were found for mesh code '{request.MeshCode}'."]);
        }

        TerrainTextureOverlay[] terrainTextureOverlays = referenceSystem.IsGeographic && demBounds is not null
            ? CreateDemTerrainTextureOverlays(demBounds.Value)
            : [];

        GeodeticPoint globalOriginPoint = CreateGlobalOrigin(globalBounds.Value, requestedMeshArea, referenceSystem.IsGeographic);
        TerrainHeightSampler? terrainHeightSampler = referenceSystem.IsGeographic && demTerrainTriangles.Count > 0
            ? TerrainHeightSampler.Create(demTerrainTriangles, globalOriginPoint, referenceSystem.Geocentric!)
            : null;
        progressReporter?.Invoke(
            terrainHeightSampler is null
                ? "[import] Terrain height sampler disabled for this dataset."
                : $"[import] Terrain height sampler indexed {demTerrainTriangles.Count} DEM triangles.");
        ResoniteAttribution attribution = PlateauResoniteAttributionFactory.Create(request);
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: sourceFiles
                    .Select(static sourceFile => sourceFile.PackageName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static packageName => packageName, StringComparer.Ordinal)
                    .ToArray(),
                SourceFiles: relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                TerrainTextureOverlays: terrainTextureOverlays),
            Attribution: attribution,
            LocalOrigin: new ResoniteLocalOrigin(
                Latitude: globalOriginPoint.Latitude,
                Longitude: globalOriginPoint.Longitude,
                Altitude: globalOriginPoint.Altitude));
        totalStopwatch.Stop();
        progressReporter?.Invoke(
            $"[import] Construction source ready in {totalStopwatch.Elapsed.TotalSeconds:F3}s.");

        return new ConstructionSource(
            metadata,
            cachedDemSourceFiles,
            sourceFilePipelines
                .Where(static pipeline => !string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            referenceSystem,
            globalOriginPoint,
            terrainHeightSampler);
    }

    private static async Task<SourceFilePipeline[]> CreateSourceFilePipelinesAsync(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        string datasetRoot,
        MeshCodeArea? requestedMeshArea,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<string, PackageParseAccumulator> packageAccumulators = new(StringComparer.OrdinalIgnoreCase);
        SourceFilePipeline[] pipelines = sourceFiles
            .Select(sourceFile =>
            {
                Task<ParsedSourceFileResult> parseTask = ParseSourceFileAsync(
                    sourceFile,
                    datasetRoot,
                    requestedMeshArea,
                    packageAccumulators,
                    progressReporter,
                    cancellationToken);
                Task<SourceFileMetadataResult> metadataTask = BootstrapSourceFileMetadataAsync(
                    sourceFile,
                    parseTask,
                    progressReporter,
                    cancellationToken);
                return new SourceFilePipeline(sourceFile, metadataTask, parseTask);
            })
            .ToArray();

        SourceFileMetadataResult[] metadataResults = await Task.WhenAll(
            pipelines.Select(static pipeline => pipeline.MetadataTask));
        for (int index = 0; index < pipelines.Length; index++)
        {
            pipelines[index] = pipelines[index] with { Metadata = metadataResults[index] };
        }

        return pipelines;
    }

    private static async Task<ParsedSourceFileResult> ParseSourceFileAsync(
        SourceFileDescriptor sourceFile,
        string datasetRoot,
        MeshCodeArea? requestedMeshArea,
        ConcurrentDictionary<string, PackageParseAccumulator> packageAccumulators,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        await using FileStream stream = new(
            sourceFile.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        ParsedCityObject[] cityObjects = ParseCityObjects(
            document,
            sourceFile,
            datasetRoot,
            requestedMeshArea);
        fileStopwatch.Stop();

        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? bounds =
            cityObjects.Length > 0
                ? GetBounds(cityObjects)
                : null;
        CoordinateReferenceSystem? fileReferenceSystem = cityObjects.Length > 0
            ? GetReferenceSystem(cityObjects)
            : null;
        TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? [.. ExtractTerrainHeightTriangles(cityObjects)]
            : [];

        packageAccumulators.AddOrUpdate(
            sourceFile.PackageName,
            _ => new PackageParseAccumulator(
                FileCount: 1,
                CityObjectCount: cityObjects.Length,
                Elapsed: fileStopwatch.Elapsed),
            (_, current) => current with
            {
                FileCount = current.FileCount + 1,
                CityObjectCount = current.CityObjectCount + cityObjects.Length,
                Elapsed = current.Elapsed + fileStopwatch.Elapsed,
            });

        progressReporter?.Invoke(
            $"[import] Parsed file '{sourceFile.RelativePath}' "
            + $"({sourceFile.PackageName}, {cityObjects.Length} city objects) "
            + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s.");

        return new ParsedSourceFileResult(
            sourceFile,
            cityObjects,
            fileReferenceSystem,
            bounds,
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    private static async Task<SourceFileMetadataResult> BootstrapSourceFileMetadataAsync(
        SourceFileDescriptor sourceFile,
        Task<ParsedSourceFileResult> parseTask,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch stopwatch = Stopwatch.StartNew();
        await using FileStream stream = new(
            sourceFile.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8 * 1024,
            useAsync: true);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        CoordinateReferenceSystem? referenceSystem = TryParseDocumentReferenceSystem(document);
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? bounds =
            TryParseDocumentBounds(document);
        stopwatch.Stop();

        if (referenceSystem is null || bounds is null)
        {
            ParsedSourceFileResult parsedSourceFile = await parseTask;
            progressReporter?.Invoke(
                $"[import] Bootstrap fallback awaited full parse for '{sourceFile.RelativePath}'.");
            return new SourceFileMetadataResult(
                sourceFile,
                parsedSourceFile.ReferenceSystem,
                parsedSourceFile.Bounds,
                stopwatch.Elapsed);
        }

        progressReporter?.Invoke(
            $"[import] Bootstrapped file '{sourceFile.RelativePath}' "
            + $"({sourceFile.PackageName}) in {stopwatch.Elapsed.TotalSeconds:F3}s.");
        return new SourceFileMetadataResult(sourceFile, referenceSystem, bounds, stopwatch.Elapsed);
    }

    private static string[] GetSourceFileSearchCodes(string meshCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        if (meshCode.Length >= 8)
        {
            return [meshCode, meshCode[..6]];
        }

        return [meshCode];
    }

    private static SourceFileDescriptor? CreateSourceFileDescriptor(
        string datasetRoot,
        string path,
        string[] sourceFileSearchCodes,
        HashSet<string>? requestedPackageNames)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(datasetRoot, path));
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "udx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!PlateauPackageCatalog.TryNormalizePackageName(segments[1], out string packageName))
        {
            return null;
        }

        if (requestedPackageNames is not null && !requestedPackageNames.Contains(packageName))
        {
            return null;
        }

        string? matchedMeshCode = MatchMeshCodeFromFileName(path, sourceFileSearchCodes)
            ?? MatchMeshCodeFromDirectoryPath(segments, sourceFileSearchCodes);
        if (matchedMeshCode is null)
        {
            return null;
        }

        return new SourceFileDescriptor(
            path,
            relativePath,
            packageName,
            matchedMeshCode.Length < sourceFileSearchCodes[0].Length);
    }

    private static CoordinateReferenceSystem? TryParseDocumentReferenceSystem(XDocument document)
    {
        XElement? envelope = document.Descendants(Gml + "Envelope").FirstOrDefault();
        string? srsName = envelope?.Attribute("srsName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(srsName))
        {
            return null;
        }

        return CoordinateReferenceSystem.Parse(srsName);
    }

    private static (
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        double minAltitude)? TryParseDocumentBounds(XDocument document)
    {
        XElement? envelope = document.Descendants(Gml + "Envelope").FirstOrDefault();
        if (envelope is null)
        {
            return null;
        }

        double[] lowerCorner = ParseDoubles(envelope.Element(Gml + "lowerCorner")?.Value ?? string.Empty);
        double[] upperCorner = ParseDoubles(envelope.Element(Gml + "upperCorner")?.Value ?? string.Empty);
        if (lowerCorner.Length < 2 || upperCorner.Length < 2)
        {
            return null;
        }

        double lowerAltitude = lowerCorner.Length >= 3 ? lowerCorner[2] : 0.0;
        double upperAltitude = upperCorner.Length >= 3 ? upperCorner[2] : lowerAltitude;
        return (
            minLatitude: Math.Min(lowerCorner[0], upperCorner[0]),
            maxLatitude: Math.Max(lowerCorner[0], upperCorner[0]),
            minLongitude: Math.Min(lowerCorner[1], upperCorner[1]),
            maxLongitude: Math.Max(lowerCorner[1], upperCorner[1]),
            minAltitude: Math.Min(lowerAltitude, upperAltitude));
    }

    private static string? MatchMeshCodeFromFileName(string path, string[] sourceFileSearchCodes)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        string[] fileMeshCodes = MeshCodeTokenRegex
            .Matches(fileNameWithoutExtension)
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return sourceFileSearchCodes
            .OrderByDescending(static code => code.Length)
            .FirstOrDefault(code => fileMeshCodes.Contains(code, StringComparer.Ordinal));
    }

    private static string? MatchMeshCodeFromDirectoryPath(string[] segments, string[] sourceFileSearchCodes)
    {
        string[] directorySegments = segments
            .Skip(2)
            .Take(Math.Max(segments.Length - 3, 0))
            .ToArray();

        return sourceFileSearchCodes
            .OrderByDescending(static code => code.Length)
            .FirstOrDefault(code => directorySegments.Contains(code, StringComparer.OrdinalIgnoreCase));
    }

    private static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        AppearanceLibrary appearanceLibrary,
        CoordinateReferenceSystem coordinateReferenceSystem,
        MeshCodeArea? requestedMeshArea)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        (XElement[] preferredSurfaceElements, int? lodLevel) = SelectPreferredLodSurfaceElements(cityObjectElement);
        ParsedSurface[] surfaces = preferredSurfaceElements
            .Select(surfaceElement => ParseSurface(surfaceElement, appearanceLibrary))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => ApplyPackageSurfaceDefaults(packageName, surface))
            .OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (requestedMeshArea is not null
            && coordinateReferenceSystem.IsGeographic
            && !IntersectsMeshCodeArea(surfaces, requestedMeshArea))
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new ParsedCityObject(slotKey, displayName!, packageName, lodLevel, surfaces, coordinateReferenceSystem);
    }

    private static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) demBounds)
    {
        double leftPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.minLongitude, DefaultDemTerrainTextureZoomLevel);
        double rightPixel = WebMercatorTileMath.LongitudeToPixelX(demBounds.maxLongitude, DefaultDemTerrainTextureZoomLevel);
        double topPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.maxLatitude, DefaultDemTerrainTextureZoomLevel);
        double bottomPixel = WebMercatorTileMath.LatitudeToPixelY(demBounds.minLatitude, DefaultDemTerrainTextureZoomLevel);

        List<TerrainTextureOverlay> overlays = [];
        int row = 0;
        for (double currentTop = topPixel; currentTop < bottomPixel - 1e-6; currentTop += DefaultDemTerrainTextureMaxSize, row++)
        {
            double currentBottom = Math.Min(currentTop + DefaultDemTerrainTextureMaxSize, bottomPixel);
            int column = 0;
            for (double currentLeft = leftPixel; currentLeft < rightPixel - 1e-6; currentLeft += DefaultDemTerrainTextureMaxSize, column++)
            {
                double currentRight = Math.Min(currentLeft + DefaultDemTerrainTextureMaxSize, rightPixel);
                overlays.Add(CreateDemTerrainTextureOverlay(row, column, currentLeft, currentRight, currentTop, currentBottom));
            }
        }

        if (overlays.Count == 0)
        {
            overlays.Add(CreateDemTerrainTextureOverlay(row: 0, column: 0, leftPixel, rightPixel, topPixel, bottomPixel));
        }

        return overlays.Count == 1
            ?
            [
                overlays[0] with { TexturePath = DefaultDemTerrainTexturePath },
            ]
            : overlays.ToArray();
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
            TexturePath: $"{DefaultDemTerrainTexturePath}/{row:D2}-{column:D2}",
            PackageName: "dem",
            UrlTemplate: DefaultDemTerrainTextureUrlTemplate,
            ZoomLevel: DefaultDemTerrainTextureZoomLevel,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: WebMercatorTileMath.PixelYToLatitude(bottomPixel, DefaultDemTerrainTextureZoomLevel),
                MaxLatitude: WebMercatorTileMath.PixelYToLatitude(topPixel, DefaultDemTerrainTextureZoomLevel),
                MinLongitude: WebMercatorTileMath.PixelXToLongitude(leftPixel, DefaultDemTerrainTextureZoomLevel),
                MaxLongitude: WebMercatorTileMath.PixelXToLongitude(rightPixel, DefaultDemTerrainTextureZoomLevel)),
            MaxTextureSize: DefaultDemTerrainTextureMaxSize);
    }

    private static (XElement[] SurfaceElements, int? LodLevel) SelectPreferredLodSurfaceElements(XElement cityObjectElement)
    {
        (XElement SurfaceElement, int? LodLevel)[] surfaces = cityObjectElement
            .Descendants()
            .Where(element => element.Name == Gml + "Polygon" || element.Name == Gml + "Triangle")
            .Select(surfaceElement => (surfaceElement, GetSurfaceLodLevel(surfaceElement, cityObjectElement)))
            .ToArray();

        int[] explicitLodLevels = surfaces
            .Where(static surface => surface.LodLevel.HasValue)
            .Select(static surface => surface.LodLevel!.Value)
            .ToArray();
        int? highestLod = explicitLodLevels.Length > 0
            ? explicitLodLevels.Max()
            : null;

        XElement[] selectedSurfaces = highestLod.HasValue
            ? surfaces
                .Where(surface => surface.LodLevel == highestLod.Value)
                .Select(static surface => surface.SurfaceElement)
                .ToArray()
            : surfaces
                .Select(static surface => surface.SurfaceElement)
                .ToArray();

        return (selectedSurfaces, highestLod);
    }

    private static ParsedSurface ApplyPackageSurfaceDefaults(string packageName, ParsedSurface surface)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(surface.TexturePath)
            ? surface with { TexturePath = DefaultDemTerrainTexturePath }
            : surface;
    }

    private static int GetPackageSendPriority(string packageName)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static int? GetSurfaceLodLevel(XElement surfaceElement, XElement cityObjectElement)
    {
        for (XElement? ancestor = surfaceElement.Parent; ancestor is not null && ancestor != cityObjectElement; ancestor = ancestor.Parent)
        {
            if (TryParseLodLevel(ancestor.Name.LocalName, out int lodLevel))
            {
                return lodLevel;
            }
        }

        return null;
    }

    private static bool TryParseLodLevel(string localName, out int lodLevel)
    {
        lodLevel = 0;
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int digitStart = 3;
        int digitLength = 0;
        while (digitStart + digitLength < localName.Length
            && char.IsDigit(localName[digitStart + digitLength]))
        {
            digitLength++;
        }

        return digitLength > 0
            && int.TryParse(
                localName.AsSpan(digitStart, digitLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lodLevel);
    }

    private static ParsedCityObject[] ParseCityObjects(
        XDocument document,
        SourceFileDescriptor sourceFile,
        string datasetRoot,
        MeshCodeArea? requestedMeshArea)
    {
        string relativeSourceFile = sourceFile.RelativePath;
        CoordinateReferenceSystem coordinateReferenceSystem = CoordinateReferenceSystem.Parse(document);
        AppearanceLibrary appearanceLibrary = AppearanceLibrary.Parse(
            document,
            Path.GetDirectoryName(sourceFile.AbsolutePath) ?? datasetRoot,
            datasetRoot);

        return document
            .Descendants(Core + "cityObjectMember")
            .Elements()
            .Select(cityObject => ParseCityObject(
                cityObject,
                sourceFile.PackageName,
                relativeSourceFile,
                appearanceLibrary,
                coordinateReferenceSystem,
                sourceFile.RequiresMeshAreaFilter ? requestedMeshArea : null))
            .Where(static cityObject => cityObject is not null)
            .Select(static cityObject => cityObject!)
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IntersectsMeshCodeArea(
        IEnumerable<ParsedSurface> surfaces,
        MeshCodeArea meshCodeArea)
    {
        List<GeodeticPoint> vertices = surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        double minLatitude = vertices.Min(static point => point.Latitude);
        double maxLatitude = vertices.Max(static point => point.Latitude);
        double minLongitude = vertices.Min(static point => point.Longitude);
        double maxLongitude = vertices.Max(static point => point.Longitude);

        return maxLatitude >= meshCodeArea.SouthLatitude
            && minLatitude <= meshCodeArea.NorthLatitude
            && maxLongitude >= meshCodeArea.WestLongitude
            && minLongitude <= meshCodeArea.EastLongitude;
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

    private static IEnumerable<TerrainHeightTriangle> ExtractTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        foreach (ParsedSurface surface in cityObjects.SelectMany(static cityObject => cityObject.Surfaces))
        {
            GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
            if (vertices.Length < 3)
            {
                continue;
            }

            GeodeticPoint origin = vertices[0];
            for (int index = 1; index + 1 < vertices.Length; index++)
            {
                yield return new TerrainHeightTriangle(origin, vertices[index], vertices[index + 1]);
            }
        }
    }

    private static ParsedCityObject ConformCityObjectToTerrain(
        ParsedCityObject parsedCityObject,
        TerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !ShouldTerrainAlignCityObject(parsedCityObject))
        {
            return parsedCityObject;
        }

        bool terrainAligned = false;
        ParsedSurface[] conformedSurfaces = new ParsedSurface[parsedCityObject.Surfaces.Length];
        for (int index = 0; index < parsedCityObject.Surfaces.Length; index++)
        {
            conformedSurfaces[index] = ConformSurfaceToTerrain(parsedCityObject.Surfaces[index], terrainHeightSampler, ref terrainAligned);
        }

        return terrainAligned
            ? parsedCityObject with
            {
                Surfaces = conformedSurfaces,
                TerrainAligned = true,
            }
            : parsedCityObject;
    }

    private static ParsedSurface ConformSurfaceToTerrain(
        ParsedSurface surface,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        ParsedRing exteriorRing = ConformRingToTerrain(surface.ExteriorRing, terrainHeightSampler, ref terrainAligned);
        ParsedRing[] interiorRings = new ParsedRing[surface.InteriorRings.Length];
        for (int index = 0; index < surface.InteriorRings.Length; index++)
        {
            interiorRings[index] = ConformRingToTerrain(surface.InteriorRings[index], terrainHeightSampler, ref terrainAligned);
        }

        return surface with
        {
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
        };
    }

    private static ParsedRing ConformRingToTerrain(
        ParsedRing ring,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        GeodeticPoint[] vertices = new GeodeticPoint[ring.Vertices.Length];
        for (int index = 0; index < ring.Vertices.Length; index++)
        {
            vertices[index] = ConformPointToTerrain(ring.Vertices[index], terrainHeightSampler, ref terrainAligned);
        }

        return ring with
        {
            Vertices = vertices,
        };
    }

    private static GeodeticPoint ConformPointToTerrain(
        GeodeticPoint point,
        TerrainHeightSampler terrainHeightSampler,
        ref bool terrainAligned)
    {
        if (!terrainHeightSampler.TrySampleHeight(point.Latitude, point.Longitude, out double altitude))
        {
            return point;
        }

        if (Math.Abs(point.Altitude - altitude) > 1e-6)
        {
            terrainAligned = true;
        }

        return new GeodeticPoint(point.Latitude, point.Longitude, altitude);
    }

    private static bool ShouldTerrainAlignCityObject(ParsedCityObject cityObject)
    {
        string packageName = cityObject.PackageName.ToLowerInvariant();
        return packageName switch
        {
            "tran" or "rwy" or "squr" or "trk" => !cityObject.LodLevel.HasValue || cityObject.LodLevel.Value < 3,
            "fld" or "ifld" or "lsld" or "luse" or "rfld" or "tnm" or "urf" or "wtr" or "wwy" => true,
            _ => false,
        };
    }

    private static ParsedSurface? ParseSurface(XElement polygonElement, AppearanceLibrary appearanceLibrary)
    {
        XElement? exteriorRing = polygonElement
            .Element(Gml + "exterior")
            ?.Element(Gml + "LinearRing");
        if (exteriorRing is null)
        {
            return null;
        }

        string polygonId = GetAttribute(polygonElement, Gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        SurfaceAppearance appearance = appearanceLibrary.Resolve(polygonId);
        ParsedRing? exteriorParsedRing = ParseRing(
            exteriorRing,
            appearance.RingUvsByRingId,
            fallbackRingId: polygonId);
        if (exteriorParsedRing is null)
        {
            return null;
        }

        ParsedRing[] interiorRings = polygonElement
            .Elements(Gml + "interior")
            .Select(interiorElement => ParseRing(
                interiorElement.Element(Gml + "LinearRing"),
                appearance.RingUvsByRingId,
                fallbackRingId: null))
            .Where(static ring => ring is not null)
            .Select(static ring => ring!)
            .ToArray();

        return new ParsedSurface(
            PolygonId: polygonId,
            Semantic: ParseSurfaceSemantic(polygonElement),
            ExteriorRing: exteriorParsedRing,
            InteriorRings: interiorRings,
            BaseColor: appearance.BaseColor,
            TexturePath: appearance.TexturePath);
    }

    private static ParsedRing? ParseRing(
        XElement? ringElement,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? ringUvsByRingId,
        string? fallbackRingId)
    {
        if (ringElement is null)
        {
            return null;
        }

        string ringId = GetAttribute(ringElement, Gml + "id")
            ?? fallbackRingId
            ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        GeodeticPoint[] vertices = ParseRingPoints(ringElement);
        if (vertices.Length < 3)
        {
            return null;
        }

        IReadOnlyList<ResoniteFloat2>? uvs = null;
        if (ringUvsByRingId is not null
            && ringUvsByRingId.TryGetValue(ringId, out IReadOnlyList<ResoniteFloat2>? ringUvs)
            && ringUvs.Count == vertices.Length)
        {
            uvs = ringUvs;
        }

        return new ParsedRing(ringId, vertices, uvs);
    }

    private static GeodeticPoint[] ParseRingPoints(XElement ringElement)
    {
        List<double> ordinates = [];
        XElement? posListElement = ringElement.Element(Gml + "posList");
        if (posListElement is not null)
        {
            ordinates.AddRange(ParseDoubles(posListElement.Value));
        }
        else
        {
            foreach (XElement posElement in ringElement.Elements(Gml + "pos"))
            {
                ordinates.AddRange(ParseDoubles(posElement.Value));
            }
        }

        List<GeodeticPoint> points = [];
        for (int index = 0; index + 2 < ordinates.Count; index += 3)
        {
            points.Add(new GeodeticPoint(ordinates[index], ordinates[index + 1], ordinates[index + 2]));
        }

        if (points.Count > 1 && AreSamePoint(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points.ToArray();
    }

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedCityObject> cityObjects)
    {
        return CreateGlobalOrigin(GetBounds(cityObjects), requestedMeshArea: null, isGeographicReferenceSystem: false);
    }

    private static GeodeticPoint CreateGlobalOrigin(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) bounds,
        MeshCodeArea? requestedMeshArea,
        bool isGeographicReferenceSystem)
    {
        if (isGeographicReferenceSystem && requestedMeshArea is not null)
        {
            return new GeodeticPoint(
                Latitude: (requestedMeshArea.SouthLatitude + requestedMeshArea.NorthLatitude) / 2.0,
                Longitude: (requestedMeshArea.WestLongitude + requestedMeshArea.EastLongitude) / 2.0,
                Altitude: bounds.minAltitude);
        }

        return new GeodeticPoint(
            Latitude: (bounds.minLatitude + bounds.maxLatitude) / 2.0,
            Longitude: (bounds.minLongitude + bounds.maxLongitude) / 2.0,
            Altitude: bounds.minAltitude);
    }

    private static CoordinateReferenceSystem GetReferenceSystem(IEnumerable<ParsedCityObject> cityObjects)
    {
        CoordinateReferenceSystem? referenceSystem = null;

        foreach (ParsedCityObject cityObject in cityObjects)
        {
            if (referenceSystem is null)
            {
                referenceSystem = cityObject.ReferenceSystem;
                continue;
            }

            if (!referenceSystem.IsCompatibleWith(cityObject.ReferenceSystem))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{cityObject.ReferenceSystem.SrsName}'."]);
            }
        }

        return referenceSystem
            ?? throw new PlateauImportValidationException(["No CityGML coordinate reference system was resolved."]);
    }

    private static ResoniteConstructionCityObject MaterializeCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject);

        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        ResoniteFloat3 slotPosition = CreateResonitePosition(
            cityObjectOrigin,
            globalOriginPoint,
            globalCartesian);

        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        List<MaterializedSurface> materializedSurfaces =
        [
            .. cityObject.Surfaces.Select(surface => MaterializeSurfaceMaterial(cityObject, cityObjectOrigin, cityObjectCartesian, surface)),
        ];

        IReadOnlyList<IGrouping<string, MaterializedSurface>> materialGroups = materializedSurfaces
            .GroupBy(
                static materializedSurface => CreateMaterialKey(
                    materializedSurface.Material.MaterialType,
                    materializedSurface.Material.TexturePath,
                    materializedSurface.Material.TextureSourceKind,
                    materializedSurface.Material.Projection,
                    materializedSurface.DepthOffset,
                    materializedSurface.Material.TextureScale,
                    materializedSurface.Surface.BaseColor),
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Count; materialIndex++)
        {
            IGrouping<string, MaterializedSurface> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (MaterializedSurface materializedSurface in materialGroup
                         .OrderBy(static surface => surface.Surface.PolygonId, StringComparer.Ordinal))
            {
                TriangulateSurface(
                    materializedSurface.Surface,
                    materializedSurface.Material,
                    cityObject.PackageName,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlay,
                    vertices,
                    indices);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            MaterializedSurface representativeSurface = materialGroup.First();
            submeshes.Add(new ResoniteMeshSubmesh(materialIndex, materialGroup.Key, indices));
            materials.Add(
                new ResoniteMaterialBinding(
                    MaterialKey: materialGroup.Key,
                    BaseColor: representativeSurface.Surface.BaseColor,
                    MaterialType: representativeSurface.Material.MaterialType,
                    TexturePath: representativeSurface.Material.TexturePath,
                    TextureSourceKind: representativeSurface.Material.TextureSourceKind,
                    Projection: representativeSurface.Material.Projection,
                    DepthOffset: representativeSurface.DepthOffset,
                    SubmeshIndices: [materialIndex],
                    TextureScale: representativeSurface.Material.TextureScale));
        }

        return new ResoniteConstructionCityObject(
            SlotKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            LodLevel: cityObject.LodLevel,
            Transform: new ResoniteTransform(slotPosition),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials);
    }

    private static GeodeticPoint GetCityObjectOrigin(ParsedCityObject cityObject)
    {
        List<GeodeticPoint> allPoints = cityObject.Surfaces.SelectMany(static surface => surface.Vertices).ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        return new GeodeticPoint(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);
    }

    private static MaterializedSurface MaterializeSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface)
    {
        if (IsGeneratedDemTexturePath(surface.TexturePath))
        {
            return new MaterializedSurface(
                surface,
                new DefaultMaterialCatalog.ResolvedMaterial(
                    ResoniteMaterialType.Standard,
                    surface.TexturePath,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null),
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(surface.TexturePath))
        {
            if (HasExplicitMaterialColor(surface.BaseColor))
            {
                return new MaterializedSurface(
                    surface,
                    new DefaultMaterialCatalog.ResolvedMaterial(
                        ResoniteMaterialType.VertexColor,
                        TexturePath: null,
                        ResoniteTextureSourceKind.Bundled,
                        ResoniteMaterialProjection.Uv,
                        Family: null,
                        TextureScale: null),
                    DepthOffset: null);
            }

            return new MaterializedSurface(
                surface with { BaseColor = DefaultVegetationMaterialColor },
                new DefaultMaterialCatalog.ResolvedMaterial(
                    ResoniteMaterialType.Standard,
                    TexturePath: null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null),
                DepthOffset: null);
        }

        if (IsGeneratedRoadMarkingSurface(surface))
        {
            return new MaterializedSurface(
                surface,
                new DefaultMaterialCatalog.ResolvedMaterial(
                    ResoniteMaterialType.VertexColor,
                    TexturePath: null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    Family: null,
                    TextureScale: null),
                DefaultTerrainAlignedMaterialDepthOffset);
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        DefaultMaterialCatalog.ResolvedMaterial resolvedMaterial = DefaultMaterialCatalog.ResolveMaterial(
            cityObject.PackageName,
            surface.TexturePath,
            preferUvProjection,
            preferUvProjection && IsBuildingPackage(cityObject.PackageName) ? BundledDefaultMaterialFamilies.Facade : null,
            $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}");
        ResoniteMaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new MaterializedSurface(surface, resolvedMaterial, depthOffset);
    }

    private static ParsedCityObject? CreateGeneratedRoadMarkingCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!string.Equals(cityObject.PackageName, "tran", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<ParsedSurface> markingSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            if (surface.TexturePath is not null)
            {
                continue;
            }

            List<ParsedSurface> generatedSurfaces = CreateGeneratedRoadMarkingSurfaces(
                surface,
                cityObjectOrigin,
                cityObjectCartesian);
            if (generatedSurfaces.Count == 0)
            {
                continue;
            }

            markingSurfaces.AddRange(generatedSurfaces);
        }

        return markingSurfaces.Count == 0
            ? null
            : cityObject with
            {
                SlotKey = $"{cityObject.SlotKey}_road_marking",
                DisplayName = $"{cityObject.DisplayName} Marking",
                Surfaces = markingSurfaces.ToArray(),
            };
    }

    private static List<ParsedSurface> CreateGeneratedRoadMarkingSurfaces(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length != 4 || surface.InteriorRings.Length != 0)
        {
            return [];
        }

        ResoniteFloat3[] positions = vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        if (normal is null || Math.Abs(normal.Y) < 0.7)
        {
            return [];
        }

        EdgePair edgePair = SelectPrimaryRoadEdgePair(vertices, positions);
        if (edgePair.Length < 1.0 || edgePair.Width < 0.3)
        {
            return [];
        }

        double markingWidth = Math.Min(DefaultGeneratedRoadMarkingWidthMeters, edgePair.Width * 0.5);
        double insetDistance = Math.Max((edgePair.Width - markingWidth) * 0.5, 0.0);
        if (insetDistance <= 1e-6)
        {
            return [];
        }

        int segmentCount = Math.Max(
            1,
            (int)Math.Ceiling(edgePair.Length / DefaultGeneratedRoadMarkingSegmentLengthMeters));
        List<ParsedSurface> segments = new(segmentCount);

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            double startT = (double)segmentIndex / segmentCount;
            double endT = (double)(segmentIndex + 1) / segmentCount;
            GeodeticPoint side0Start = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], startT);
            GeodeticPoint side0End = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], endT);
            GeodeticPoint side1Start = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], startT);
            GeodeticPoint side1End = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], endT);

            GeodeticPoint[] side0Source = [side0Start, side0End];
            GeodeticPoint[] side1Source = [side1Start, side1End];
            ResoniteFloat3[] side0Positions = side0Source
                .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            ResoniteFloat3[] side1Positions = side1Source
                .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();

            GeodeticPoint[] side0 = MoveTowardCrossSection(
                side0Source,
                side1Source,
                side0Positions,
                side1Positions,
                insetDistance);
            GeodeticPoint[] side1 = MoveTowardCrossSection(
                side1Source,
                side0Source,
                side1Positions,
                side0Positions,
                insetDistance);
            segments.Add(new ParsedSurface(
                $"{surface.PolygonId}_generated_marking_{segmentIndex:D2}",
                surface.Semantic,
                new ParsedRing(
                    $"{surface.ExteriorRing.RingId}_generated_marking_{segmentIndex:D2}",
                    [side0[0], side0[1], side1[1], side1[0]],
                    UVs: null),
                [],
                DefaultRoadMarkingColor,
                TexturePath: null));
        }

        return segments;
    }

    private static EdgePair SelectPrimaryRoadEdgePair(
        GeodeticPoint[] vertices,
        ResoniteFloat3[] positions)
    {
        double edge01 = Distance(positions[0], positions[1]);
        double edge12 = Distance(positions[1], positions[2]);
        double edge23 = Distance(positions[2], positions[3]);
        double edge30 = Distance(positions[3], positions[0]);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new EdgePair(
                [vertices[0], vertices[1]],
                [vertices[3], vertices[2]],
                pair01Length,
                (Distance(positions[0], positions[3]) + Distance(positions[1], positions[2])) * 0.5)
            : new EdgePair(
                [vertices[1], vertices[2]],
                [vertices[0], vertices[3]],
                pair12Length,
                (Distance(positions[1], positions[0]) + Distance(positions[2], positions[3])) * 0.5);
    }

    // Adapted from the "move toward counterpart edge" idea in
    // PLATEAU-SDK-for-Unity Runtime/RoadAdjust/RnmModelAdjuster.cs.
    // Upstream MIT license text is stored in THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt.
    private static GeodeticPoint[] MoveTowardCrossSection(
        IReadOnlyList<GeodeticPoint> sourceWay,
        IReadOnlyList<GeodeticPoint> targetWay,
        ResoniteFloat3[] sourcePositions,
        ResoniteFloat3[] targetPositions,
        double distance)
    {
        if (sourceWay.Count != 2
            || targetWay.Count != 2
            || sourcePositions.Length != 2
            || targetPositions.Length != 2
            || distance <= 0.0)
        {
            return sourceWay.ToArray();
        }

        GeodeticPoint[] moved = new GeodeticPoint[2];
        for (int index = 0; index < 2; index++)
        {
            GeodeticPoint source = sourceWay[index];
            GeodeticPoint target = targetWay[index];
            double actualDistance = Distance(sourcePositions[index], targetPositions[index]);
            if (actualDistance <= 1e-8)
            {
                moved[index] = source;
                continue;
            }

            double moveRatio = Math.Min(distance, actualDistance) / actualDistance;
            moved[index] = Lerp(source, target, moveRatio);
        }

        return moved;
    }

    private static void TriangulateSurface(
        ParsedSurface surface,
        DefaultMaterialCatalog.ResolvedMaterial material,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        bool useVertexColors = material.MaterialType == ResoniteMaterialType.VertexColor;
        bool useGeneratedDemUv = string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && IsGeneratedDemTexturePath(surface.TexturePath)
            && demTerrainTextureOverlay is not null;
        SurfaceUvProjection? generatedSurfaceUvProjection = !useGeneratedDemUv
            && surface.TexturePath is null
            && material.Projection == ResoniteMaterialProjection.Uv
                ? CreateGeneratedSurfaceUvProjection(
                    surface,
                    packageName,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    material.TextureScale)
                : null;
        List<TessellatedRing> tessellatedRings = CreateSurfaceTessellatedRings(
            surface,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            useGeneratedDemUv,
            generatedSurfaceUvProjection,
            useVertexColors ? surface.BaseColor : null);
        if (tessellatedRings.Count == 0)
        {
            return;
        }

        ResoniteFloat3? expectedNormal = ComputePolygonNormal(tessellatedRings[0].Vertices.Select(static vertex => vertex.Position));
        if (expectedNormal is null)
        {
            return;
        }

        (ResoniteFloat3 planeOrigin, ResoniteFloat3 basisX, ResoniteFloat3 basisY) = CreateSurfacePlane(tessellatedRings[0].Vertices);
        Tess tessellator = new();

        foreach (TessellatedRing ring in tessellatedRings)
        {
            ContourVertex[] contour = ring.Vertices
                .Select(vertex => CreateContourVertex(vertex, planeOrigin, basisX, basisY))
                .ToArray();
            tessellator.AddContour(contour, ContourOrientation.Original);
        }

        tessellator.Tessellate(
            WindingRule.EvenOdd,
            ElementType.Polygons,
            polySize: 3,
            CombineTessVertexData);

        for (int triangleIndex = 0; triangleIndex < tessellator.ElementCount; triangleIndex++)
        {
            int elementBaseIndex = triangleIndex * 3;
            int element0 = tessellator.Elements[elementBaseIndex];
            int element1 = tessellator.Elements[elementBaseIndex + 1];
            int element2 = tessellator.Elements[elementBaseIndex + 2];
            if (element0 < 0 || element1 < 0 || element2 < 0)
            {
                continue;
            }

            TessVertexPayload vertex0 = GetTessVertexPayload(tessellator, element0);
            TessVertexPayload vertex1 = GetTessVertexPayload(tessellator, element1);
            TessVertexPayload vertex2 = GetTessVertexPayload(tessellator, element2);

            ResoniteFloat3 position0 = vertex0.Position;
            ResoniteFloat3 position1 = vertex1.Position;
            ResoniteFloat3 position2 = vertex2.Position;
            ResoniteFloat2 uv0 = vertex0.UV;
            ResoniteFloat2 uv1 = vertex1.UV;
            ResoniteFloat2 uv2 = vertex2.UV;
            ResoniteColor? color0 = vertex0.Color;
            ResoniteColor? color1 = vertex1.Color;
            ResoniteColor? color2 = vertex2.Color;

            ResoniteFloat3? triangleNormal = ComputeNormal(position0, position1, position2);
            if (triangleNormal is null)
            {
                continue;
            }

            if (Dot(triangleNormal, expectedNormal) < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                (color1, color2) = (color2, color1);
                triangleNormal = ComputeNormal(position0, position1, position2);
                if (triangleNormal is null)
                {
                    continue;
                }
            }

            ResoniteFloat3? resoniteNormal = ComputeNormal(position0, position2, position1);
            if (resoniteNormal is null)
            {
                continue;
            }

            int baseIndex = vertices.Count;
            vertices.Add(new ResoniteMeshVertex(position0, resoniteNormal, uv0, color0));
            vertices.Add(new ResoniteMeshVertex(position1, resoniteNormal, uv1, color1));
            vertices.Add(new ResoniteMeshVertex(position2, resoniteNormal, uv2, color2));

            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
        }
    }

    private static List<TessellatedRing> CreateSurfaceTessellatedRings(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        bool useGeneratedDemUv,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ResoniteColor? vertexColor)
    {
        List<TessellatedRing> rings =
        [
            CreateTessellatedRing(
                surface.ExteriorRing,
                cityObjectOrigin,
                cityObjectCartesian,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlay,
                useGeneratedDemUv,
                generatedSurfaceUvProjection,
                vertexColor),
        ];
        rings.AddRange(surface.InteriorRings.Select(ring => CreateTessellatedRing(
            ring,
            cityObjectOrigin,
            cityObjectCartesian,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            useGeneratedDemUv,
            generatedSurfaceUvProjection,
            vertexColor)));
        return rings.Where(static ring => ring.Vertices.Count >= 3).ToList();
    }

    private static TessellatedRing CreateTessellatedRing(
        ParsedRing ring,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        bool useGeneratedDemUv,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ResoniteColor? vertexColor)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian),
                ring.UVs is not null && index < ring.UVs.Count
                    ? ring.UVs[index]
                    : useGeneratedDemUv
                        ? CreateGeneratedDemUv(point, demTerrainTextureOverlay!)
                        : generatedSurfaceUvProjection is not null
                            ? CreateGeneratedSurfaceUv(point, cityObjectOrigin, cityObjectCartesian, generatedSurfaceUvProjection)
                            : new ResoniteFloat2(0.0, 0.0),
                vertexColor))
            .ToArray();
        return new TessellatedRing(ring.RingId, vertices);
    }

    private static ResoniteFloat2 CreateGeneratedDemUv(
        GeodeticPoint point,
        TerrainTextureOverlay demTerrainTextureOverlay)
    {
        GeographicRectangle bounds = demTerrainTextureOverlay.GeographicBounds;
        double west = WebMercatorTileMath.LongitudeToNormalizedX(bounds.MinLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(bounds.MaxLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(bounds.MaxLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(bounds.MinLatitude);
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new ResoniteFloat2(
            Math.Clamp((pointX - west) / width, 0.0, 1.0),
            Math.Clamp((south - pointY) / height, 0.0, 1.0));
    }

    private static SurfaceUvProjection? CreateGeneratedSurfaceUvProjection(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ResoniteFloat2? textureScale)
    {
        ResoniteFloat3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length < 3)
        {
            return null;
        }

        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return null;
        }

        SurfaceUvAxes? surfaceAxes = TryCreatePathAlignedSurfaceUvAxes(packageName, positions, normal)
            ?? TryCreateSurfaceUvAxes(normal);
        if (surfaceAxes is null)
        {
            return null;
        }

        double minU = double.PositiveInfinity;
        double minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity;
        double maxV = double.NegativeInfinity;

        foreach (GeodeticPoint vertex in surface.Vertices)
        {
            ResoniteFloat3 position = CreateResonitePosition(vertex, cityObjectOrigin, cityObjectCartesian);
            double u = Dot(position, surfaceAxes.AxisU);
            double v = Dot(position, surfaceAxes.AxisV);
            minU = Math.Min(minU, u);
            minV = Math.Min(minV, v);
            maxU = Math.Max(maxU, u);
            maxV = Math.Max(maxV, v);
        }

        return new SurfaceUvProjection(
            surfaceAxes.AxisU,
            surfaceAxes.AxisV,
            minU,
            minV,
            Math.Max(maxU - minU, 1e-8),
            Math.Max(maxV - minV, 1e-8),
            surfaceAxes.AlignWidthToTextureScale
                ? AlignTextureSpan(Math.Max(maxU - minU, 1e-8), textureScale?.X)
                : Math.Max(maxU - minU, 1e-8),
            AlignTextureSpan(Math.Max(maxV - minV, 1e-8), textureScale?.Y));
    }

    private static ResoniteFloat2 CreateGeneratedSurfaceUv(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        SurfaceUvProjection projection)
    {
        ResoniteFloat3 position = CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian);
        double u = ((Dot(position, projection.AxisU) - projection.MinU) / projection.Width) * projection.AlignedWidth;
        double v = ((Dot(position, projection.AxisV) - projection.MinV) / projection.Height) * projection.AlignedHeight;
        return new ResoniteFloat2(u, v);
    }

    private static double AlignTextureSpan(double span, double? tilesPerMeter)
    {
        if (!tilesPerMeter.HasValue
            || tilesPerMeter.Value <= 1e-8
            || span <= 1e-8)
        {
            return span;
        }

        double repeats = Math.Max(1.0, Math.Round(span * tilesPerMeter.Value, MidpointRounding.AwayFromZero));
        return repeats / tilesPerMeter.Value;
    }

    private static SurfaceUvAxes? TryCreateSurfaceUvAxes(ResoniteFloat3 normal)
    {
        ResoniteFloat3 verticalAxis = new(0.0, 1.0, 0.0);
        ResoniteFloat3 facadeAxisU = Cross(verticalAxis, normal);
        if (Magnitude(facadeAxisU) >= 1e-8)
        {
            return new SurfaceUvAxes(Normalize(facadeAxisU), verticalAxis, AlignWidthToTextureScale: true);
        }

        ResoniteFloat3[] referenceAxes =
        [
            new ResoniteFloat3(1.0, 0.0, 0.0),
            new ResoniteFloat3(0.0, 0.0, 1.0),
            verticalAxis,
        ];

        foreach (ResoniteFloat3 referenceAxis in referenceAxes.OrderBy(axis => Math.Abs(Dot(normal, axis))))
        {
            ResoniteFloat3 axisU = Cross(referenceAxis, normal);
            if (Magnitude(axisU) < 1e-8)
            {
                continue;
            }

            axisU = Normalize(axisU);
            ResoniteFloat3 axisV = Cross(normal, axisU);
            if (Magnitude(axisV) < 1e-8)
            {
                continue;
            }

            return new SurfaceUvAxes(axisU, Normalize(axisV), AlignWidthToTextureScale: true);
        }

        return null;
    }

    private static SurfaceUvAxes? TryCreatePathAlignedSurfaceUvAxes(
        string packageName,
        ResoniteFloat3[] positions,
        ResoniteFloat3 normal)
    {
        if (!PlateauPackageCatalog.IsPathLikePackage(packageName)
            || positions.Length < 2
            || Math.Abs(normal.Y) < 0.7)
        {
            return null;
        }

        ResoniteFloat3 axisU = Subtract(positions[1], positions[0]);
        double axisULength = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            ResoniteFloat3 start = positions[index];
            ResoniteFloat3 end = positions[(index + 1) % positions.Length];
            ResoniteFloat3 edge = Subtract(end, start);
            ResoniteFloat3 planarEdge = Subtract(edge, Multiply(normal, Dot(edge, normal)));
            double edgeLength = Magnitude(planarEdge);
            if (edgeLength <= axisULength)
            {
                continue;
            }

            axisU = planarEdge;
            axisULength = edgeLength;
        }

        if (axisULength < 1e-8)
        {
            return null;
        }

        axisU = Normalize(axisU);
        ResoniteFloat3 axisV = Cross(normal, axisU);
        if (Magnitude(axisV) < 1e-8)
        {
            return null;
        }

        return new SurfaceUvAxes(axisU, Normalize(axisV), AlignWidthToTextureScale: true);
    }

    private static bool IsBuildingPackage(string packageName)
    {
        return string.Equals(packageName, "bldg", StringComparison.Ordinal)
            || string.Equals(packageName, "ubld", StringComparison.Ordinal);
    }

    private static bool IsGeneratedDemTexturePath(string? texturePath)
    {
        return !string.IsNullOrWhiteSpace(texturePath)
            && texturePath.StartsWith(DefaultDemTerrainTexturePath, StringComparison.Ordinal);
    }

    private static (ResoniteFloat3 Origin, ResoniteFloat3 BasisX, ResoniteFloat3 BasisY) CreateSurfacePlane(
        IReadOnlyList<TessellatedVertex> vertices)
    {
        ResoniteFloat3 origin = vertices[0].Position;
        ResoniteFloat3? normal = ComputePolygonNormal(vertices.Select(static vertex => vertex.Position))
            ?? throw new PlateauImportValidationException(["Failed to resolve a polygon plane for tessellation."]);

        ResoniteFloat3? basisX = null;
        foreach (TessellatedVertex vertex in vertices.Skip(1))
        {
            ResoniteFloat3 candidate = Subtract(vertex.Position, origin);
            if (Magnitude(candidate) >= 1e-8)
            {
                basisX = Normalize(candidate);
                break;
            }
        }

        if (basisX is null)
        {
            throw new PlateauImportValidationException(["Failed to resolve a polygon basis for tessellation."]);
        }

        ResoniteFloat3 basisY = Normalize(Cross(normal, basisX));
        return (origin, basisX, basisY);
    }

    private static ContourVertex CreateContourVertex(
        TessellatedVertex vertex,
        ResoniteFloat3 planeOrigin,
        ResoniteFloat3 basisX,
        ResoniteFloat3 basisY)
    {
        ResoniteFloat3 delta = Subtract(vertex.Position, planeOrigin);
        double projectedX = Dot(delta, basisX);
        double projectedY = Dot(delta, basisY);

        return new ContourVertex
        {
            Position = new Vec3((float)projectedX, (float)projectedY, 0.0f),
            Data = new TessVertexPayload(vertex.Position, vertex.UV, vertex.Color),
        };
    }

    private static object CombineTessVertexData(Vec3 position, object[] data, float[] weights)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        double u = 0.0;
        double v = 0.0;
        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        double a = 0.0;
        bool hasColor = false;

        for (int index = 0; index < data.Length; index++)
        {
            if (data[index] is not TessVertexPayload vertexData)
            {
                continue;
            }

            double weight = weights[index];
            x += vertexData.Position.X * weight;
            y += vertexData.Position.Y * weight;
            z += vertexData.Position.Z * weight;
            u += vertexData.UV.X * weight;
            v += vertexData.UV.Y * weight;
            if (vertexData.Color is not null)
            {
                hasColor = true;
                r += vertexData.Color.R * weight;
                g += vertexData.Color.G * weight;
                b += vertexData.Color.B * weight;
                a += vertexData.Color.A * weight;
            }
        }

        return new TessVertexPayload(
            new ResoniteFloat3(x, y, z),
            new ResoniteFloat2(u, v),
            hasColor ? new ResoniteColor(r, g, b, a) : null);
    }

    private static TessVertexPayload GetTessVertexPayload(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessVertexPayload
            ?? throw new PlateauImportValidationException(["Polygon tessellation produced a vertex without payload data."]);
    }

    private static ResoniteFloat3? ComputePolygonNormal(IEnumerable<ResoniteFloat3> positions)
    {
        ResoniteFloat3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            ResoniteFloat3 current = points[index];
            ResoniteFloat3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new ResoniteFloat3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }

    private static ResoniteFloat3? ComputeNormal(
        ResoniteFloat3 position0,
        ResoniteFloat3 position1,
        ResoniteFloat3 position2)
    {
        double ax = position1.X - position0.X;
        double ay = position1.Y - position0.Y;
        double az = position1.Z - position0.Z;
        double bx = position2.X - position0.X;
        double by = position2.Y - position0.Y;
        double bz = position2.Z - position0.Z;

        double crossX = ay * bz - az * by;
        double crossY = az * bx - ax * bz;
        double crossZ = ax * by - ay * bx;
        double magnitude = Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ));

        if (magnitude < 1e-8)
        {
            return null;
        }

        return new ResoniteFloat3(crossX / magnitude, crossY / magnitude, crossZ / magnitude);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static ResoniteFloat3 Multiply(ResoniteFloat3 vector, double scalar)
    {
        return new ResoniteFloat3(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar);
    }

    private static ResoniteFloat3 Cross(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static double Magnitude(ResoniteFloat3 vector)
    {
        return Math.Sqrt(Dot(vector, vector));
    }

    private static double Distance(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return Math.Sqrt(DistanceSquared(left, right));
    }

    private static double DistanceSquared(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
    }

    private static ResoniteFloat3 Normalize(ResoniteFloat3 vector)
    {
        double magnitude = Magnitude(vector);
        if (magnitude < 1e-8)
        {
            throw new PlateauImportValidationException(["Attempted to normalize a zero-length polygon vector."]);
        }

        return new ResoniteFloat3(
            vector.X / magnitude,
            vector.Y / magnitude,
            vector.Z / magnitude);
    }

    private static ResoniteFloat3 MapToResonitePosition((double x, double y, double z) eun)
    {
        return new ResoniteFloat3(
            X: eun.x,
            Y: eun.z,
            Z: eun.y);
    }

    private static ResoniteFloat3 CreateResonitePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        if (cartesian is null)
        {
            return new ResoniteFloat3(
                X: point.Latitude - origin.Latitude,
                Y: point.Altitude - origin.Altitude,
                Z: point.Longitude - origin.Longitude);
        }

        return MapToResonitePosition(cartesian.Forward(
            point.Latitude,
            point.Longitude,
            point.Altitude));
    }

    private static string CreateMaterialKey(
        ResoniteMaterialType materialType,
        string? texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset,
        ResoniteFloat2? textureScale,
        ResoniteColor color)
    {
        string colorKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.######},{color.G:0.######},{color.B:0.######},{color.A:0.######}");
        string depthOffsetKey = depthOffset is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{depthOffset.Factor:0.######},{depthOffset.Units:0.######}");
        string textureScaleKey = textureScale is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{textureScale.X:0.######},{textureScale.Y:0.######}");

        string materialTypeKey = materialType.ToString().ToLowerInvariant();
        return texturePath is null
            ? $"type:{materialTypeKey}|depth:{depthOffsetKey}|scale:{textureScaleKey}|color:{colorKey}"
            : $"{materialTypeKey}|{projection.ToString().ToLowerInvariant()}|{textureSourceKind.ToString().ToLowerInvariant()}-texture:{texturePath.ToLowerInvariant()}|depth:{depthOffsetKey}|scale:{textureScaleKey}|color:{colorKey}";
    }

    private static bool ShouldPreferUvProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.TexturePath is not null)
        {
            return true;
        }

        if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsBuildingPackage(packageName))
        {
            return PlateauPackageCatalog.IsPathLikePackage(packageName)
                && IsNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Wall)
        {
            return true;
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Roof
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor)
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static ParsedSurfaceSemantic ParseSurfaceSemantic(XElement polygonElement)
    {
        for (XElement? ancestor = polygonElement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ParsedSurfaceSemantic semantic = ancestor.Name.LocalName switch
            {
                "WallSurface" or "InteriorWallSurface" => ParsedSurfaceSemantic.Wall,
                "RoofSurface" => ParsedSurfaceSemantic.Roof,
                "GroundSurface" => ParsedSurfaceSemantic.Ground,
                "ClosureSurface" => ParsedSurfaceSemantic.Closure,
                "OuterCeilingSurface" => ParsedSurfaceSemantic.OuterCeiling,
                "OuterFloorSurface" => ParsedSurfaceSemantic.OuterFloor,
                _ => ParsedSurfaceSemantic.Unknown,
            };

            if (semantic != ParsedSurfaceSemantic.Unknown)
            {
                return semantic;
            }
        }

        return ParsedSurfaceSemantic.Unknown;
    }

    private static bool IsFacadeSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ResoniteFloat3[] positions = surface.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return false;
        }

        return Math.Abs(normal.Y) < 0.45;
    }

    private static bool IsNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ResoniteFloat3[] positions = surface.Vertices
            .Select(point => CreateResonitePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        ResoniteFloat3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    private static bool IsGeneratedRoadMarkingSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_marking", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static double[] ParseDoubles(string value)
    {
        return value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => double.Parse(token, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static List<ResoniteFloat2> ParseTextureCoordinates(string value)
    {
        double[] ordinates = ParseDoubles(value);
        List<ResoniteFloat2> coordinates = [];
        for (int index = 0; index + 1 < ordinates.Length; index += 2)
        {
            coordinates.Add(new ResoniteFloat2(ordinates[index], ordinates[index + 1]));
        }

        if (coordinates.Count > 1 && AreSameUV(coordinates[0], coordinates[^1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        return coordinates;
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private static GeodeticPoint Lerp(GeodeticPoint source, GeodeticPoint target, double ratio)
    {
        return new GeodeticPoint(
            source.Latitude + ((target.Latitude - source.Latitude) * ratio),
            source.Longitude + ((target.Longitude - source.Longitude) * ratio),
            source.Altitude + ((target.Altitude - source.Altitude) * ratio));
    }

    private static GeodeticPoint InterpolateAlongEdge(GeodeticPoint start, GeodeticPoint end, double ratio)
    {
        return Lerp(start, end, ratio);
    }

    private static bool AreSameUV(ResoniteFloat2 left, ResoniteFloat2 right)
    {
        return Math.Abs(left.X - right.X) < 1e-8
            && Math.Abs(left.Y - right.Y) < 1e-8;
    }

    private static bool HasExplicitMaterialColor(ResoniteColor color)
    {
        return Math.Abs(color.R - DefaultMaterialColor.R) >= 1e-8
            || Math.Abs(color.G - DefaultMaterialColor.G) >= 1e-8
            || Math.Abs(color.B - DefaultMaterialColor.B) >= 1e-8
            || Math.Abs(color.A - DefaultMaterialColor.A) >= 1e-8;
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(
            value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private sealed class ConstructionSource(
        ResoniteConstructionMetadata metadata,
        IReadOnlyList<CachedSourceFileDescriptor> demSourceFiles,
        IReadOnlyList<SourceFilePipeline> deferredSourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        TerrainHeightSampler? terrainHeightSampler)
        : IResoniteConstructionSource
    {
        private readonly TerrainTextureOverlay[] demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();

        public ResoniteConstructionMetadata Metadata { get; } = metadata;

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
        {
            LocalCartesian? globalCartesian = referenceSystem.IsGeographic
                ? new LocalCartesian(
                    globalOriginPoint.Latitude,
                    globalOriginPoint.Longitude,
                    globalOriginPoint.Altitude,
                    referenceSystem.Geocentric)
                : null;

            foreach (CachedSourceFileDescriptor sourceFile in demSourceFiles)
            {
                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    sourceFile,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler))
                {
                    yield return cityObject;
                }
            }

            foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
            {
                ParsedSourceFileResult parsedSourceFile = sourceFile.ParseTask.GetAwaiter().GetResult();
                if (parsedSourceFile.CityObjects.Length == 0)
                {
                    continue;
                }

                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    new CachedSourceFileDescriptor(parsedSourceFile.SourceFile, parsedSourceFile.CityObjects),
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler))
                {
                    yield return cityObject;
                }
            }
        }

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LocalCartesian? globalCartesian = referenceSystem.IsGeographic
                ? new LocalCartesian(
                    globalOriginPoint.Latitude,
                    globalOriginPoint.Longitude,
                    globalOriginPoint.Altitude,
                    referenceSystem.Geocentric)
                : null;

            foreach (CachedSourceFileDescriptor sourceFile in demSourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    sourceFile,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return cityObject;
                }
            }

            foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParsedSourceFileResult parsedSourceFile = await sourceFile.ParseTask.WaitAsync(cancellationToken);
                if (parsedSourceFile.CityObjects.Length == 0)
                {
                    continue;
                }

                foreach (ResoniteConstructionCityObject cityObject in MaterializeCityObjects(
                    new CachedSourceFileDescriptor(parsedSourceFile.SourceFile, parsedSourceFile.CityObjects),
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return cityObject;
                }
            }
        }
    }

    private static IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        CachedSourceFileDescriptor sourceFile,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler)
    {
        foreach (ParsedCityObject parsedCityObject in sourceFile.CityObjects)
        {
            ParsedCityObject terrainAlignedCityObject = ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler);
            foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject in SplitParsedCityObject(
                         terrainAlignedCityObject,
                         demTerrainTextureOverlays))
            {
                ResoniteConstructionCityObject cityObject = MaterializeCityObject(
                    splitCityObject.CityObject,
                    globalOriginPoint,
                    globalCartesian,
                    splitCityObject.Overlay);

                if (cityObject.Mesh.Submeshes.Count > 0)
                {
                    yield return cityObject;
                }

                GeodeticPoint markingOrigin = GetCityObjectOrigin(splitCityObject.CityObject);
                LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                    ? new LocalCartesian(
                        markingOrigin.Latitude,
                        markingOrigin.Longitude,
                        markingOrigin.Altitude,
                        splitCityObject.CityObject.ReferenceSystem.Geocentric)
                    : null;
                ParsedCityObject? roadMarkingCityObject = CreateGeneratedRoadMarkingCityObject(
                    splitCityObject.CityObject,
                    markingOrigin,
                    markingCartesian);
                if (roadMarkingCityObject is null)
                {
                    continue;
                }

                ResoniteConstructionCityObject markingObject = MaterializeCityObject(
                    roadMarkingCityObject,
                    globalOriginPoint,
                    globalCartesian,
                    splitCityObject.Overlay) with
                {
                    CollisionEnabled = false,
                };
                if (markingObject.Mesh.Submeshes.Count > 0)
                {
                    yield return markingObject;
                }
            }
        }
    }

    private static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || demTerrainTextureOverlays.Count == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        IReadOnlyList<IGrouping<string, ParsedSurface>> groups = parsedCityObject.Surfaces
            .Where(static surface => IsGeneratedDemTexturePath(surface.TexturePath))
            .Select(surface =>
            {
                TerrainTextureOverlay overlay = SelectDemTerrainTextureOverlay(surface, demTerrainTextureOverlays);
                return surface with { TexturePath = overlay.TexturePath };
            })
            .GroupBy(static surface => surface.TexturePath!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (groups.Count == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        if (groups.Count == 1 && groups[0].Count() == parsedCityObject.Surfaces.Length)
        {
            yield return (
                parsedCityObject with
                {
                    Surfaces = groups[0].ToArray(),
                },
                FindOverlay(groups[0].Key, demTerrainTextureOverlays));
            yield break;
        }

        for (int index = 0; index < groups.Count; index++)
        {
            IGrouping<string, ParsedSurface> group = groups[index];
            yield return (
                parsedCityObject with
                {
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = groups.Count > 1 ? $"{parsedCityObject.DisplayName} ({index + 1})" : parsedCityObject.DisplayName,
                    Surfaces = group.ToArray(),
                },
                FindOverlay(group.Key, demTerrainTextureOverlays));
        }
    }

    private static TerrainTextureOverlay FindOverlay(
        string texturePath,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return demTerrainTextureOverlays.First(overlay => string.Equals(overlay.TexturePath, texturePath, StringComparison.Ordinal));
    }

    private static TerrainTextureOverlay SelectDemTerrainTextureOverlay(
        ParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        double minLatitude = surface.Vertices.Min(static point => point.Latitude);
        double maxLatitude = surface.Vertices.Max(static point => point.Latitude);
        double minLongitude = surface.Vertices.Min(static point => point.Longitude);
        double maxLongitude = surface.Vertices.Max(static point => point.Longitude);

        TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            minLatitude >= overlay.GeographicBounds.MinLatitude
            && maxLatitude <= overlay.GeographicBounds.MaxLatitude
            && minLongitude >= overlay.GeographicBounds.MinLongitude
            && maxLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (containingOverlay is not null)
        {
            return containingOverlay;
        }

        double centerLatitude = (minLatitude + maxLatitude) / 2.0;
        double centerLongitude = (minLongitude + maxLongitude) / 2.0;
        TerrainTextureOverlay? centerOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            centerLatitude >= overlay.GeographicBounds.MinLatitude
            && centerLatitude <= overlay.GeographicBounds.MaxLatitude
            && centerLongitude >= overlay.GeographicBounds.MinLongitude
            && centerLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (centerOverlay is not null)
        {
            return centerOverlay;
        }

        return demTerrainTextureOverlays.First(overlay =>
            maxLatitude >= overlay.GeographicBounds.MinLatitude
            && minLatitude <= overlay.GeographicBounds.MaxLatitude
            && maxLongitude >= overlay.GeographicBounds.MinLongitude
            && minLongitude <= overlay.GeographicBounds.MaxLongitude);
    }

    private sealed record ParsedCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        int? LodLevel,
        ParsedSurface[] Surfaces,
        CoordinateReferenceSystem ReferenceSystem,
        bool TerrainAligned = false);

    private sealed record SourceFileDescriptor(
        string AbsolutePath,
        string RelativePath,
        string PackageName,
        bool RequiresMeshAreaFilter);

    private sealed record CachedSourceFileDescriptor(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects)
    {
        public string AbsolutePath => SourceFile.AbsolutePath;

        public string RelativePath => SourceFile.RelativePath;

        public string PackageName => SourceFile.PackageName;
    }

    private sealed record SourceFilePipeline(
        SourceFileDescriptor SourceFile,
        Task<SourceFileMetadataResult> MetadataTask,
        Task<ParsedSourceFileResult> ParseTask,
        SourceFileMetadataResult? Metadata = null);

    private sealed record SourceFileMetadataResult(
        SourceFileDescriptor SourceFile,
        CoordinateReferenceSystem? ReferenceSystem,
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? Bounds,
        TimeSpan Elapsed);

    private sealed record ParsedSourceFileResult(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects,
        CoordinateReferenceSystem? ReferenceSystem,
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude)? Bounds,
        TerrainHeightTriangle[] TerrainTriangles,
        TimeSpan Elapsed);

    private readonly record struct PackageParseAccumulator(
        int FileCount,
        int CityObjectCount,
        TimeSpan Elapsed);

    private sealed record MeshCodeArea(
        double SouthLatitude,
        double NorthLatitude,
        double WestLongitude,
        double EastLongitude)
    {
        public static MeshCodeArea? TryParse(string meshCode)
        {
            if (string.IsNullOrWhiteSpace(meshCode)
                || (meshCode.Length != 6 && meshCode.Length != 8)
                || !meshCode.All(char.IsDigit))
            {
                return null;
            }

            int firstLatitudeIndex = int.Parse(meshCode[..2], CultureInfo.InvariantCulture);
            int firstLongitudeIndex = int.Parse(meshCode[2..4], CultureInfo.InvariantCulture);

            double southLatitude = firstLatitudeIndex / 1.5;
            double westLongitude = 100.0 + firstLongitudeIndex;
            double latitudeSpan = 40.0 / 60.0;
            double longitudeSpan = 1.0;

            if (meshCode.Length >= 6)
            {
                int secondLatitudeIndex = int.Parse(meshCode[4].ToString(), CultureInfo.InvariantCulture);
                int secondLongitudeIndex = int.Parse(meshCode[5].ToString(), CultureInfo.InvariantCulture);
                latitudeSpan /= 8.0;
                longitudeSpan /= 8.0;
                southLatitude += secondLatitudeIndex * latitudeSpan;
                westLongitude += secondLongitudeIndex * longitudeSpan;
            }

            if (meshCode.Length >= 8)
            {
                int thirdLatitudeIndex = int.Parse(meshCode[6].ToString(), CultureInfo.InvariantCulture);
                int thirdLongitudeIndex = int.Parse(meshCode[7].ToString(), CultureInfo.InvariantCulture);
                latitudeSpan /= 10.0;
                longitudeSpan /= 10.0;
                southLatitude += thirdLatitudeIndex * latitudeSpan;
                westLongitude += thirdLongitudeIndex * longitudeSpan;
            }

            return new MeshCodeArea(
                southLatitude,
                southLatitude + latitudeSpan,
                westLongitude,
                westLongitude + longitudeSpan);
        }
    }

    private sealed record ParsedRing(
        string RingId,
        GeodeticPoint[] Vertices,
        IReadOnlyList<ResoniteFloat2>? UVs);

    private sealed record EdgePair(
        GeodeticPoint[] Side0,
        GeodeticPoint[] Side1,
        double Length,
        double Width);

    private sealed record ParsedSurface(
        string PolygonId,
        ParsedSurfaceSemantic Semantic,
        ParsedRing ExteriorRing,
        ParsedRing[] InteriorRings,
        ResoniteColor BaseColor,
        string? TexturePath)
    {
        public IEnumerable<GeodeticPoint> Vertices =>
            ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));
    }

    private sealed record MaterializedSurface(
        ParsedSurface Surface,
        DefaultMaterialCatalog.ResolvedMaterial Material,
        ResoniteMaterialDepthOffset? DepthOffset);

    private sealed record TessellatedVertex(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV,
        ResoniteColor? Color);

    private sealed record TessellatedRing(
        string RingId,
        IReadOnlyList<TessellatedVertex> Vertices);

    private sealed record TessVertexPayload(
        ResoniteFloat3 Position,
        ResoniteFloat2 UV,
        ResoniteColor? Color);

    private sealed record SurfaceUvAxes(
        ResoniteFloat3 AxisU,
        ResoniteFloat3 AxisV,
        bool AlignWidthToTextureScale);

    private sealed record SurfaceUvProjection(
        ResoniteFloat3 AxisU,
        ResoniteFloat3 AxisV,
        double MinU,
        double MinV,
        double Width,
        double Height,
        double AlignedWidth,
        double AlignedHeight);

    private enum ParsedSurfaceSemantic
    {
        Unknown = 0,
        Wall = 1,
        Roof = 2,
        Ground = 3,
        Closure = 4,
        OuterCeiling = 5,
        OuterFloor = 6,
    }

    private sealed record GeodeticPoint(
        double Latitude,
        double Longitude,
        double Altitude);

    private sealed record TerrainHeightPoint(
        double Latitude,
        double Longitude,
        double Altitude,
        double X,
        double Z);

    private sealed record TerrainHeightTriangle(
        GeodeticPoint Vertex0,
        GeodeticPoint Vertex1,
        GeodeticPoint Vertex2);

    private sealed record ProjectedTerrainHeightTriangle(
        TerrainHeightPoint Vertex0,
        TerrainHeightPoint Vertex1,
        TerrainHeightPoint Vertex2,
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    private sealed class TerrainHeightSampler
    {
        private readonly LocalCartesian cartesian;
        private readonly double cellSize;
        private readonly double maxX;
        private readonly double maxZ;
        private readonly double minX;
        private readonly double minZ;
        private readonly int maxCellSearchRadius;
        private readonly TerrainHeightPoint[] points;
        private readonly Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell;
        private readonly ProjectedTerrainHeightTriangle[] triangles;
        private readonly Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell;

        private TerrainHeightSampler(
            LocalCartesian cartesian,
            double minX,
            double maxX,
            double minZ,
            double maxZ,
            double cellSize,
            TerrainHeightPoint[] points,
            ProjectedTerrainHeightTriangle[] triangles,
            Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell,
            Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell)
        {
            this.cartesian = cartesian;
            this.cellSize = cellSize;
            this.maxX = maxX;
            this.maxZ = maxZ;
            this.minX = minX;
            this.minZ = minZ;
            maxCellSearchRadius = Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Max(maxX - minX, maxZ - minZ)
                    / Math.Max(cellSize, 1e-6)));
            this.points = points;
            this.pointsByCell = pointsByCell;
            this.triangles = triangles;
            this.trianglesByCell = trianglesByCell;
        }

        public static TerrainHeightSampler Create(
            IEnumerable<TerrainHeightTriangle> sourceTriangles,
            GeodeticPoint origin,
            Geocentric geocentric)
        {
            ArgumentNullException.ThrowIfNull(sourceTriangles);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(geocentric);

            LocalCartesian cartesian = new(
                origin.Latitude,
                origin.Longitude,
                origin.Altitude,
                geocentric);
            List<ProjectedTerrainHeightTriangle> triangles = [];
            List<TerrainHeightPoint> points = [];

            foreach (TerrainHeightTriangle triangle in sourceTriangles)
            {
                TerrainHeightPoint point0 = CreatePoint(triangle.Vertex0, cartesian);
                TerrainHeightPoint point1 = CreatePoint(triangle.Vertex1, cartesian);
                TerrainHeightPoint point2 = CreatePoint(triangle.Vertex2, cartesian);
                triangles.Add(new ProjectedTerrainHeightTriangle(
                    point0,
                    point1,
                    point2,
                    Math.Min(point0.X, Math.Min(point1.X, point2.X)),
                    Math.Max(point0.X, Math.Max(point1.X, point2.X)),
                    Math.Min(point0.Z, Math.Min(point1.Z, point2.Z)),
                    Math.Max(point0.Z, Math.Max(point1.Z, point2.Z))));
                points.Add(point0);
                points.Add(point1);
                points.Add(point2);
            }

            if (points.Count == 0)
            {
                return new TerrainHeightSampler(
                    cartesian,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0,
                    [],
                    [],
                    new Dictionary<TerrainGridCell, TerrainHeightPoint[]>(),
                    new Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]>());
            }

            double minX = points.Min(static point => point.X);
            double maxX = points.Max(static point => point.X);
            double minZ = points.Min(static point => point.Z);
            double maxZ = points.Max(static point => point.Z);
            double cellSize = ComputeCellSize(minX, maxX, minZ, maxZ, triangles.Count);

            Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell = BuildPointIndex(points, minX, minZ, cellSize);
            Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell =
                BuildTriangleIndex(triangles, minX, minZ, cellSize);

            return new TerrainHeightSampler(
                cartesian,
                minX,
                maxX,
                minZ,
                maxZ,
                cellSize,
                points.ToArray(),
                triangles.ToArray(),
                pointsByCell,
                trianglesByCell);
        }

        public bool TrySampleHeight(double latitude, double longitude, out double altitude)
        {
            (double x, double z) = Project(latitude, longitude);
            if (x < minX - 1e-6
                || x > maxX + 1e-6
                || z < minZ - 1e-6
                || z > maxZ + 1e-6)
            {
                altitude = 0.0;
                return false;
            }

            TerrainGridCell cell = GetCell(x, z);
            foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell))
            {
                if (x < triangle.MinX - 1e-6
                    || x > triangle.MaxX + 1e-6
                    || z < triangle.MinZ - 1e-6
                    || z > triangle.MaxZ + 1e-6)
                {
                    continue;
                }

                if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
                {
                    return true;
                }
            }

            foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell, radius: 1))
            {
                if (x < triangle.MinX - 1e-6
                    || x > triangle.MaxX + 1e-6
                    || z < triangle.MinZ - 1e-6
                    || z > triangle.MaxZ + 1e-6)
                {
                    continue;
                }

                if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
                {
                    return true;
                }
            }

            return TrySampleNearestPointHeight(x, z, out altitude);
        }

        private static TerrainHeightPoint CreatePoint(GeodeticPoint point, LocalCartesian cartesian)
        {
            (double x, _, double z) = cartesian.Forward(point.Latitude, point.Longitude, 0.0);
            return new TerrainHeightPoint(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                x,
                z);
        }

        private (double X, double Z) Project(double latitude, double longitude)
        {
            (double x, _, double z) = cartesian.Forward(latitude, longitude, 0.0);
            return (x, z);
        }

        private static bool TryInterpolateTriangleHeight(
            ProjectedTerrainHeightTriangle triangle,
            double x,
            double z,
            out double altitude)
        {
            double ax = triangle.Vertex0.X;
            double az = triangle.Vertex0.Z;
            double bx = triangle.Vertex1.X;
            double bz = triangle.Vertex1.Z;
            double cx = triangle.Vertex2.X;
            double cz = triangle.Vertex2.Z;

            double denominator = ((bz - cz) * (ax - cx)) + ((cx - bx) * (az - cz));
            if (Math.Abs(denominator) < 1e-8)
            {
                altitude = 0.0;
                return false;
            }

            double weight0 = (((bz - cz) * (x - cx)) + ((cx - bx) * (z - cz))) / denominator;
            double weight1 = (((cz - az) * (x - cx)) + ((ax - cx) * (z - cz))) / denominator;
            double weight2 = 1.0 - weight0 - weight1;
            if (weight0 < -1e-5 || weight1 < -1e-5 || weight2 < -1e-5)
            {
                altitude = 0.0;
                return false;
            }

            altitude = (triangle.Vertex0.Altitude * weight0)
                + (triangle.Vertex1.Altitude * weight1)
                + (triangle.Vertex2.Altitude * weight2);
            return true;
        }

        private bool TrySampleNearestPointHeight(double x, double z, out double altitude)
        {
            if (points.Length == 0)
            {
                altitude = 0.0;
                return false;
            }

            TerrainGridCell cell = GetCell(x, z);
            List<TerrainHeightPoint> candidatePoints = [];
            for (int radius = 0; radius <= maxCellSearchRadius; radius++)
            {
                AppendCandidatePoints(candidatePoints, cell, radius);
                if (candidatePoints.Count >= 4)
                {
                    break;
                }
            }

            if (candidatePoints.Count == 0)
            {
                altitude = 0.0;
                return false;
            }

            ReadOnlySpan<TerrainHeightPoint> nearestPoints = SelectNearestPoints(candidatePoints, x, z);
            if (nearestPoints.Length == 0)
            {
                altitude = 0.0;
                return false;
            }

            double weightedAltitude = 0.0;
            double weightSum = 0.0;
            foreach (TerrainHeightPoint point in nearestPoints)
            {
                double distanceSquared = SquaredDistance(point, x, z);
                if (distanceSquared < 1e-8)
                {
                    altitude = point.Altitude;
                    return true;
                }

                double weight = 1.0 / distanceSquared;
                weightedAltitude += point.Altitude * weight;
                weightSum += weight;
            }

            if (weightSum < 1e-8)
            {
                altitude = 0.0;
                return false;
            }

            altitude = weightedAltitude / weightSum;
            return true;
        }

        private static double ComputeCellSize(
            double minX,
            double maxX,
            double minZ,
            double maxZ,
            int triangleCount)
        {
            if (triangleCount <= 0)
            {
                return 1.0;
            }

            double width = Math.Max(maxX - minX, 1.0);
            double depth = Math.Max(maxZ - minZ, 1.0);
            double area = width * depth;
            double estimatedCellArea = area / triangleCount;
            return Math.Max(1.0, Math.Sqrt(Math.Max(estimatedCellArea, 1e-6)));
        }

        private static Dictionary<TerrainGridCell, TerrainHeightPoint[]> BuildPointIndex(
            IEnumerable<TerrainHeightPoint> points,
            double minX,
            double minZ,
            double cellSize)
        {
            Dictionary<TerrainGridCell, List<TerrainHeightPoint>> buckets = [];

            foreach (TerrainHeightPoint point in points)
            {
                TerrainGridCell cell = GetCell(point.X, point.Z, minX, minZ, cellSize);
                if (!buckets.TryGetValue(cell, out List<TerrainHeightPoint>? bucket))
                {
                    bucket = [];
                    buckets[cell] = bucket;
                }

                bucket.Add(point);
            }

            return buckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray());
        }

        private static Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> BuildTriangleIndex(
            IEnumerable<ProjectedTerrainHeightTriangle> triangles,
            double minX,
            double minZ,
            double cellSize)
        {
            Dictionary<TerrainGridCell, List<ProjectedTerrainHeightTriangle>> buckets = [];

            foreach (ProjectedTerrainHeightTriangle triangle in triangles)
            {
                TerrainGridCell minCell = GetCell(triangle.MinX, triangle.MinZ, minX, minZ, cellSize);
                TerrainGridCell maxCell = GetCell(triangle.MaxX, triangle.MaxZ, minX, minZ, cellSize);

                for (int x = minCell.X; x <= maxCell.X; x++)
                {
                    for (int z = minCell.Z; z <= maxCell.Z; z++)
                    {
                        TerrainGridCell cell = new(x, z);
                        if (!buckets.TryGetValue(cell, out List<ProjectedTerrainHeightTriangle>? bucket))
                        {
                            bucket = [];
                            buckets[cell] = bucket;
                        }

                        bucket.Add(triangle);
                    }
                }
            }

            return buckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToArray());
        }

        private IEnumerable<ProjectedTerrainHeightTriangle> GetCandidateTriangles(TerrainGridCell centerCell, int radius = 0)
        {
            if (radius == 0)
            {
                if (trianglesByCell.TryGetValue(centerCell, out ProjectedTerrainHeightTriangle[]? localTriangles))
                {
                    foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
                    {
                        yield return triangle;
                    }
                }

                yield break;
            }

            HashSet<ProjectedTerrainHeightTriangle> seen = [];
            foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
            {
                if (!trianglesByCell.TryGetValue(cell, out ProjectedTerrainHeightTriangle[]? localTriangles))
                {
                    continue;
                }

                foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
                {
                    if (seen.Add(triangle))
                    {
                        yield return triangle;
                    }
                }
            }
        }

        private void AppendCandidatePoints(List<TerrainHeightPoint> destination, TerrainGridCell centerCell, int radius)
        {
            foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
            {
                if (!pointsByCell.TryGetValue(cell, out TerrainHeightPoint[]? localPoints))
                {
                    continue;
                }

                destination.AddRange(localPoints);
            }
        }

        private static IEnumerable<TerrainGridCell> EnumerateCells(TerrainGridCell centerCell, int radius)
        {
            if (radius == 0)
            {
                yield return centerCell;
                yield break;
            }

            for (int x = centerCell.X - radius; x <= centerCell.X + radius; x++)
            {
                for (int z = centerCell.Z - radius; z <= centerCell.Z + radius; z++)
                {
                    if (Math.Abs(x - centerCell.X) != radius
                        && Math.Abs(z - centerCell.Z) != radius)
                    {
                        continue;
                    }

                    yield return new TerrainGridCell(x, z);
                }
            }
        }

        private static TerrainHeightPoint[] SelectNearestPoints(List<TerrainHeightPoint> candidates, double x, double z)
        {
            const int MaxNearestPoints = 4;
            TerrainHeightPoint[] nearestPoints = new TerrainHeightPoint[Math.Min(MaxNearestPoints, candidates.Count)];
            double[] nearestDistances = Enumerable.Repeat(double.PositiveInfinity, nearestPoints.Length).ToArray();
            int count = 0;

            foreach (TerrainHeightPoint candidate in candidates)
            {
                double distanceSquared = SquaredDistance(candidate, x, z);
                int insertIndex = count < nearestPoints.Length
                    ? count
                    : GetWorstIndex(nearestDistances);

                if (count == nearestPoints.Length && distanceSquared >= nearestDistances[insertIndex])
                {
                    continue;
                }

                nearestPoints[insertIndex] = candidate;
                nearestDistances[insertIndex] = distanceSquared;
                if (count < nearestPoints.Length)
                {
                    count++;
                }
            }

            if (count == nearestPoints.Length)
            {
                return nearestPoints;
            }

            TerrainHeightPoint[] trimmed = new TerrainHeightPoint[count];
            Array.Copy(nearestPoints, trimmed, count);
            return trimmed;
        }

        private static int GetWorstIndex(double[] distances)
        {
            int worstIndex = 0;
            double worstDistance = distances[0];

            for (int index = 1; index < distances.Length; index++)
            {
                if (distances[index] > worstDistance)
                {
                    worstDistance = distances[index];
                    worstIndex = index;
                }
            }

            return worstIndex;
        }

        private TerrainGridCell GetCell(double x, double z)
        {
            return GetCell(x, z, minX, minZ, cellSize);
        }

        private static TerrainGridCell GetCell(double x, double z, double minX, double minZ, double cellSize)
        {
            int cellX = (int)Math.Floor((x - minX) / cellSize);
            int cellZ = (int)Math.Floor((z - minZ) / cellSize);
            return new TerrainGridCell(cellX, cellZ);
        }

        private static double SquaredDistance(TerrainHeightPoint point, double x, double z)
        {
            double dx = point.X - x;
            double dz = point.Z - z;
            return (dx * dx) + (dz * dz);
        }
    }

    private readonly record struct TerrainGridCell(int X, int Z);

    private sealed record CoordinateReferenceSystem(
        string SrsName,
        Geocentric? Geocentric,
        string CompatibilityKey)
    {
        public bool IsGeographic => Geocentric is not null;

        public bool IsCompatibleWith(CoordinateReferenceSystem other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return string.Equals(CompatibilityKey, other.CompatibilityKey, StringComparison.Ordinal);
        }

        public static CoordinateReferenceSystem Parse(XDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            string? srsName = document
                .Descendants(Gml + "Envelope")
                .Attributes("srsName")
                .Select(static attribute => attribute.Value.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return Parse(srsName);
        }

        public static CoordinateReferenceSystem Parse(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return new CoordinateReferenceSystem("local-cartesian", null, "local-cartesian");
            }

            (Geocentric geocentric, string compatibilityKey) = ResolveGeocentric(srsName);
            return new CoordinateReferenceSystem(srsName, geocentric, compatibilityKey);
        }

        private static (Geocentric Geocentric, string CompatibilityKey) ResolveGeocentric(string srsName)
        {
            if (srsName.EndsWith("/6697", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6697", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/6668", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6668", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/6696", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6696", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/4979", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4979", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/4326", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4326", StringComparison.OrdinalIgnoreCase))
            {
                return (Geocentric.WGS84, "wgs84");
            }

            throw new PlateauImportValidationException(
                [$"Unsupported CityGML CRS '{srsName}'. Only geographic 3D CRS values currently used by PLATEAU are supported."]);
        }
    }

    private sealed record SurfaceAppearance(
        ResoniteColor BaseColor,
        string? TexturePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>>? RingUvsByRingId);

    private sealed record TextureAssignment(
        string TexturePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResoniteFloat2>> RingCoordinates);

    private sealed class AppearanceLibrary
    {
        private readonly Dictionary<string, ResoniteColor> colorsByPolygonId;
        private readonly Dictionary<string, TextureAssignment> texturesByPolygonId;

        private AppearanceLibrary(
            Dictionary<string, ResoniteColor> colorsByPolygonId,
            Dictionary<string, TextureAssignment> texturesByPolygonId)
        {
            this.colorsByPolygonId = colorsByPolygonId;
            this.texturesByPolygonId = texturesByPolygonId;
        }

        public static AppearanceLibrary Parse(
            XDocument document,
            string sourceDirectory,
            string datasetRoot)
        {
            Dictionary<string, ResoniteColor> colorsByPolygonId = new(StringComparer.Ordinal);
            Dictionary<string, TextureAssignment> texturesByPolygonId = new(StringComparer.Ordinal);

            foreach (XElement textureElement in document.Descendants(App + "ParameterizedTexture"))
            {
                string? imageUri = textureElement.Element(App + "imageURI")?.Value.Trim();
                string? resolvedTexturePath = ResolveTexturePath(sourceDirectory, datasetRoot, imageUri);
                if (resolvedTexturePath is null)
                {
                    continue;
                }

                foreach (XElement targetElement in textureElement.Elements(App + "target"))
                {
                    string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                    if (string.IsNullOrWhiteSpace(polygonId))
                    {
                        continue;
                    }

                    Dictionary<string, IReadOnlyList<ResoniteFloat2>> ringCoordinates = new(StringComparer.Ordinal);
                    foreach (XElement textureCoordinatesElement in targetElement.Descendants(App + "textureCoordinates"))
                    {
                        string? ringId = StripReferencePrefix(textureCoordinatesElement.Attribute("ring")?.Value);
                        if (string.IsNullOrWhiteSpace(ringId))
                        {
                            continue;
                        }

                        List<ResoniteFloat2> coordinates = ParseTextureCoordinates(textureCoordinatesElement.Value);
                        if (coordinates.Count > 0)
                        {
                            ringCoordinates[ringId] = coordinates;
                        }
                    }

                    texturesByPolygonId[polygonId] = new TextureAssignment(resolvedTexturePath, ringCoordinates);
                }
            }

            foreach (XElement materialElement in document.Descendants(App + "X3DMaterial"))
            {
                ResoniteColor diffuseColor = ParseColor(
                    materialElement.Element(App + "diffuseColor")?.Value,
                    DefaultMaterialColor);

                foreach (XElement targetElement in materialElement.Elements(App + "target"))
                {
                    string? polygonId = StripReferencePrefix(targetElement.Attribute("uri")?.Value);
                    if (!string.IsNullOrWhiteSpace(polygonId))
                    {
                        colorsByPolygonId[polygonId] = diffuseColor;
                    }
                }
            }

            return new AppearanceLibrary(colorsByPolygonId, texturesByPolygonId);
        }

        public SurfaceAppearance Resolve(string polygonId)
        {
            ResoniteColor baseColor = colorsByPolygonId.TryGetValue(polygonId, out ResoniteColor? color)
                ? color
                : DefaultMaterialColor;

            if (!texturesByPolygonId.TryGetValue(polygonId, out TextureAssignment? textureAssignment))
            {
                return new SurfaceAppearance(baseColor, null, null);
            }

            return new SurfaceAppearance(baseColor, textureAssignment.TexturePath, textureAssignment.RingCoordinates);
        }

        private static string? ResolveTexturePath(
            string sourceDirectory,
            string datasetRoot,
            string? imageUri)
        {
            if (string.IsNullOrWhiteSpace(imageUri))
            {
                return null;
            }

            string absoluteTexturePath = Path.GetFullPath(Path.Combine(sourceDirectory, imageUri));
            if (!File.Exists(absoluteTexturePath))
            {
                return null;
            }

            return NormalizePath(Path.GetRelativePath(datasetRoot, absoluteTexturePath));
        }

        private static string? StripReferencePrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.StartsWith('#')
                ? value[1..]
                : value;
        }

        private static ResoniteColor ParseColor(string? value, ResoniteColor fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            double[] values = ParseDoubles(value);
            if (values.Length < 3)
            {
                return fallback;
            }

            return new ResoniteColor(
                R: values[0],
                G: values[1],
                B: values[2],
                A: values.Length >= 4 ? values[3] : 1.0);
        }
    }
}
