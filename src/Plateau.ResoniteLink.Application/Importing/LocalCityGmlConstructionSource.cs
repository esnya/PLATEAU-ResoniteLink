using System.Diagnostics;
using System.Threading.Channels;

using LocalCartesian = GeographicLib.LocalCartesian;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    private readonly PlateauImportRequest request;
    private readonly CachedSourceFileDescriptor[] demSourceFiles;
    private readonly SourceFilePipeline[] deferredSourceFiles;
    private readonly CoordinateReferenceSystem referenceSystem;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly TerrainHeightSampler? terrainHeightSampler;
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
        demSourceFiles = documentSet.BootstrapCachedDemSourceFiles.ToArray();
        deferredSourceFiles = documentSet.BootstrapSourceFilePipelines
            .Where(static pipeline => !string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        referenceSystem = documentSet.BootstrapReferenceSystem;
        globalOriginPoint = documentSet.BootstrapGlobalOriginPoint;
        terrainHeightSampler = documentSet.BootstrapTerrainHeightSampler;
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
                request))
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

    }

    public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object streaming pipeline starting "
                + $"(deferred_files={deferredSourceFiles.Length}, dem_files={demSourceFiles.Length})."));
        LocalCartesian? globalCartesian = referenceSystem.IsGeographic
            ? new LocalCartesian(
                globalOriginPoint.Latitude,
                globalOriginPoint.Longitude,
                globalOriginPoint.Altitude,
                referenceSystem.Geocentric)
            : null;

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
                "City object producers launched: deferred-nonterrain, dem."));
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
                    progressReporter,
                    producerName: "deferred-nonterrain",
                    predicate: null,
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
                    progressReporter,
                    producerName: "dem",
                    predicate: null,
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
        SourceFilePipeline[] sourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Action<string>? progressReporter,
        string producerName,
        Func<BootstrapParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceFilePipeline sourceFile = sourceFiles[index];
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"City object producer '{producerName}' processing deferred source file "
                    + $"{index + 1}/{sourceFiles.Length}: '{sourceFile.SourceFile.RelativePath}'."));
            int yieldedCount = 0;

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
                               progressReporter,
                               producerName,
                               predicate,
                               cancellationToken))
            {
                yieldedCount++;
                await writer.WriteAsync(cityObject, cancellationToken);
            }

            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"City object producer '{producerName}' finished deferred source file "
                    + $"{index + 1}/{sourceFiles.Length}: '{sourceFile.SourceFile.RelativePath}' "
                    + $"(yielded={yieldedCount})."));
        }
    }

    private static async Task ProduceCachedCityObjectsAsync(
        ChannelWriter<ResoniteConstructionCityObject> writer,
        ICityGmlGeometryProjector geometryProjector,
        CachedSourceFileDescriptor[] sourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Action<string>? progressReporter,
        string producerName,
        Func<BootstrapParsedCityObject, bool>? predicate,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CachedSourceFileDescriptor sourceFile = sourceFiles[index];
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"City object producer '{producerName}' processing cached source file "
                    + $"{index + 1}/{sourceFiles.Length}: '{sourceFile.SourceFile.RelativePath}'."));
            int yieldedCount = 0;

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
                yieldedCount++;
                await writer.WriteAsync(cityObject, cancellationToken);
            }

            progressReporter?.Invoke(
                PlateauLog.Info(
                    "import",
                    $"City object producer '{producerName}' finished cached source file "
                    + $"{index + 1}/{sourceFiles.Length}: '{sourceFile.SourceFile.RelativePath}' "
                    + $"(yielded={yieldedCount})."));
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
        Action<string>? progressReporter,
        string producerName,
        Func<BootstrapParsedCityObject, bool>? predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stopwatch parseStopwatch = Stopwatch.StartNew();
        ParsedSourceFileResult parsedSourceFile = parseTask is null
            ? await sourceFile.GetParseTask()
            : await parseTask;
        parseStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer '{producerName}' parsed '{sourceFile.SourceFile.RelativePath}' in "
                + $"{parseStopwatch.Elapsed.TotalSeconds:F3}s "
                + $"(parsed_city_objects={parsedSourceFile.CityObjects.Length})."));

        ValidateCompatibleReferenceSystem(referenceSystem, parsedSourceFile.ReferenceSystem);

        int materializedCount = 0;
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
                materializedCount++;
                yield return cityObject;
            }
        }

        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer '{producerName}' materialized '{sourceFile.SourceFile.RelativePath}' "
                + $"(yielded={materializedCount})."));
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
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
}
