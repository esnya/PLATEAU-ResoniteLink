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
        return await ReadAsync(
            request,
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            progressReporter,
            cancellationToken);
    }

    public static async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadDocumentSetCoreAsync(
            request,
            datasetContentSourceFactory,
            progressReporter,
            appearanceStoreFactory,
            lodSelector,
            cancellationToken);
    }

    internal static async Task<LocalCityGmlDocumentSet> ReadDocumentSetCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadDocumentSetCoreAsync(
            request,
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            progressReporter,
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            cancellationToken);
    }

    internal static async Task<LocalCityGmlDocumentSet> ReadDocumentSetCoreAsync(
        PlateauImportRequest request,
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        Action<string>? progressReporter = null,
        ICityGmlAppearanceStoreFactory? appearanceStoreFactory = null,
        ICityGmlLodSelector? lodSelector = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadDocumentSetCoreInternalAsync(
            request,
            datasetContentSourceFactory,
            progressReporter,
            appearanceStoreFactory ?? new CityGmlAppearanceStoreFactory(),
            lodSelector ?? new CityGmlLodSelector(),
            cancellationToken);
    }

    private static async Task<LocalCityGmlDocumentSet> ReadDocumentSetCoreInternalAsync(
        PlateauImportRequest request,
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        Action<string>? progressReporter,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(datasetContentSourceFactory);
        ArgumentNullException.ThrowIfNull(appearanceStoreFactory);
        ArgumentNullException.ThrowIfNull(lodSelector);

        if (request.Source is not PlateauLocalImportSource localSource || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.MissingLocalSourcePath()]);
        }

        IPlateauDatasetContentSource datasetSource = await datasetContentSourceFactory.CreateAsync(
            localSource.LocalSourcePath!,
            cancellationToken);
        MeshCodeBounds? requestedMeshArea =
            MeshCodeBounds.TryParse(request.MeshCode);
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch scanStopwatch = Stopwatch.StartNew();
        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            datasetSource.EnumerateFiles(),
            request.MeshCode,
            request.PackageNames);
        IReadOnlyList<LocalCityGmlSourceFileDescriptor> discoveredSourceFiles = discoveryResult.SourceFiles;
        LocalCityGmlObjectProjection.SourceFileDescriptor[] sourceFiles = discoveredSourceFiles
            .Select(static descriptor => new LocalCityGmlObjectProjection.SourceFileDescriptor(
                descriptor.RelativePath,
                descriptor.PackageName,
                descriptor.MatchedMeshCode,
                descriptor.RequiresMeshAreaFilter))
            .ToArray();
        MeshCodeBounds[] requestedMeshAreas = requestedMeshArea is null
            ? MeshCodeBounds.CreateManyFromRequestedMeshCodes(discoveryResult.RequestedMeshCodes)
            : [requestedMeshArea];
        MeshCodeBounds? effectiveRequestedMeshArea =
            MeshCodeBounds.TryMerge(requestedMeshAreas);
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

        LocalCityGmlObjectProjection.SourceFilePipeline[] sourceFilePipelines =
            await LocalCityGmlObjectProjection.CreateSourceFilePipelinesAsync(
                sourceFiles,
                datasetSource,
                requestedMeshAreas,
                progressReporter,
                lodFilteringStrategy,
                appearanceStoreFactory,
                lodSelector,
                cancellationToken);
        ParsedSourceFileResult[] demParsedSourceFiles = (await Task.WhenAll(
            sourceFilePipelines
                .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
                .Select(static pipeline => pipeline.GetParseTask())))
            .Select(ParsedSourceFileResult.FromLegacy)
            .ToArray();
        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();
        DemTerrainGeoReferencedRasterCatalog? demRasterCatalog = await DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            request.DemTextureSource,
            datasetContentSourceFactory,
            cancellationToken);
        if (request.DemTextureSource is not null && demRasterCatalog is null)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        TerrainTextureOverlay[] terrainTextureOverlays = await CreateBootstrapTerrainTextureOverlaysAsync(
            demParsedSourceFiles,
            discoveryResult.RequestedMeshCodes,
            demRasterCatalog,
            cancellationToken);
        if (request.DemTextureSource is not null
            && terrainTextureOverlays.Any(static overlay => !overlay.EnumerateGeoReferencedRasterSources().Any()))
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        ResoniteLocalOrigin? resolvedLocalOrigin =
            LocalCityGmlObjectProjection.ResolveLocalOrigin(effectiveRequestedMeshArea);
        if (resolvedLocalOrigin is null)
        {
            throw new PlateauImportValidationException(
                [$"The mesh code selector '{request.MeshCode}' did not resolve a supported geographic center."]);
        }

        LocalCityGmlObjectProjection.GeodeticPoint globalOriginPoint = new(
            resolvedLocalOrigin.Latitude,
            resolvedLocalOrigin.Longitude,
            0.0);

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
            referenceSystem: null,
            GeodeticPoint.FromLegacy(globalOriginPoint),
            terrainHeightSampler: null);
    }

    private static async Task<TerrainTextureOverlay[]> CreateBootstrapTerrainTextureOverlaysAsync(
        ParsedSourceFileResult[] demParsedSourceFiles,
        IReadOnlyList<string> requestedMeshCodes,
        DemTerrainGeoReferencedRasterCatalog? demRasterCatalog,
        CancellationToken cancellationToken)
    {
        if (demParsedSourceFiles.Length == 0)
        {
            return [];
        }

        DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            demParsedSourceFiles,
            fallbackBounds: null);
        if (demBounds is null)
        {
            return [];
        }

        return await LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlaysAsync(
            demBounds,
            requestedMeshCodes,
            demRasterCatalog,
            cancellationToken);
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
}
