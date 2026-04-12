using System.Diagnostics;
using System.Xml.Linq;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlResonitePlanBuilder
{
    public static Task<IResoniteConstructionSource> CreateConstructionSourceAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateConstructionSourceFactory().CreateAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    public static IResoniteConstructionSource CreateConstructionSource(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateConstructionSourceAsync(request, progressReporter).GetAwaiter().GetResult();
    }

    private static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return PlateauImportApplicationComposition.CreateConstructionSourceFactory();
    }

    private static async Task<IResoniteConstructionSource> CreateConstructionSourceCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Source is not PlateauLocalImportSource localSource || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.MissingLocalSourcePath()]);
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            localSource.LocalSourcePath!,
            cancellationToken);
        MeshCodeArea? requestedMeshArea = MeshCodeArea.TryParse(request.MeshCode);
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch scanStopwatch = Stopwatch.StartNew();
        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            datasetSource.EnumerateFiles(),
            request.MeshCode,
            request.PackageNames);
        IReadOnlyList<LocalCityGmlSourceFileDescriptor> discoveredSourceFiles = discoveryResult.SourceFiles;
        SourceFileDescriptor[] sourceFiles = discoveredSourceFiles
            .Select(static descriptor => new SourceFileDescriptor(
                descriptor.RelativePath,
                descriptor.PackageName,
                descriptor.MatchedMeshCode,
                descriptor.RequiresMeshAreaFilter))
            .ToArray();
        MeshCodeArea[] requestedMeshAreas = requestedMeshArea is null
            ? MeshCodeArea.CreateManyFromRequestedMeshCodes(discoveryResult.RequestedMeshCodes)
            : [requestedMeshArea];
        MeshCodeArea? effectiveRequestedMeshArea = MeshCodeArea.TryMerge(requestedMeshAreas);
        scanStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Debug("import", $"Scanned {sourceFiles.Length} matching CityGML files in {scanStopwatch.Elapsed.TotalSeconds:F3}s."));

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.NoMatchingFiles(request, localSource.LocalSourcePath!)]);
        }

        // Create LOD filtering strategy from the request
        LodFilteringStrategy lodFilteringStrategy = new(
            globalExcludeLodLevels: request.GlobalExcludeLodLevels,
            excludeLodByPackage: request.ExcludeLodLevelsByPackage,
            packagePatterns: request.PackagePatterns,
            includeMarkingAlways: request.IncludeMarkingAlways);

        SourceFilePipeline[] sourceFilePipelines = await CreateSourceFilePipelinesAsync(
            sourceFiles,
            datasetSource,
            requestedMeshAreas,
            progressReporter,
            lodFilteringStrategy,
            cancellationToken);

        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();

        List<SourceFilePipeline> demPipelines = sourceFilePipelines
            .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Stopwatch demStopwatch = Stopwatch.StartNew();
        ParsedSourceFileResult[] demParsedSourceFiles = demPipelines.Count == 0
            ? []
            : await Task.WhenAll(demPipelines.Select(static pipeline => pipeline.GetParseTask()));
        demStopwatch.Stop();

        CoordinateReferenceSystem? referenceSystem = null;
        foreach (SourceFilePipeline pipeline in sourceFilePipelines)
        {
            ParsedSourceFileResult parsedSourceFile = await pipeline.GetParseTask();
            if (parsedSourceFile.ReferenceSystem is null)
            {
                continue;
            }

            referenceSystem = parsedSourceFile.ReferenceSystem;
            break;
        }

        List<CachedSourceFileDescriptor> cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new CachedSourceFileDescriptor(parsed.SourceFile, parsed.CityObjects))
            .ToList();
        List<TerrainHeightTriangle> demTerrainTriangles = [];

        foreach (ParsedSourceFileResult demParsedSourceFile in demParsedSourceFiles)
        {
            demTerrainTriangles.AddRange(demParsedSourceFile.TerrainTriangles);
        }

        if (demPipelines.Count > 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Debug(
                    "import",
                    $"DEM bootstrap parsed {demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length)} city objects "
                    + $"from {demPipelines.Count} files in {demStopwatch.Elapsed.TotalSeconds:F3}s."));
        }

        if (referenceSystem is null)
        {
            throw new PlateauImportValidationException(
                [$"No CityGML coordinate reference system was resolved for mesh code '{request.MeshCode}'."]);
        }

        ResoniteLocalOrigin? resolvedLocalOrigin = ResolveLocalOrigin(effectiveRequestedMeshArea);
        if (resolvedLocalOrigin is null)
        {
            throw new PlateauImportValidationException(
                [$"The mesh code selector '{request.MeshCode}' did not resolve a supported geographic center."]);
        }

        ResoniteLocalOrigin meshCenter = resolvedLocalOrigin;
        MeshCodeArea? demTerrainBounds = referenceSystem.IsGeographic
            ? ResolveDemTerrainBounds(demParsedSourceFiles, effectiveRequestedMeshArea)
            : null;
        TerrainTextureOverlay[] terrainTextureOverlays = demTerrainBounds is not null && demPipelines.Count > 0
            ? CreateDemTerrainTextureOverlays(demTerrainBounds)
            : [];

        GeodeticPoint globalOriginPoint = new(
            meshCenter.Latitude,
            meshCenter.Longitude,
            0.0);
        TerrainHeightSampler? terrainHeightSampler = referenceSystem.IsGeographic && demTerrainTriangles.Count > 0
            ? TerrainHeightSampler.Create(demTerrainTriangles, globalOriginPoint, referenceSystem.Geocentric!)
            : null;
        progressReporter?.Invoke(
            terrainHeightSampler is null
                ? PlateauLog.Debug("import", "Terrain height sampler disabled for this dataset.")
                : PlateauLog.Debug("import", $"Terrain height sampler indexed {demTerrainTriangles.Count} DEM triangles."));
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
                TerrainTextureOverlays: terrainTextureOverlays,
                RequestedMeshCodes: discoveryResult.RequestedMeshCodes),
            Attribution: attribution,
            LocalOrigin: new ResoniteLocalOrigin(
                Latitude: globalOriginPoint.Latitude,
                Longitude: globalOriginPoint.Longitude,
                Altitude: globalOriginPoint.Altitude));
        totalStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Debug("import", $"Construction source ready in {totalStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new ConstructionSource(
            metadata,
            request,
            cachedDemSourceFiles,
            sourceFilePipelines
                .Where(static pipeline => !string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray(),
            referenceSystem,
            globalOriginPoint,
            terrainHeightSampler,
            new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()));
    }

    internal static ResoniteLocalOrigin? ResolveLocalOrigin(
        MeshCodeArea? requestedMeshArea)
    {
        return requestedMeshArea?.GetCenter();
    }

    internal static async Task<SourceFilePipeline[]> CreateSourceFilePipelinesAsync(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        return sourceFiles
            .Select(sourceFile =>
                new SourceFilePipeline(
                    sourceFile,
                    () => ParseSourceFileAsync(
                        sourceFile,
                        datasetSource,
                        requestedMeshAreas,
                        progressReporter,
                        lodFilteringStrategy,
                        cancellationToken)))
            .ToArray();
    }

    internal static async Task<ParsedSourceFileResult> ParseSourceFileAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        List<ParsedCityObject> cityObjects = [];
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsAsync(
                           sourceFile,
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           cancellationToken))
        {
            cityObjects.Add(cityObject);
        }
        fileStopwatch.Stop();

        ParsedCityObject[] cityObjectArray = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
        CoordinateReferenceSystem coordinateReferenceSystem = await ReadDocumentReferenceSystemAsync(
            datasetSource,
            sourceFile.RelativePath,
            cancellationToken);
        TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? [.. ExtractTerrainHeightTriangles(cityObjectArray)]
            : [];

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Parsed file '{sourceFile.RelativePath}' "
                + $"({sourceFile.PackageName}, {cityObjectArray.Length} city objects) "
                + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new ParsedSourceFileResult(
            sourceFile,
            cityObjectArray,
            coordinateReferenceSystem,
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    internal static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        ParsedCityObject[] cityObjects = ParseCityObjects(
            document,
            sourceFile,
            datasetSource,
            requestedMeshAreas,
            lodFilteringStrategy);

        foreach (ParsedCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }
    }

    internal static async Task<CoordinateReferenceSystem> ReadDocumentReferenceSystemAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        try
        {
            return CoordinateReferenceSystem.Parse(document);
        }
        catch (PlateauImportValidationException)
        {
            throw new PlateauImportValidationException(
                [$"CityGML file '{NormalizePath(relativePath)}' does not declare a supported coordinate reference system."]);
        }
    }
}
