using System.Threading.Channels;

using LocalCartesian = GeographicLib.LocalCartesian;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    private readonly PlateauImportRequest request;
    private readonly IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> demSourceFiles;
    private readonly IReadOnlyList<LocalCityGmlResonitePlanBuilder.SourceFilePipeline> deferredSourceFiles;
    private readonly LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem;
    private readonly LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint;
    private readonly LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler;
    private readonly TerrainTextureOverlay[] demTerrainTextureOverlays;
    private readonly ICityGmlGeometryProjector geometryProjector;

    public LocalCityGmlConstructionSource(
        ResoniteConstructionMetadata metadata,
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        ICityGmlGeometryProjector geometryProjector)
    {
        Metadata = metadata;
        this.request = request;
        demSourceFiles = documentSet.CachedDemSourceFiles;
        deferredSourceFiles = documentSet.SourceFilePipelines
            .Where(static pipeline => !string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        referenceSystem = documentSet.ReferenceSystem;
        globalOriginPoint = documentSet.GlobalOriginPoint;
        terrainHeightSampler = documentSet.TerrainHeightSampler;
        this.geometryProjector = geometryProjector;
        demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();
    }

    public ResoniteConstructionMetadata Metadata { get; }

    public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
    {
        LocalCartesian? globalCartesian = referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;

        foreach (LocalCityGmlResonitePlanBuilder.SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(
                    sourceFile.SourceFile,
                    parsedSourceFile.CityObjects),
                referenceSystem,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlays,
                terrainHeightSampler,
                request,
                static parsedCityObject => !IsTerrainDependentCityObject(parsedCityObject)))
            {
                yield return cityObject;
            }
        }

        foreach (LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor sourceFile in demSourceFiles)
        {
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                sourceFile,
                referenceSystem,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlays,
                terrainHeightSampler,
                request))
            {
                yield return cityObject;
            }
        }

        foreach (LocalCityGmlResonitePlanBuilder.SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(
                    sourceFile.SourceFile,
                    parsedSourceFile.CityObjects),
                referenceSystem,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlays,
                terrainHeightSampler,
                request,
                IsTerrainDependentCityObject))
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

        foreach (LocalCityGmlResonitePlanBuilder.SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = sourceFile.GetParseTask();
        }

        Channel<ResoniteConstructionCityObject> channel = Channel.CreateBounded<ResoniteConstructionCityObject>(
            new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        Task[] producers =
        [
            Task.Run(
                () => ProduceDeferredCityObjectsAsync(
                    channel.Writer,
                    geometryProjector,
                    deferredSourceFiles,
                    referenceSystem,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler,
                    request,
                    static parsedCityObject => !IsTerrainDependentCityObject(parsedCityObject),
                    cancellationToken),
                cancellationToken),
            Task.Run(
                () => ProduceCachedCityObjectsAsync(
                    channel.Writer,
                    geometryProjector,
                    demSourceFiles,
                    referenceSystem,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler,
                    request,
                    predicate: null,
                    cancellationToken),
                cancellationToken),
            Task.Run(
                () => ProduceDeferredCityObjectsAsync(
                    channel.Writer,
                    geometryProjector,
                    deferredSourceFiles,
                    referenceSystem,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    terrainHeightSampler,
                    request,
                    IsTerrainDependentCityObject,
                    cancellationToken),
                cancellationToken),
        ];
        Task completionTask = CompleteWriterWhenFinishedAsync(channel.Writer, producers);

        await foreach (ResoniteConstructionCityObject cityObject in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return cityObject;
        }

        await completionTask;
    }

    private static async Task ProduceDeferredCityObjectsAsync(
        ChannelWriter<ResoniteConstructionCityObject> writer,
        ICityGmlGeometryProjector geometryProjector,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.SourceFilePipeline> sourceFiles,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<ParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        foreach (LocalCityGmlResonitePlanBuilder.SourceFilePipeline sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await foreach (ResoniteConstructionCityObject cityObject in StreamMaterializedCityObjectsAsync(
                               sourceFile,
                               geometryProjector,
                               referenceSystem,
                               globalOriginPoint,
                               globalCartesian,
                               demTerrainTextureOverlays,
                               terrainHeightSampler,
                               request,
                               sourceFile.GetParseTask(),
                               predicate,
                               cancellationToken))
            {
                await writer.WriteAsync(cityObject, cancellationToken);
            }
        }
    }

    private static async Task ProduceCachedCityObjectsAsync(
        ChannelWriter<ResoniteConstructionCityObject> writer,
        ICityGmlGeometryProjector geometryProjector,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> sourceFiles,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<ParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        foreach (LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                         sourceFile,
                         referenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         terrainHeightSampler,
                         request,
                         predicate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(cityObject, cancellationToken);
            }
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

    private static async IAsyncEnumerable<ResoniteConstructionCityObject> StreamMaterializedCityObjectsAsync(
        LocalCityGmlResonitePlanBuilder.SourceFilePipeline sourceFile,
        ICityGmlGeometryProjector geometryProjector,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Task<LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult>? parseTask,
        Func<ParsedCityObject, bool>? predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedSourceFile = parseTask is null
            ? await sourceFile.GetParseTask()
            : await parseTask;

        ValidateCompatibleReferenceSystem(referenceSystem, parsedSourceFile.ReferenceSystem);

        foreach (ParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
        {
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                         new LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                         referenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         terrainHeightSampler,
                         request))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return cityObject;
            }
        }
    }

    private static void ValidateCompatibleReferenceSystem(
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem expectedReferenceSystem,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem? actualReferenceSystem)
    {
        if (actualReferenceSystem is null || expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

    private static bool IsTerrainDependentCityObject(ParsedCityObject parsedCityObject)
    {
        return parsedCityObject.PackageName is not null
            && string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
    }
}
