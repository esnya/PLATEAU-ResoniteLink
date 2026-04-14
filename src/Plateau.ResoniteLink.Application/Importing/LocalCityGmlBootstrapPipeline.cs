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
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem = await LocalCityGmlResonitePlanBuilder.ReadDocumentReferenceSystemCoreAsync(
            datasetSource,
            sourceFiles[0].RelativePath,
            cancellationToken);

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
        Stopwatch demBoundsStopwatch = Stopwatch.StartNew();
        DemBoundsScanResult demBoundsScanResult = demPipelines.Count == 0 || !referenceSystem.IsGeographic
            ? new DemBoundsScanResult(effectiveRequestedMeshArea is null ? null : DemTerrainBounds.FromLegacy(effectiveRequestedMeshArea), 0)
            : await ReadDemTerrainBoundsAsync(
                demPipelines.Select(static pipeline => new SourceFilePipeline(pipeline)).ToArray(),
                CoordinateReferenceSystem.FromLegacy(referenceSystem),
                effectiveRequestedMeshArea is null ? null : DemTerrainBounds.FromLegacy(effectiveRequestedMeshArea),
                cancellationToken);
        demBoundsStopwatch.Stop();
        TerrainTextureOverlay[] terrainTextureOverlays = demBoundsScanResult.Bounds is not null && demPipelines.Count > 0
            ? LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(demBoundsScanResult.Bounds!, discoveryResult.RequestedMeshCodes)
            : [];
        if (demPipelines.Count > 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"DEM bootstrap scanned bounds from {demPipelines.Count} files "
                    + $"(parsed_city_objects={demBoundsScanResult.ParsedCityObjectCount}) "
                    + $"in {demBoundsStopwatch.Elapsed.TotalSeconds:F3}s."));
        }
        progressReporter?.Invoke(
            demPipelines.Count == 0 || !referenceSystem.IsGeographic
                ? PlateauLog.Info("import", "Terrain height sampler disabled for this dataset.")
                : PlateauLog.Info("import", "Terrain height sampler bootstrap deferred to construction streaming."));

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
            [],
            CoordinateReferenceSystem.FromLegacy(referenceSystem),
            GeodeticPoint.FromLegacy(globalOriginPoint),
            terrainHeightSampler: null);
    }

    private static async Task<DemBoundsScanResult> ReadDemTerrainBoundsAsync(
        IReadOnlyList<SourceFilePipeline> demPipelines,
        CoordinateReferenceSystem referenceSystem,
        DemTerrainBounds? fallbackBounds,
        CancellationToken cancellationToken)
    {
        (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)? bounds = null;
        int parsedCityObjectCount = 0;

        foreach (SourceFilePipeline pipeline in demPipelines)
        {
            await foreach (BootstrapParsedCityObject cityObject in pipeline.StreamParsedCityObjectsAsync(cancellationToken))
            {
                parsedCityObjectCount++;
                ValidateCompatibleReferenceSystem(referenceSystem, cityObject.ReferenceSystem);
                bounds = MergeBounds(bounds, GetBounds(cityObject));
            }
        }

        if (bounds is null)
        {
            return new DemBoundsScanResult(fallbackBounds, parsedCityObjectCount);
        }

        return new DemBoundsScanResult(
            new DemTerrainBounds(
                bounds.Value.MinLatitude,
                bounds.Value.MaxLatitude,
                bounds.Value.MinLongitude,
                bounds.Value.MaxLongitude),
            parsedCityObjectCount);
    }

    private static (
        double MinLatitude,
        double MaxLatitude,
        double MinLongitude,
        double MaxLongitude)? MergeBounds(
        (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)? left,
        (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) right)
    {
        if (left is null)
        {
            return right;
        }

        return (
            Math.Min(left.Value.MinLatitude, right.MinLatitude),
            Math.Max(left.Value.MaxLatitude, right.MaxLatitude),
            Math.Min(left.Value.MinLongitude, right.MinLongitude),
            Math.Max(left.Value.MaxLongitude, right.MaxLongitude));
    }

    private static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) GetBounds(
        BootstrapParsedCityObject cityObject)
    {
        GeodeticPoint[] vertices = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToArray();

        return (
            vertices.Min(static point => point.Latitude),
            vertices.Max(static point => point.Latitude),
            vertices.Min(static point => point.Longitude),
            vertices.Max(static point => point.Longitude));
    }

    private static void ValidateCompatibleReferenceSystem(
        CoordinateReferenceSystem expectedReferenceSystem,
        CoordinateReferenceSystem actualReferenceSystem)
    {
        if (expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

    private sealed record DemBoundsScanResult(
        DemTerrainBounds? Bounds,
        int ParsedCityObjectCount);
}
