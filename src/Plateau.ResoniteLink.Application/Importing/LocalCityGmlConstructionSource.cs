using System.Diagnostics;
using System.Threading.Channels;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly CoordinateReferenceSystem referenceSystem;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly TerrainTextureOverlay[] demTerrainTextureOverlays;
    private readonly ICityGmlGeometryProjector geometryProjector;
    private readonly Action<string>? progressReporter;

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
        referenceSystem = documentSet.BootstrapReferenceSystem;
        globalOriginPoint = documentSet.BootstrapGlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.progressReporter = progressReporter;
        demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();
    }

    public ResoniteConstructionMetadata Metadata { get; }

    public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LocalCartesian? globalCartesian = CreateGlobalCartesian();
        HashSet<string> emittedMaterialKeys = new(StringComparer.Ordinal);

        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
            {
                ValidateCompatibleReferenceSystem(referenceSystem, parsedCityObject.ReferenceSystem);

                foreach (ResoniteMaterialBinding material in LocalCityGmlResonitePlanBuilder.EnumerateCommonMaterials(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]).ToLegacy(),
                             referenceSystem.ToLegacy(),
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
        LocalCartesian? globalCartesian = CreateGlobalCartesian();

        foreach (SourceFilePipeline sourceFile in sourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            if (parsedSourceFile.ReferenceSystem is not null)
            {
                ValidateCompatibleReferenceSystem(referenceSystem, parsedSourceFile.ReferenceSystem);
            }

            foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                             referenceSystem,
                             globalOriginPoint,
                             globalCartesian,
                             demTerrainTextureOverlays,
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

        LocalCartesian? globalCartesian = CreateGlobalCartesian();
        Channel<ResoniteConstructionCityObject> channel = Channel.CreateBounded<ResoniteConstructionCityObject>(
            new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object producers launched: {sourceFiles.Length} file-scoped streams."));

        Task[] producers = sourceFiles
            .Select((sourceFile, index) => Task.Run(
                () => ProduceCityObjectsAsync(
                    channel.Writer,
                    sourceFile,
                    index + 1,
                    sourceFiles.Length,
                    globalCartesian,
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

    private LocalCartesian? CreateGlobalCartesian()
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
        LocalCartesian? globalCartesian,
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
                           globalCartesian,
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
        LocalCartesian? globalCartesian,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stopwatch fileStopwatch = Stopwatch.StartNew();
        int parsedCount = 0;
        int yieldedCount = 0;

        await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
        {
            parsedCount++;
            ValidateCompatibleReferenceSystem(referenceSystem, parsedCityObject.ReferenceSystem);

            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                         referenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
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
