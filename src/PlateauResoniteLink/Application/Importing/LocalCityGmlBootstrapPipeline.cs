using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlBootstrapPipeline
{
    public static async Task<LocalCityGmlDocumentReadResult> ReadAsync(
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
            appearanceStoreFactory,
            lodSelector,
            progressReporter,
            cancellationToken);
    }

    internal static async Task<LocalCityGmlDocumentReadResult> ReadDocumentSetCoreAsync(
        PlateauImportRequest request,
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadDocumentSetCoreInternalAsync(
            request,
            datasetContentSourceFactory,
            progressReporter,
            appearanceStoreFactory,
            lodSelector,
            cancellationToken);
    }

    private static async Task<LocalCityGmlDocumentReadResult> ReadDocumentSetCoreInternalAsync(
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

        if (request.Source is not LocalDatasetLocation localSource || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
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
        SourceFileDescriptor[] sourceFiles = discoveredSourceFiles
            .Select(static descriptor => new SourceFileDescriptor(
                descriptor.RelativePath,
                descriptor.PackageName,
                descriptor.MatchedMeshCode,
                descriptor.RequiresMeshAreaFilter))
            .ToArray();
        MeshCodeBounds[] requestedMeshAreas = requestedMeshArea is null
            ? MeshCodeBounds.CreateManyFromRequestedMeshCodes(discoveryResult.SelectedMeshCodes)
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

        SourceFilePipeline[] sourceFilePipelines =
            await LocalCityGmlObjectProjection.CreateSourceFilePipelinesCoreAsync(
                sourceFiles,
                datasetSource,
                requestedMeshAreas,
                progressReporter,
                lodFilteringStrategy,
                appearanceStoreFactory,
                lodSelector,
                cancellationToken);
        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();
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

        LocalCityGmlDocumentSet documentSet = new(
            datasetSource,
            relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            sourceFiles
                .Select(static sourceFile => sourceFile.PackageName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static packageName => packageName, StringComparer.Ordinal)
                .ToArray(),
            [],
            discoveryResult.SelectedMeshCodes);
        LocalCityGmlBootstrapContext bootstrapContext = new(
            sourceFilePipelines,
            new GeodeticPoint(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude));
        return new LocalCityGmlDocumentReadResult(documentSet, bootstrapContext);
    }
}
