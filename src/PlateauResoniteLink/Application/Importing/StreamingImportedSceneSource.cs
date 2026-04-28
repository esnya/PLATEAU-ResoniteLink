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

internal sealed class StreamingImportedSceneSource : IImportedSceneSource
{
    internal const int MaxConcurrentCityObjectProducers = 8;

    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly GeodeticPoint globalOriginPoint;
    private readonly ICityGmlGeometryProjector geometryProjector;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy;
    private readonly IImportedObjectUnitOptimizer objectUnitOptimizer;
    private readonly Action<string>? progressReporter;
    private readonly object referenceSystemGate = new();
    private readonly ConcurrentDictionary<string, Task<TerrainTextureOverlay[]>> demTerrainTextureOverlayTasks = new(StringComparer.Ordinal);
    private readonly MeshCodeBounds[] requestedMeshAreas;
    private readonly TerrainTextureOverlay[] bootstrapTerrainTextureOverlays;
    private CoordinateReferenceSystem? referenceSystem;

    public StreamingImportedSceneSource(
        ImportedSceneMetadata metadata,
        PlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        ICityGmlGeometryProjector geometryProjector,
        IDemTextureSourcePolicy demTextureSourcePolicy,
        IImportedObjectUnitOptimizer objectUnitOptimizer,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        ImportedSceneSourceDataset documentSet = readResult.DocumentSet;
        ImportedSceneSourceContext bootstrapContext = readResult.BootstrapContext;
        Metadata = metadata;
        this.request = request;
        sourceFiles = bootstrapContext.SourceFilePipelines.ToArray();
        bootstrapTerrainTextureOverlays = documentSet.TerrainTextureOverlays.ToArray();
        globalOriginPoint = bootstrapContext.GlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
        this.objectUnitOptimizer = objectUnitOptimizer;
        this.progressReporter = progressReporter;
        requestedMeshAreas = MeshCodeBounds.CreateManyFromSelectedMeshCodes(
            Metadata.SourceDataset.SelectedMeshCodes ?? [request.MeshCode]);
    }

    public ImportedSceneMetadata Metadata { get; }

    public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReportProgress(
            PlateauLog.Info(
                "import",
                $"City object unit streaming pipeline starting (source_files={sourceFiles.Length})."));
        Channel<ImportedObjectUnit> channel = Channel.CreateBounded<ImportedObjectUnit>(
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
                $"City object unit producers launched: {producerConcurrency} worker(s) for {sourceFiles.Length} file-scoped streams."));

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

        await foreach (ImportedObjectUnit objectUnit in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return objectUnit;
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
        ChannelWriter<ImportedObjectUnit> writer,
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
        int yieldedUnitCount = 0;
        await foreach (ImportedObjectUnit objectUnit in objectUnitOptimizer.OptimizeAsync(
                           BuildObjectUnitsAsync(sourceFile, cancellationToken),
                           cancellationToken))
        {
            yieldedUnitCount++;
            yieldedCount += objectUnit.CityObjects.Count;
            await writer.WriteAsync(objectUnit, cancellationToken);
        }

        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer finished source file "
                + $"{fileIndex}/{totalFiles}: '{sourceFile.SourceFile.RelativePath}' "
                + $"(yielded_units={yieldedUnitCount}, yielded_city_objects={yieldedCount})."));
    }

    private async Task ProduceCityObjectsUntilDrainedAsync(
        ChannelWriter<ImportedObjectUnit> writer,
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
        ChannelWriter<ImportedObjectUnit> writer,
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
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = await GetDemTerrainTextureOverlaysAsync(
            sourceFile,
            cancellationToken);
        bool isDemSourceFile = string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        List<BootstrapParsedCityObject> parsedDemCityObjects = [];

        await foreach (BootstrapParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            parsedCount++;
            resolvedReferenceSystem ??= ResolveReferenceSystem(parsedCityObject.ReferenceSystem);
            globalCartesian ??= CreateGlobalCartesian(resolvedReferenceSystem);
            if (isDemSourceFile)
            {
                parsedDemCityObjects.Add(parsedCityObject);
                continue;
            }

            foreach (ImportedCityObject cityObject in geometryProjector.ProjectCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, [parsedCityObject]),
                         resolvedReferenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
                         request,
                         predicate: null,
                         progressReporter,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yieldedCount++;
                yield return cityObject;
            }
        }

        if (isDemSourceFile && parsedDemCityObjects.Count > 0)
        {
            BootstrapParsedCityObject[] aggregatedDemCityObjects =
                DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                    sourceFile.SourceFile,
                    parsedDemCityObjects);
            foreach (ImportedCityObject cityObject in geometryProjector.ProjectCityObjects(
                         new CachedSourceFileDescriptor(sourceFile.SourceFile, aggregatedDemCityObjects),
                         resolvedReferenceSystem
                             ?? throw new PlateauImportValidationException(
                                 [$"CityGML file '{sourceFile.SourceFile.RelativePath}' does not declare a supported coordinate reference system."]),
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
                         request,
                         predicate: null,
                         progressReporter,
                         cancellationToken))
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
                $"City object producer projected '{sourceFile.SourceFile.RelativePath}' "
                + $"(parsed_city_objects={parsedCount}, yielded={yieldedCount}, elapsed={fileStopwatch.Elapsed.TotalSeconds:F3}s)."));
    }

    private async IAsyncEnumerable<ImportedObjectUnit> BuildObjectUnitsAsync(
        SourceFilePipeline sourceFile,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<(int? LodLevel, List<ImportedCityObject> CityObjects)> objectUnitsByLod = [];

        await foreach (ImportedCityObject cityObject in StreamProjectedCityObjectsAsync(sourceFile, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? lodLevel = cityObject.LodLevel;
            int existingIndex = objectUnitsByLod.FindIndex(group => group.LodLevel == lodLevel);
            if (existingIndex < 0)
            {
                List<ImportedCityObject> cityObjects = [cityObject];
                objectUnitsByLod.Add((lodLevel, cityObjects));
                continue;
            }

            objectUnitsByLod[existingIndex].CityObjects.Add(cityObject);
        }

        foreach ((int? lodLevel, List<ImportedCityObject> cityObjects) in objectUnitsByLod)
        {
            if (cityObjects.Count == 0)
            {
                continue;
            }

            yield return new ImportedObjectUnit(
                sourceFileRelativePath: sourceFile.SourceFile.RelativePath,
                packageName: sourceFile.SourceFile.PackageName,
                lodLevel: lodLevel,
                cityObjects: cityObjects.ToArray(),
                matchedMeshCode: sourceFile.SourceFile.MatchedMeshCode);
        }
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

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(string packageName)
    {
        if (!string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            || bootstrapTerrainTextureOverlays.Length == 0)
        {
            return [];
        }

        return bootstrapTerrainTextureOverlays.ToArray();
    }

    private bool HasOverlayCoverage(
        ParsedSourceFileResult parsedSourceFile,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        foreach (BootstrapParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
        {
            if (DemTerrainOverlayAssignment.HasOverlayCoverage(
                    parsedCityObject,
                    overlays,
                    requestedMeshAreas))
            {
                continue;
            }

            return false;
        }

        return true;
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

    private async Task<TerrainTextureOverlay[]> GetDemTerrainTextureOverlaysAsync(
        SourceFilePipeline sourceFile,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        Task<TerrainTextureOverlay[]> overlayTask = demTerrainTextureOverlayTasks.GetOrAdd(
            sourceFile.SourceFile.RelativePath,
            _ => ResolveDemTerrainTextureOverlaysCoreAsync(sourceFile, cancellationToken));
        try
        {
            return await overlayTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (overlayTask.IsCanceled || overlayTask.IsFaulted)
            {
                demTerrainTextureOverlayTasks.TryRemove(sourceFile.SourceFile.RelativePath, out _);
            }

            throw;
        }
    }

    private async Task<TerrainTextureOverlay[]> ResolveDemTerrainTextureOverlaysCoreAsync(
        SourceFilePipeline sourceFile,
        CancellationToken cancellationToken)
    {
        ParsedSourceFileResult parsedSourceFile = await sourceFile.GetParseTask().WaitAsync(cancellationToken);
        return await CreateDemTerrainTextureOverlaysAsync(parsedSourceFile, cancellationToken);
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
        IReadOnlyList<string> selectedMeshCodes =
            preferRequestedMeshCodeSplit && Metadata.SourceDataset.SelectedMeshCodes is { Count: > 0 }
                ? Metadata.SourceDataset.SelectedMeshCodes
                : [];
        if (!HasAnyVertices(parsedSourceFile.CityObjects))
        {
            return fallbackBounds is null
                ? []
                : DemSourceBootstrapSupport.CreateDemTerrainOverlayRegions(
                    fallbackBounds,
                    selectedMeshCodes);
        }

        DemTerrainBounds? demBounds = DemSourceBootstrapSupport.ResolveDemTerrainBounds(
            [parsedSourceFile],
            fallbackBounds);
        return demBounds is null
            ? []
            : DemSourceBootstrapSupport.CreateDemTerrainOverlayRegions(
                demBounds,
                selectedMeshCodes);
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
