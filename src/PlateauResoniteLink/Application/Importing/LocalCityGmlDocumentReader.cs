using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader
{
    private readonly Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource;
    private readonly Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore> createAppearanceStore;
    private readonly SelectCityGmlLod selectLod;

    internal LocalCityGmlDocumentReader(
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource,
        Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore> createAppearanceStore,
        SelectCityGmlLod? selectLod = null)
    {
        ArgumentNullException.ThrowIfNull(createDatasetContentSource);
        ArgumentNullException.ThrowIfNull(createAppearanceStore);

        this.createDatasetContentSource = createDatasetContentSource;
        this.createAppearanceStore = createAppearanceStore;
        this.selectLod = selectLod ?? CityGmlLodSelector.SelectPreferredSurfaceElements;
    }

    public async Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IPlateauDatasetContentSource datasetSource = await createDatasetContentSource(
            request.CityGmlLocalSourcePath,
            cancellationToken);
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
                descriptor.RequiresMeshCodeBoundsFilter,
                descriptor.SourceFileRootMeshCode))
            .ToArray();
        MeshCodeBounds[] requestedMeshCodeBounds =
            MeshCodeBounds.CreateManyFromSelectedMeshCodes(discoveryResult.SelectedMeshCodes);
        MeshCodeBounds? effectiveRequestedMeshCodeBounds =
            MeshCodeBounds.TryMerge(requestedMeshCodeBounds);
        scanStopwatch.Stop();
        logger?.WriteInformation(
            "Scanned {SourceFileCount} matching CityGML files in {ElapsedSeconds:F3}s.",
            sourceFiles.Length,
            scanStopwatch.Elapsed.TotalSeconds);

        if (sourceFiles.Length == 0)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.NoMatchingFiles(request.ToImportRequest(), request.CityGmlLocalSourcePath)]);
        }

        LodFilteringStrategy lodFilteringStrategy = new(
            globalExcludeLodLevels: request.GlobalExcludeLodLevels,
            excludeLodByPackage: request.ExcludeLodLevelsByPackage,
            packagePatterns: request.PackagePatterns,
            includeMarkingAlways: request.IncludeMarkingAlways);

        SourceFilePipeline[] sourceFilePipelines =
            await LocalCityGmlSourceFileParser.CreateSourceFilePipelinesCoreAsync(
                sourceFiles,
                datasetSource,
                requestedMeshCodeBounds,
                logger,
                lodFilteringStrategy,
                createAppearanceStore,
                cancellationToken,
                selectLod);
        List<string> relativeSourceFiles = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.RelativePath)
            .ToList();
        GeodeticCoordinate? resolvedGeodeticCenter =
            LocalCityGmlSourceFileParser.ResolveGeodeticCenter(effectiveRequestedMeshCodeBounds);
        if (resolvedGeodeticCenter is null)
        {
            throw new PlateauImportValidationException(
                [$"The mesh-code selector '{request.MeshCode}' did not resolve a supported geographic center."]);
        }

        GeodeticPoint globalOriginPoint = new(
            resolvedGeodeticCenter.Latitude,
            resolvedGeodeticCenter.Longitude,
            resolvedGeodeticCenter.Altitude);

        totalStopwatch.Stop();
        logger?.WriteInformation(
            "Imported scene source ready in {ElapsedSeconds:F3}s.",
            totalStopwatch.Elapsed.TotalSeconds);

        ImportedSceneSourceDataset documentSet = new(
            datasetSource,
            relativeSourceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            sourceFiles
                .Select(static sourceFile => sourceFile.PackageName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static packageName => packageName, StringComparer.Ordinal)
                .ToArray(),
            [],
            discoveryResult.SelectedMeshCodes);
        ImportedSceneSourceContext discoveryContext = new(
            sourceFilePipelines,
            globalOriginPoint);
        return new ImportedSceneSourceSnapshot(documentSet, discoveryContext);
    }
}
