using System.Diagnostics;
using System.Threading.Channels;

using LocalCartesian = GeographicLib.LocalCartesian;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSource : IResoniteConstructionSource
{
    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly SourceFilePipeline[] demSourceFiles;
    private readonly CoordinateReferenceSystem referenceSystem;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly TerrainTextureOverlay[] demTerrainTextureOverlays;
    private readonly ICityGmlGeometryProjector geometryProjector;
    private readonly Action<string>? progressReporter;
    private readonly Task<TerrainContext> terrainContextTask;

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
        demSourceFiles = sourceFiles
            .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        referenceSystem = documentSet.BootstrapReferenceSystem;
        globalOriginPoint = documentSet.BootstrapGlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.progressReporter = progressReporter;
        demTerrainTextureOverlays = metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .ToArray();
        terrainContextTask = CreateTerrainContextTask();
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
                TerrainContext terrainContext = await ResolveTerrainContextAsync(parsedCityObject, cancellationToken);

                foreach (ResoniteMaterialBinding material in LocalCityGmlResonitePlanBuilder.EnumerateCommonMaterials(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]).ToLegacy(),
                             referenceSystem.ToLegacy(),
                             globalOriginPoint.ToLegacy(),
                             globalCartesian,
                             demTerrainTextureOverlays,
                             terrainContext.TerrainHeightSampler?.ToLegacy(),
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
                TerrainHeightSampler? terrainHeightSampler = LocalCityGmlTerrainDependency.IsTerrainDependent(parsedCityObject)
                    ? terrainContextTask.GetAwaiter().GetResult().TerrainHeightSampler
                    : null;

                foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                             new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
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
    }

    public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object streaming pipeline starting "
                + $"(source_files={sourceFiles.Length}, dem_files={demSourceFiles.Length})."));

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

    private Task<TerrainContext> CreateTerrainContextTask()
    {
        if (demSourceFiles.Length == 0 || !referenceSystem.IsGeographic || referenceSystem.Geocentric is null)
        {
            return Task.FromResult(TerrainContext.Empty);
        }

        return BuildTerrainContextAsync(
            demSourceFiles,
            referenceSystem,
            globalOriginPoint,
            progressReporter);
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
            TerrainContext terrainContext = await ResolveTerrainContextAsync(parsedCityObject, cancellationToken);

            foreach (ResoniteConstructionCityObject cityObject in geometryProjector.MaterializeCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                         referenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         terrainContext.TerrainHeightSampler,
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

    private async Task<TerrainContext> ResolveTerrainContextAsync(
        BootstrapParsedCityObject parsedCityObject,
        CancellationToken cancellationToken)
    {
        if (!LocalCityGmlTerrainDependency.IsTerrainDependent(parsedCityObject))
        {
            return TerrainContext.Empty;
        }

        return await terrainContextTask.WaitAsync(cancellationToken);
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private static async Task<TerrainContext> BuildTerrainContextAsync(
        SourceFilePipeline[] demSourceFiles,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        Action<string>? progressReporter)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<TerrainHeightTriangle> terrainTriangles = [];
        int parsedCityObjectCount = 0;

        foreach (SourceFilePipeline sourceFile in demSourceFiles)
        {
            await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync())
            {
                ValidateCompatibleReferenceSystem(referenceSystem, parsedCityObject.ReferenceSystem);
                parsedCityObjectCount++;
                terrainTriangles.AddRange(LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles([parsedCityObject]));
            }
        }

        TerrainHeightSampler? terrainHeightSampler = LocalCityGmlDemBootstrapSupport.CreateTerrainHeightSampler(
            referenceSystem.IsGeographic,
            terrainTriangles,
            globalOriginPoint,
            referenceSystem.Geocentric);

        stopwatch.Stop();
        progressReporter?.Invoke(
            terrainHeightSampler is null
                ? PlateauLog.Info(
                    "import",
                    $"Terrain bootstrap completed without sampler "
                    + $"(dem_files={demSourceFiles.Length}, parsed_city_objects={parsedCityObjectCount}, elapsed={stopwatch.Elapsed.TotalSeconds:F3}s).")
                : PlateauLog.Info(
                    "import",
                    $"Terrain bootstrap completed "
                    + $"(dem_files={demSourceFiles.Length}, parsed_city_objects={parsedCityObjectCount}, triangles={terrainTriangles.Count}, elapsed={stopwatch.Elapsed.TotalSeconds:F3}s)."));

        return new TerrainContext(terrainHeightSampler, parsedCityObjectCount, terrainTriangles.Count);
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
