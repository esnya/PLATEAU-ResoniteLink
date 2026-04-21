using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IImportedSceneSource
{
    internal const int MaxConcurrentCityObjectProducers = 8;

    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly ICityGmlGeometryProjector geometryProjector;
    private readonly ICityGmlCommonMaterialEnumerator commonMaterialEnumerator;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy;
    private readonly Action<string>? progressReporter;
    private readonly object referenceSystemGate = new();
    private readonly MeshCodeBounds[] requestedMeshAreas;
    private readonly TerrainTextureOverlay[] bootstrapTerrainTextureOverlays;
    private CoordinateReferenceSystem? referenceSystem;

    public LocalCityGmlConstructionSource(
        ImportedSceneMetadata metadata,
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        ICityGmlGeometryProjector geometryProjector,
        ICityGmlCommonMaterialEnumerator commonMaterialEnumerator,
        IDemTextureSourcePolicy demTextureSourcePolicy,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(readResult);
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;
        LocalCityGmlBootstrapContext bootstrapContext = readResult.BootstrapContext;
        Metadata = metadata;
        this.request = request;
        sourceFiles = bootstrapContext.SourceFilePipelines.ToArray();
        bootstrapTerrainTextureOverlays = documentSet.TerrainTextureOverlays.ToArray();
        globalOriginPoint = bootstrapContext.GlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.commonMaterialEnumerator = commonMaterialEnumerator;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
        this.progressReporter = progressReporter;
        requestedMeshAreas = MeshCodeBounds.CreateManyFromRequestedMeshCodes(
            Metadata.SourceDataset.RequestedMeshCodes ?? [request.MeshCode]);
    }

    public ImportedSceneMetadata Metadata { get; }

    public async IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HashSet<string> emittedMaterialKeys = new(StringComparer.Ordinal);

        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            CoordinateReferenceSystem? resolvedReferenceSystem = null;
            LocalCartesian? globalCartesian = null;

            await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
            {
                IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(
                    sourceFile.SourceFile,
                    parsedCityObject);
                foreach (ResoniteMaterialBinding material in commonMaterialEnumerator.Enumerate(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                             resolvedReferenceSystem ??= ResolveReferenceSystem(parsedCityObject.ReferenceSystem),
                             globalOriginPoint,
                             globalCartesian ??= CreateGlobalCartesian(resolvedReferenceSystem),
                             demTerrainTextureOverlays,
                             requestedMeshAreas,
                             request,
                             emittedMaterialKeys))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return SceneImportContractMapper.ToContract(material);
                }
            }
        }
    }

    public IEnumerable<ImportedCityObject> ReadCityObjects()
    {
        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            CoordinateReferenceSystem resolvedReferenceSystem = ResolveReferenceSystem(parsedSourceFile);
            LocalCartesian? globalCartesian = CreateGlobalCartesian(resolvedReferenceSystem);
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile);

            foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                foreach (ResoniteConstructionCityObject cityObject in geometryProjector.ProjectCityObjects(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                             resolvedReferenceSystem,
                             globalOriginPoint,
                             globalCartesian,
                             demTerrainTextureOverlays,
                             requestedMeshAreas,
                             request))
                {
                    yield return SceneImportContractMapper.ToContract(cityObject);
                }
            }
        }
    }

    public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object streaming pipeline starting (source_files={sourceFiles.Length})."));
        Channel<ImportedCityObject> channel = Channel.CreateBounded<ImportedCityObject>(
            new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        int producerConcurrency = Math.Min(sourceFiles.Length, MaxConcurrentCityObjectProducers);
        ConcurrentQueue<(SourceFilePipeline SourceFile, int Index)> pendingSourceFiles = new(
            sourceFiles.Select((sourceFile, index) => (sourceFile, index + 1)));

        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object producers launched: {producerConcurrency} worker(s) for {sourceFiles.Length} file-scoped streams."));

        Task[] producers = Enumerable.Range(0, producerConcurrency)
            .Select(_ => Task.Run(
                () => ProduceCityObjectsUntilDrainedAsync(
                    channel.Writer,
                    pendingSourceFiles,
                    sourceFiles.Length,
                    cancellationToken),
                cancellationToken))
            .ToArray();
        Task completionTask = CompleteWriterWhenFinishedAsync(channel.Writer, producers);

        await foreach (ImportedCityObject cityObject in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return cityObject;
        }

        await completionTask;
    }

    private LocalCartesian? CreateGlobalCartesian(CoordinateReferenceSystem referenceSystem)
    {
        return referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;
    }

    private async Task ProduceCityObjectsAsync(
        ChannelWriter<ImportedCityObject> writer,
        SourceFilePipeline sourceFile,
        int fileIndex,
        int totalFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer processing source file "
                + $"{fileIndex}/{totalFiles}: '{sourceFile.SourceFile.RelativePath}'."));

        int yieldedCount = 0;
        await foreach (ImportedCityObject cityObject in StreamProjectedCityObjectsAsync(
                           sourceFile,
                           cancellationToken))
        {
            yieldedCount++;
            await writer.WriteAsync(cityObject, cancellationToken);
        }

        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer finished source file "
                + $"{fileIndex}/{totalFiles}: '{sourceFile.SourceFile.RelativePath}' "
                + $"(yielded={yieldedCount})."));
    }

    private async Task ProduceCityObjectsUntilDrainedAsync(
        ChannelWriter<ImportedCityObject> writer,
        ConcurrentQueue<(SourceFilePipeline SourceFile, int Index)> pendingSourceFiles,
        int totalFiles,
        CancellationToken cancellationToken)
    {
        while (pendingSourceFiles.TryDequeue(out (SourceFilePipeline SourceFile, int Index) nextSourceFile))
        {
            await ProduceCityObjectsAsync(
                writer,
                nextSourceFile.SourceFile,
                nextSourceFile.Index,
                totalFiles,
                cancellationToken);
        }
    }

    private static async Task CompleteWriterWhenFinishedAsync(
        ChannelWriter<ImportedCityObject> writer,
        IReadOnlyList<Task> producers)
    {
        Task allProducers = Task.WhenAll(producers);
        await allProducers.ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Exception? completionException = allProducers.Exception;
        if (completionException is AggregateException { InnerExceptions.Count: 1 } aggregateException)
        {
            completionException = aggregateException.InnerExceptions[0];
        }
        else if (allProducers.IsCanceled)
        {
            completionException = new OperationCanceledException();
        }

        writer.TryComplete(completionException);
    }

    private async IAsyncEnumerable<ImportedCityObject> StreamProjectedCityObjectsAsync(
        SourceFilePipeline sourceFile,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stopwatch fileStopwatch = Stopwatch.StartNew();
        CoordinateReferenceSystem? resolvedReferenceSystem = null;
        LocalCartesian? globalCartesian = null;
        int parsedCount = 0;
        int yieldedCount = 0;

        await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            parsedCount++;
            resolvedReferenceSystem ??= ResolveReferenceSystem(parsedCityObject.ReferenceSystem);
            globalCartesian ??= CreateGlobalCartesian(resolvedReferenceSystem);
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(
                sourceFile.SourceFile,
                parsedCityObject);

            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.ProjectCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                         resolvedReferenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
                         request))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yieldedCount++;
                yield return SceneImportContractMapper.ToContract(cityObject);
            }
        }

        fileStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer projected '{sourceFile.SourceFile.RelativePath}' "
                + $"(parsed_city_objects={parsedCount}, yielded={yieldedCount}, elapsed={fileStopwatch.Elapsed.TotalSeconds:F3}s)."));
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private CoordinateReferenceSystem ResolveReferenceSystem(CoordinateReferenceSystem parsedReferenceSystem)
    {
        lock (referenceSystemGate)
        {
            if (referenceSystem is null)
            {
                referenceSystem = parsedReferenceSystem;
                return parsedReferenceSystem;
            }

            ValidateCompatibleReferenceSystem(referenceSystem, parsedReferenceSystem);
            return referenceSystem;
        }
    }

    private CoordinateReferenceSystem ResolveReferenceSystem(ParsedSourceFileResult parsedSourceFile)
    {
        return ResolveReferenceSystem(parsedSourceFile.ReferenceSystem
            ?? throw new PlateauImportValidationException(
                [$"CityGML file '{parsedSourceFile.SourceFile.RelativePath}' does not declare a supported coordinate reference system."]));
    }

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(ParsedSourceFileResult parsedSourceFile)
    {
        if (!string.Equals(parsedSourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        TerrainTextureOverlay[] bootstrapOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile.SourceFile.PackageName);
        if (bootstrapOverlays.Length == 0)
        {
            return CreateDemTerrainTextureOverlaysFromParsedSourceFile(parsedSourceFile, preferRequestedMeshCodeSplit: true);
        }

        if (HasOverlayCoverage(parsedSourceFile, bootstrapOverlays))
        {
            return bootstrapOverlays;
        }

        return CreateDemTerrainTextureOverlaysFromParsedSourceFile(parsedSourceFile, preferRequestedMeshCodeSplit: false);
    }

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        SourceFileDescriptor sourceFile,
        BootstrapParsedCityObject parsedCityObject)
    {
        return CreateDemTerrainTextureOverlays(
            new ParsedSourceFileResult(
                sourceFile,
                [parsedCityObject],
                parsedCityObject.ReferenceSystem,
                [],
                TimeSpan.Zero));
    }

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(string packageName)
    {
        if (!string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            || bootstrapTerrainTextureOverlays.Length == 0)
        {
            return [];
        }

        return bootstrapTerrainTextureOverlays.ToArray();
    }

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlaysFromParsedSourceFile(
        ParsedSourceFileResult parsedSourceFile,
        bool preferRequestedMeshCodeSplit)
    {
        DemTerrainOverlayRegion[] overlayRegions = ResolveDemTerrainOverlayRegionsFromParsedSourceFile(
            parsedSourceFile,
            preferRequestedMeshCodeSplit);
        if (overlayRegions.Length == 0)
        {
            return [];
        }

        return demTextureSourcePolicy.ResolveAsync(
                request,
                overlayRegions,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Overlays
            .ToArray();
    }

    private bool HasOverlayCoverage(
        ParsedSourceFileResult parsedSourceFile,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        try
        {
            foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                _ = DemTerrainOverlayAssignment.SplitParsedCityObject(
                        parsedCityObject.ToLegacy(),
                        overlays,
                        requestedMeshAreas)
                    .ToArray();
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasAnyVertices(IEnumerable<BootstrapParsedCityObject> cityObjects)
    {
        return cityObjects.Any(static cityObject => cityObject.Surfaces.Any(static surface => surface.Vertices.Any()));
    }

    private async Task<TerrainTextureOverlay[]> CreateDemTerrainTextureOverlaysAsync(
        ParsedSourceFileResult parsedSourceFile,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(parsedSourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        TerrainTextureOverlay[] bootstrapOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile.SourceFile.PackageName);
        if (bootstrapOverlays.Length == 0)
        {
            return await CreateDemTerrainTextureOverlaysFromParsedSourceFileAsync(
                parsedSourceFile,
                preferRequestedMeshCodeSplit: true,
                cancellationToken);
        }

        if (HasOverlayCoverage(parsedSourceFile, bootstrapOverlays))
        {
            return bootstrapOverlays;
        }

        return await CreateDemTerrainTextureOverlaysFromParsedSourceFileAsync(
            parsedSourceFile,
            preferRequestedMeshCodeSplit: false,
            cancellationToken);
    }

    private async Task<TerrainTextureOverlay[]> CreateDemTerrainTextureOverlaysFromParsedSourceFileAsync(
        ParsedSourceFileResult parsedSourceFile,
        bool preferRequestedMeshCodeSplit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DemTerrainOverlayRegion[] overlayRegions = ResolveDemTerrainOverlayRegionsFromParsedSourceFile(
            parsedSourceFile,
            preferRequestedMeshCodeSplit);
        if (overlayRegions.Length == 0)
        {
            return [];
        }

        ResolvedDemTextureSources resolvedDemTextureSources = await demTextureSourcePolicy.ResolveAsync(
            request,
            overlayRegions,
            cancellationToken);
        return resolvedDemTextureSources.Overlays.ToArray();
    }

    private DemTerrainOverlayRegion[] ResolveDemTerrainOverlayRegionsFromParsedSourceFile(
        ParsedSourceFileResult parsedSourceFile,
        bool preferRequestedMeshCodeSplit)
    {
        DemTerrainBounds? fallbackBounds = MeshCodeBounds.TryMerge(requestedMeshAreas) is { } requestedMeshBounds
            ? new DemTerrainBounds(
                requestedMeshBounds.SouthLatitude,
                requestedMeshBounds.NorthLatitude,
                requestedMeshBounds.WestLongitude,
                requestedMeshBounds.EastLongitude)
            : null;
        IReadOnlyList<string> requestedMeshCodes =
            preferRequestedMeshCodeSplit && Metadata.SourceDataset.RequestedMeshCodes is { Count: > 0 }
                ? Metadata.SourceDataset.RequestedMeshCodes
                : [];
        if (!HasAnyVertices(parsedSourceFile.CityObjects))
        {
            return fallbackBounds is null
                ? []
                : LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(
                    fallbackBounds,
                    requestedMeshCodes);
        }

        DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            [parsedSourceFile],
            fallbackBounds);
        return demBounds is null
            ? []
            : LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(
                demBounds,
                requestedMeshCodes);
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
