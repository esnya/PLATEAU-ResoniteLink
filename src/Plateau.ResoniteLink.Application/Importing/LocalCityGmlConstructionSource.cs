using System.Threading.Channels;

using LocalCartesian = GeographicLib.LocalCartesian;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    private readonly PlateauImportRequest request;
    private readonly IReadOnlyList<CachedSourceFileDescriptor> demSourceFiles;
    private readonly IReadOnlyList<SourceFilePipeline> deferredSourceFiles;
    private readonly CoordinateReferenceSystem referenceSystem;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly TerrainHeightSampler? terrainHeightSampler;
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
        demSourceFiles = documentSet.BootstrapCachedDemSourceFiles;
        deferredSourceFiles = documentSet.BootstrapSourceFilePipelines
            .Where(static pipeline => !string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        referenceSystem = documentSet.BootstrapReferenceSystem;
        globalOriginPoint = documentSet.BootstrapGlobalOriginPoint;
        terrainHeightSampler = documentSet.BootstrapTerrainHeightSampler;
        this.geometryProjector = geometryProjector;
        demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();
    }

    public ResoniteConstructionMetadata Metadata { get; }

    public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LocalCartesian? globalCartesian = referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;
        HashSet<string> emittedMaterialKeys = new(StringComparer.Ordinal);

        foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = sourceFile.GetParseTask();
        }

        foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            await foreach (ResoniteMaterialBinding material in StreamCommonMaterialsAsync(
                               sourceFile,
                               referenceSystem,
                               globalOriginPoint,
                               globalCartesian,
                               demTerrainTextureOverlays,
                               terrainHeightSampler,
                               request,
                               sourceFile.GetParseTask(),
                               emittedMaterialKeys,
                               cancellationToken))
            {
                yield return material;
            }
        }

        foreach (CachedSourceFileDescriptor sourceFile in demSourceFiles)
        {
            foreach (ResoniteMaterialBinding material in LocalCityGmlResonitePlanBuilder.EnumerateCommonMaterials(
                         sourceFile.ToLegacy(),
                         referenceSystem.ToLegacy(),
                         globalOriginPoint.ToLegacy(),
                         globalCartesian,
                         demTerrainTextureOverlays,
                         terrainHeightSampler?.ToLegacy(),
                         request,
                         emittedMaterialKeys))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return material;
            }
        }
    }

    public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
    {
        LocalCartesian? globalCartesian = referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;

        foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                new CachedSourceFileDescriptor(
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

        foreach (CachedSourceFileDescriptor sourceFile in demSourceFiles)
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

        foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
        {
            ParsedSourceFileResult parsedSourceFile = sourceFile.GetParseTask().GetAwaiter().GetResult();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                new CachedSourceFileDescriptor(
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

        foreach (SourceFilePipeline sourceFile in deferredSourceFiles)
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
        IReadOnlyList<SourceFilePipeline> sourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        foreach (SourceFilePipeline sourceFile in sourceFiles)
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
        IReadOnlyList<CachedSourceFileDescriptor> sourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        foreach (CachedSourceFileDescriptor sourceFile in sourceFiles)
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
        SourceFilePipeline sourceFile,
        ICityGmlGeometryProjector geometryProjector,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Task<ParsedSourceFileResult>? parseTask,
        Func<BootstrapParsedCityObject, bool>? predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ParsedSourceFileResult parsedSourceFile = parseTask is null
            ? await sourceFile.GetParseTask()
            : await parseTask;

        ValidateCompatibleReferenceSystem(referenceSystem, parsedSourceFile.ReferenceSystem);

        foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
        {
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
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

    private static async IAsyncEnumerable<ResoniteMaterialBinding> StreamCommonMaterialsAsync(
        SourceFilePipeline sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Task<ParsedSourceFileResult>? parseTask,
        HashSet<string> emittedMaterialKeys,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ParsedSourceFileResult parsedSourceFile = parseTask is null
            ? await sourceFile.GetParseTask()
            : await parseTask;

        ValidateCompatibleReferenceSystem(referenceSystem, parsedSourceFile.ReferenceSystem);

        foreach (ResoniteMaterialBinding material in LocalCityGmlResonitePlanBuilder.EnumerateCommonMaterials(
                     new CachedSourceFileDescriptor(sourceFile.SourceFile, parsedSourceFile.CityObjects).ToLegacy(),
                     referenceSystem.ToLegacy(),
                     globalOriginPoint.ToLegacy(),
                     globalCartesian,
                     demTerrainTextureOverlays,
                     terrainHeightSampler?.ToLegacy(),
                     request,
                     emittedMaterialKeys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return material;
        }
    }

    private static void ValidateCompatibleReferenceSystem(
        CoordinateReferenceSystem expectedReferenceSystem,
        CoordinateReferenceSystem? actualReferenceSystem)
    {
        if (actualReferenceSystem is null || expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

    private static bool IsTerrainDependentCityObject(BootstrapParsedCityObject parsedCityObject)
    {
        return parsedCityObject.PackageName is not null
            && string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
    }
}
