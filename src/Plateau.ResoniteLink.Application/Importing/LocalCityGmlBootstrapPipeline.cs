using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class LocalCityGmlBootstrapPipeline
{
    public static async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadDocumentSetCoreAsync(request, progressReporter, cancellationToken);
    }

    internal static async Task<LocalCityGmlDocumentSet> ReadDocumentSetCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
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
        LocalCityGmlResonitePlanBuilder.MeshCodeArea? requestedMeshArea =
            LocalCityGmlResonitePlanBuilder.MeshCodeArea.TryParse(request.MeshCode);
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch scanStopwatch = Stopwatch.StartNew();
        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            datasetSource.EnumerateFiles(),
            request.MeshCode,
            request.PackageNames);
        IReadOnlyList<LocalCityGmlSourceFileDescriptor> discoveredSourceFiles = discoveryResult.SourceFiles;
        LocalCityGmlResonitePlanBuilder.SourceFileDescriptor[] sourceFiles = discoveredSourceFiles
            .Select(static descriptor => new LocalCityGmlResonitePlanBuilder.SourceFileDescriptor(
                descriptor.RelativePath,
                descriptor.PackageName,
                descriptor.MatchedMeshCode,
                descriptor.RequiresMeshAreaFilter))
            .ToArray();
        LocalCityGmlResonitePlanBuilder.MeshCodeArea[] requestedMeshAreas = requestedMeshArea is null
            ? LocalCityGmlResonitePlanBuilder.MeshCodeArea.CreateManyFromRequestedMeshCodes(discoveryResult.RequestedMeshCodes)
            : [requestedMeshArea];
        LocalCityGmlResonitePlanBuilder.MeshCodeArea? effectiveRequestedMeshArea =
            LocalCityGmlResonitePlanBuilder.MeshCodeArea.TryMerge(requestedMeshAreas);
        scanStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Info("import", $"Scanned {sourceFiles.Length} matching CityGML files in {scanStopwatch.Elapsed.TotalSeconds:F3}s."));

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.NoMatchingFiles(request, localSource.LocalSourcePath!)]);
        }

        LodFilteringStrategy lodFilteringStrategy = new(
            globalExcludeLodLevels: request.GlobalExcludeLodLevels,
            excludeLodByPackage: request.ExcludeLodLevelsByPackage,
            packagePatterns: request.PackagePatterns,
            includeMarkingAlways: request.IncludeMarkingAlways);

        LocalCityGmlResonitePlanBuilder.SourceFilePipeline[] sourceFilePipelines =
            await LocalCityGmlResonitePlanBuilder.CreateSourceFilePipelinesAsync(
                sourceFiles,
                datasetSource,
                requestedMeshAreas,
                progressReporter,
                lodFilteringStrategy,
                cancellationToken);

        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();

        List<LocalCityGmlResonitePlanBuilder.SourceFilePipeline> demPipelines = sourceFilePipelines
            .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Stopwatch demStopwatch = Stopwatch.StartNew();
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult[] demParsedSourceFiles = demPipelines.Count == 0
            ? []
            : await Task.WhenAll(demPipelines.Select(static pipeline => pipeline.GetParseTask()));
        demStopwatch.Stop();

        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem? referenceSystem = null;
        foreach (LocalCityGmlResonitePlanBuilder.SourceFilePipeline pipeline in sourceFilePipelines)
        {
            LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedSourceFile = await pipeline.GetParseTask();
            if (parsedSourceFile.ReferenceSystem is null)
            {
                continue;
            }

            referenceSystem = parsedSourceFile.ReferenceSystem;
            break;
        }

        List<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> cachedDemSourceFiles = demParsedSourceFiles
            .Where(static parsed => parsed.CityObjects.Length > 0)
            .Select(static parsed => new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(parsed.SourceFile, parsed.CityObjects))
            .ToList();
        List<LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle> demTerrainTriangles = [];

        foreach (LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult demParsedSourceFile in demParsedSourceFiles)
        {
            demTerrainTriangles.AddRange(demParsedSourceFile.TerrainTriangles);
        }

        if (demPipelines.Count > 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"DEM bootstrap parsed {demParsedSourceFiles.Sum(static parsed => parsed.CityObjects.Length)} city objects "
                    + $"from {demPipelines.Count} files in {demStopwatch.Elapsed.TotalSeconds:F3}s."));
        }

        if (referenceSystem is null)
        {
            throw new PlateauImportValidationException(
                [$"No CityGML coordinate reference system was resolved for mesh code '{request.MeshCode}'."]);
        }

        ResoniteLocalOrigin? resolvedLocalOrigin =
            LocalCityGmlResonitePlanBuilder.ResolveLocalOrigin(effectiveRequestedMeshArea);
        if (resolvedLocalOrigin is null)
        {
            throw new PlateauImportValidationException(
                [$"The mesh code selector '{request.MeshCode}' did not resolve a supported geographic center."]);
        }

        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint = new(
            resolvedLocalOrigin.Latitude,
            resolvedLocalOrigin.Longitude,
            0.0);
        LocalCityGmlResonitePlanBuilder.MeshCodeArea? demTerrainBounds = referenceSystem.IsGeographic
            ? LocalCityGmlResonitePlanBuilder.ResolveDemTerrainBounds(demParsedSourceFiles, effectiveRequestedMeshArea)
            : null;
        TerrainTextureOverlay[] terrainTextureOverlays = demTerrainBounds is not null && demPipelines.Count > 0
            ? LocalCityGmlResonitePlanBuilder.CreateDemTerrainTextureOverlays(demTerrainBounds)
            : [];

        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler =
            referenceSystem.IsGeographic && demTerrainTriangles.Count > 0
                ? LocalCityGmlResonitePlanBuilder.TerrainHeightSampler.Create(
                    demTerrainTriangles,
                    globalOriginPoint,
                    referenceSystem.Geocentric!)
                : null;
        progressReporter?.Invoke(
            terrainHeightSampler is null
                ? PlateauLog.Info("import", "Terrain height sampler disabled for this dataset.")
                : PlateauLog.Info("import", $"Terrain height sampler indexed {demTerrainTriangles.Count} DEM triangles."));

        totalStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Info("import", $"Construction source ready in {totalStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new LocalCityGmlDocumentSet(
            datasetSource,
            relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            sourceFiles
                .Select(static sourceFile => sourceFile.PackageName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static packageName => packageName, StringComparer.Ordinal)
                .ToArray(),
            terrainTextureOverlays,
            discoveryResult.RequestedMeshCodes,
            sourceFilePipelines.Select(static pipeline => new SourceFilePipeline(pipeline)).ToArray(),
            cachedDemSourceFiles.Select(CachedSourceFileDescriptor.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(referenceSystem),
            GeodeticPoint.FromLegacy(globalOriginPoint),
            TerrainHeightSampler.FromLegacy(terrainHeightSampler));
    }
}
