using System.Diagnostics;
using System.Threading.Channels;
using System.Collections.Concurrent;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    internal const int MaxConcurrentCityObjectProducers = 8;

    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly ICityGmlGeometryProjector geometryProjector;
    private readonly Action<string>? progressReporter;
    private readonly object referenceSystemGate = new();
    private readonly MeshCodeBounds[] requestedMeshAreas;
    private CoordinateReferenceSystem? referenceSystem;

    public LocalCityGmlConstructionSource(
        ResoniteConstructionMetadata metadata,
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        ICityGmlGeometryProjector geometryProjector,
        Action<string>? progressReporter = null)
    {
        Metadata = metadata;
        this.request = request;
        sourceFiles = documentSet.BootstrapSourceFilePipelines.ToArray();
        globalOriginPoint = documentSet.BootstrapGlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.progressReporter = progressReporter;
        requestedMeshAreas = MeshCodeBounds.CreateManyFromRequestedMeshCodes(
            Metadata.SourceDataset.RequestedMeshCodes ?? [request.MeshCode]);
    }

    public ResoniteConstructionMetadata Metadata { get; }

    public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HashSet<string> emittedMaterialKeys = new(StringComparer.Ordinal);

        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = await sourceFile.GetParseTask().WaitAsync(cancellationToken);
            CoordinateReferenceSystem resolvedReferenceSystem = ResolveReferenceSystem(parsedSourceFile);
            LocalCartesian? globalCartesian = CreateGlobalCartesian(resolvedReferenceSystem);
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile);

            foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                foreach (ResoniteMaterialBinding material in LocalCityGmlObjectProjection.EnumerateCommonMaterials(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]).ToLegacy(),
                             resolvedReferenceSystem.ToLegacy(),
                             globalOriginPoint.ToLegacy(),
                             globalCartesian,
                             demTerrainTextureOverlays,
                             terrainHeightSampler: null,
                             request,
                             emittedMaterialKeys))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return material;
                }
            }
        }
    }

    public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
    {
        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            CoordinateReferenceSystem resolvedReferenceSystem = ResolveReferenceSystem(parsedSourceFile);
            LocalCartesian? globalCartesian = CreateGlobalCartesian(resolvedReferenceSystem);
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile);

            foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                             resolvedReferenceSystem,
                             globalOriginPoint,
                             globalCartesian,
                             demTerrainTextureOverlays,
                             requestedMeshAreas,
                             request))
                {
                    yield return cityObject;
                }
            }
        }
    }

    public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object streaming pipeline starting (source_files={sourceFiles.Length})."));
        Channel<ResoniteConstructionCityObject> channel = Channel.CreateBounded<ResoniteConstructionCityObject>(
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

        await foreach (ResoniteConstructionCityObject cityObject in channel.Reader.ReadAllAsync(cancellationToken))
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
        ChannelWriter<ResoniteConstructionCityObject> writer,
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
        await foreach (ResoniteConstructionCityObject cityObject in StreamMaterializedCityObjectsAsync(
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
        ChannelWriter<ResoniteConstructionCityObject> writer,
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
        ChannelWriter<ResoniteConstructionCityObject> writer,
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

    private async IAsyncEnumerable<ResoniteConstructionCityObject> StreamMaterializedCityObjectsAsync(
        SourceFilePipeline sourceFile,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stopwatch fileStopwatch = Stopwatch.StartNew();
        ParsedSourceFileResult parsedSourceFile = await sourceFile.GetParseTask().WaitAsync(cancellationToken);
        CoordinateReferenceSystem resolvedReferenceSystem = ResolveReferenceSystem(parsedSourceFile);
        LocalCartesian? globalCartesian = CreateGlobalCartesian(resolvedReferenceSystem);
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile);
        int parsedCount = 0;
        int yieldedCount = 0;

        foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parsedCount++;

            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
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
                yield return cityObject;
            }
        }

        fileStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer materialized '{sourceFile.SourceFile.RelativePath}' "
                + $"(parsed_city_objects={parsedCount}, yielded={yieldedCount}, elapsed={fileStopwatch.Elapsed.TotalSeconds:F3}s)."));
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private CoordinateReferenceSystem ResolveReferenceSystem(ParsedSourceFileResult parsedSourceFile)
    {
        CoordinateReferenceSystem parsedReferenceSystem = parsedSourceFile.ReferenceSystem
            ?? throw new PlateauImportValidationException(
                [$"CityGML file '{parsedSourceFile.SourceFile.RelativePath}' does not declare a supported coordinate reference system."]);

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

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(ParsedSourceFileResult parsedSourceFile)
    {
        if (!string.Equals(parsedSourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || parsedSourceFile.CityObjects.Length == 0)
        {
            return [];
        }

        DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds([parsedSourceFile], fallbackBounds: null);
        if (demBounds is null)
        {
            return [];
        }

        return LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(
            demBounds,
            Metadata.SourceDataset.RequestedMeshCodes ?? [request.MeshCode]);
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
