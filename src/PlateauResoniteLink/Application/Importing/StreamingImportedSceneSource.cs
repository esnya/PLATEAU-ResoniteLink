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

internal sealed class StreamingImportedSceneSource : IImportedSceneSource, IImportedSceneSourcePreflight
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
    private readonly object sceneParsedDemSourceFilesGate = new();
    private readonly object sceneDemOverlayRegionsGate = new();
    private readonly object sceneDemTextureSourcesGate = new();
    private readonly object projectionTerrainOverlaySetGate = new();
    private readonly X3DMaterialWarningStatistics x3DMaterialWarningStatistics = new();
    private readonly MeshCodeBounds[] requestedMeshCodeBounds;
    private readonly string[] selectedMeshCodes;
    private readonly TerrainTextureOverlay[] discoveryTerrainTextureOverlays;
    private readonly bool hasDemPackage;
    private Task<ParsedSourceFileResult[]>? sceneParsedDemSourceFilesTask;
    private Task<DemTerrainOverlayRegion[]>? sceneDemOverlayRegionsTask;
    private Task<ResolvedDemTextureSources>? sceneDemTextureSourcesTask;
    private Task<ProjectionTerrainOverlaySet>? projectionTerrainOverlaySetTask;
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
        ImportedSceneSourceContext DiscoveryContext = readResult.DiscoveryContext;
        Metadata = metadata;
        this.request = request;
        sourceFiles = DiscoveryContext.SourceFilePipelines.ToArray();
        discoveryTerrainTextureOverlays = documentSet.TerrainTextureOverlays.ToArray();
        selectedMeshCodes = documentSet.SelectedMeshCodes.ToArray();
        hasDemPackage = documentSet.PackageNames.Contains("dem", StringComparer.OrdinalIgnoreCase);
        globalOriginPoint = DiscoveryContext.GlobalOriginPoint;
        this.geometryProjector = geometryProjector;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
        this.objectUnitOptimizer = objectUnitOptimizer;
        this.progressReporter = progressReporter;
        requestedMeshCodeBounds = MeshCodeBounds.CreateManyFromSelectedMeshCodes(
            Metadata.SourceDataset.SelectedMeshCodes ?? [request.MeshCode]);
    }

    public ImportedSceneMetadata Metadata { get; }

    public async Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default)
    {
        if (request.DemTextureSource is null || !hasDemPackage)
        {
            return;
        }

        _ = await GetSceneDemTextureSourcesAsync(useParsedDemCoverage: true, cancellationToken);
    }

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
        x3DMaterialWarningStatistics.ReportFinal(ReportProgress);
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
                           CreateObjectUnitsAsync(sourceFile, cancellationToken),
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
        ProjectionTerrainOverlaySet projectionTerrainOverlaySet = await GetProjectionTerrainOverlaySetAsync(cancellationToken);
        bool isDemSourceFile = string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        List<ParsedCityObject> parsedDemCityObjects = [];
        X3DMaterialWarningStatistics fileWarningStatistics = new();

        await foreach (ParsedCityObject parsedCityObject in sourceFile.StreamParsedCityObjectsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            parsedCount++;
            fileWarningStatistics.Add(parsedCityObject);
            x3DMaterialWarningStatistics.Add(parsedCityObject);
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
                         projectionTerrainOverlaySet.Overlays,
                         requestedMeshCodeBounds,
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
            ParsedCityObject[] aggregatedDemCityObjects =
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
                         projectionTerrainOverlaySet.Overlays,
                         requestedMeshCodeBounds,
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
        fileWarningStatistics.ReportFile(sourceFile.SourceFile.RelativePath, progressReporter);
        progressReporter?.Invoke(
            PlateauLog.Info(
                "import",
                $"City object producer projected '{sourceFile.SourceFile.RelativePath}' "
                + $"(parsed_city_objects={parsedCount}, yielded={yieldedCount}, elapsed={fileStopwatch.Elapsed.TotalSeconds:F3}s)."));
    }

    private async IAsyncEnumerable<ImportedObjectUnit> CreateObjectUnitsAsync(
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

    private async Task<ProjectionTerrainOverlaySet> GetProjectionTerrainOverlaySetAsync(
        CancellationToken cancellationToken)
    {
        if (!hasDemPackage)
        {
            return ProjectionTerrainOverlaySet.Empty;
        }

        Task<ProjectionTerrainOverlaySet> overlaySetTask;
        lock (projectionTerrainOverlaySetGate)
        {
            overlaySetTask = projectionTerrainOverlaySetTask ??= CreateProjectionTerrainOverlaySetAsync(cancellationToken);
        }

        try
        {
            return await overlaySetTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (overlaySetTask.IsCanceled || overlaySetTask.IsFaulted)
            {
                lock (projectionTerrainOverlaySetGate)
                {
                    if (ReferenceEquals(projectionTerrainOverlaySetTask, overlaySetTask))
                    {
                        projectionTerrainOverlaySetTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<ProjectionTerrainOverlaySet> CreateProjectionTerrainOverlaySetAsync(
        CancellationToken cancellationToken)
    {
        if (discoveryTerrainTextureOverlays.Length > 0
            && await HasSceneDemOverlayCoverageAsync(discoveryTerrainTextureOverlays, cancellationToken))
        {
            return new ProjectionTerrainOverlaySet(discoveryTerrainTextureOverlays);
        }

        bool useParsedDemCoverage = request.DemTextureSource is not null || discoveryTerrainTextureOverlays.Length > 0;
        ResolvedDemTextureSources resolvedDemTextureSources = await GetSceneDemTextureSourcesAsync(
            useParsedDemCoverage,
            cancellationToken);
        return new ProjectionTerrainOverlaySet(resolvedDemTextureSources.Overlays);
    }

    private async Task<bool> HasSceneDemOverlayCoverageAsync(
        IReadOnlyList<TerrainTextureOverlay> overlays,
        CancellationToken cancellationToken)
    {
        ParsedSourceFileResult[] parsedDemSourceFiles = await GetSceneParsedDemSourceFilesAsync(cancellationToken);
        foreach (ParsedSourceFileResult parsedSourceFile in parsedDemSourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DemTerrainOverlayAssignment.HasOverlayCoverage(
                        parsedCityObject,
                        overlays,
                        requestedMeshCodeBounds))
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private async Task<ResolvedDemTextureSources> GetSceneDemTextureSourcesAsync(
        bool useParsedDemCoverage,
        CancellationToken cancellationToken)
    {
        DemTerrainOverlayRegion[] overlayRegions = await GetSceneDemOverlayRegionsAsync(
            useParsedDemCoverage,
            cancellationToken);
        if (overlayRegions.Length == 0)
        {
            return new ResolvedDemTextureSources([]);
        }

        Task<ResolvedDemTextureSources> demTextureSourcesTask;
        lock (sceneDemTextureSourcesGate)
        {
            demTextureSourcesTask = sceneDemTextureSourcesTask ??= demTextureSourcePolicy.ResolveAsync(
                request,
                overlayRegions,
                cancellationToken);
        }

        try
        {
            return await demTextureSourcesTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (demTextureSourcesTask.IsCanceled || demTextureSourcesTask.IsFaulted)
            {
                lock (sceneDemTextureSourcesGate)
                {
                    if (ReferenceEquals(sceneDemTextureSourcesTask, demTextureSourcesTask))
                    {
                        sceneDemTextureSourcesTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<DemTerrainOverlayRegion[]> GetSceneDemOverlayRegionsAsync(
        bool useParsedDemCoverage,
        CancellationToken cancellationToken)
    {
        if (!useParsedDemCoverage)
        {
            return CreateRequestedDemOverlayRegions();
        }

        Task<DemTerrainOverlayRegion[]> overlayRegionsTask;
        lock (sceneDemOverlayRegionsGate)
        {
            overlayRegionsTask = sceneDemOverlayRegionsTask ??= CreateSceneDemOverlayRegionsAsync(cancellationToken);
        }

        try
        {
            return await overlayRegionsTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (overlayRegionsTask.IsCanceled || overlayRegionsTask.IsFaulted)
            {
                lock (sceneDemOverlayRegionsGate)
                {
                    if (ReferenceEquals(sceneDemOverlayRegionsTask, overlayRegionsTask))
                    {
                        sceneDemOverlayRegionsTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<DemTerrainOverlayRegion[]> CreateSceneDemOverlayRegionsAsync(
        CancellationToken cancellationToken)
    {
        ParsedSourceFileResult[] parsedDemSourceFiles = await GetSceneParsedDemSourceFilesAsync(cancellationToken);
        DemTerrainBounds? demBounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            ResolveRequestedDemTerrainBounds());
        return demBounds is null
            ? CreateRequestedDemOverlayRegions()
            : DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(demBounds, GetRequestedMeshCodes());
    }

    private DemTerrainOverlayRegion[] CreateRequestedDemOverlayRegions()
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(GetRequestedMeshCodes());
    }

    private string[] GetRequestedMeshCodes()
    {
        return selectedMeshCodes.Length == 0 ? [request.MeshCode] : selectedMeshCodes;
    }

    private async Task<ParsedSourceFileResult[]> GetSceneParsedDemSourceFilesAsync(
        CancellationToken cancellationToken)
    {
        Task<ParsedSourceFileResult[]> parsedDemSourceFilesTask;
        lock (sceneParsedDemSourceFilesGate)
        {
            parsedDemSourceFilesTask = sceneParsedDemSourceFilesTask ??= Task.WhenAll(
                sourceFiles
                    .Where(static sourceFile => string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
                    .Select(sourceFile => sourceFile.GetParseTask().WaitAsync(cancellationToken)));
        }

        try
        {
            return await parsedDemSourceFilesTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (parsedDemSourceFilesTask.IsCanceled || parsedDemSourceFilesTask.IsFaulted)
            {
                lock (sceneParsedDemSourceFilesGate)
                {
                    if (ReferenceEquals(sceneParsedDemSourceFilesTask, parsedDemSourceFilesTask))
                    {
                        sceneParsedDemSourceFilesTask = null;
                    }
                }
            }

            throw;
        }
    }

    private DemTerrainBounds? ResolveRequestedDemTerrainBounds()
    {
        return MeshCodeBounds.TryMerge(requestedMeshCodeBounds) is { } requestedMeshBounds
            ? new DemTerrainBounds(
                requestedMeshBounds.SouthLatitude,
                requestedMeshBounds.NorthLatitude,
                requestedMeshBounds.WestLongitude,
                requestedMeshBounds.EastLongitude)
            : null;
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

    private sealed record ProjectionTerrainOverlaySet(IReadOnlyList<TerrainTextureOverlay> Overlays)
    {
        public static ProjectionTerrainOverlaySet Empty { get; } = new([]);
    }

    private sealed class X3DMaterialWarningStatistics
    {
        private readonly object gate = new();
        private int materialSurfaceCount;
        private int unsupportedMaterialSurfaceCount;
        private int nonDefaultShininessCount;
        private int nonDefaultSpecularColorCount;
        private int specularOnlyCount;
        private int shininessOnlyCount;
        private int specularWithShininessCount;
        private int emissiveColorCount;
        private int ambientIntensityCount;

        public void Add(ParsedCityObject cityObject)
        {
            ArgumentNullException.ThrowIfNull(cityObject);

            foreach (ParsedSurface surface in cityObject.Surfaces)
            {
                MaterialOpticalProperties? opticalProperties = surface.OpticalProperties;
                if (opticalProperties is null)
                {
                    continue;
                }

                lock (gate)
                {
                    materialSurfaceCount++;
                    bool hasNonDefaultShininess = HasNonDefaultShininess(opticalProperties.Shininess);
                    bool hasNonDefaultSpecularColor = HasNonDefaultSpecularColor(opticalProperties.SpecularColor);
                    bool hasNonDefaultEmissiveColor = HasNonDefaultEmissiveColor(opticalProperties.EmissiveColor);
                    bool hasNonDefaultAmbientIntensity =
                        HasNonDefaultAmbientIntensity(opticalProperties.AmbientIntensity);
                    if (hasNonDefaultShininess)
                    {
                        nonDefaultShininessCount++;
                    }

                    if (hasNonDefaultSpecularColor)
                    {
                        nonDefaultSpecularColorCount++;
                    }

                    if (hasNonDefaultSpecularColor && hasNonDefaultShininess)
                    {
                        specularWithShininessCount++;
                    }
                    else if (hasNonDefaultSpecularColor)
                    {
                        specularOnlyCount++;
                    }
                    else if (hasNonDefaultShininess)
                    {
                        shininessOnlyCount++;
                    }

                    if (hasNonDefaultEmissiveColor)
                    {
                        emissiveColorCount++;
                    }

                    if (hasNonDefaultAmbientIntensity)
                    {
                        ambientIntensityCount++;
                    }

                    if (hasNonDefaultShininess
                        || hasNonDefaultSpecularColor
                        || hasNonDefaultEmissiveColor
                        || hasNonDefaultAmbientIntensity)
                    {
                        unsupportedMaterialSurfaceCount++;
                    }
                }
            }
        }

        public void ReportFile(string sourceFileRelativePath, Action<string>? progressReporter)
        {
            if (!HasUnsupportedValues)
            {
                return;
            }

            progressReporter?.Invoke(
                PlateauLog.Warning(
                    "import",
                    $"CityGML file '{sourceFileRelativePath}' contains unsupported X3DMaterial optical attributes left unprojected "
                    + CreateSummarySuffix()));
        }

        public void ReportFinal(Action<string> progressReporter)
        {
            if (!HasUnsupportedValues)
            {
                return;
            }

            progressReporter(
                PlateauLog.Warning(
                    "import",
                    "Unsupported X3DMaterial optical attribute summary: values were parsed for diagnostics but not projected to Resonite materials "
                    + CreateSummarySuffix()));
        }

        private bool HasUnsupportedValues =>
            nonDefaultShininessCount > 0
            || nonDefaultSpecularColorCount > 0
            || emissiveColorCount > 0
            || ambientIntensityCount > 0;

        private string CreateSummarySuffix()
        {
            lock (gate)
            {
                return string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"(x3d_material_surfaces={materialSurfaceCount}, unsupported_x3d_material_surfaces={unsupportedMaterialSurfaceCount}, shininess_nonzero={nonDefaultShininessCount}, specular_nondefault={nonDefaultSpecularColorCount}, specular_nondefault_only={specularOnlyCount}, shininess_nonzero_only={shininessOnlyCount}, specular_nondefault_with_shininess={specularWithShininessCount}, emissive_nonzero={emissiveColorCount}, ambient_nonzero={ambientIntensityCount}).");
            }
        }

        private static bool HasNonDefaultShininess(double? shininess)
        {
            return shininess.HasValue && Math.Abs(Math.Clamp(shininess.Value, 0.0, 1.0)) > 1e-9;
        }

        private static bool HasNonDefaultAmbientIntensity(double? ambientIntensity)
        {
            return ambientIntensity.HasValue && Math.Abs(Math.Clamp(ambientIntensity.Value, 0.0, 1.0)) > 1e-9;
        }

        private static bool HasNonDefaultEmissiveColor(ColorRgba? emissiveColor)
        {
            if (emissiveColor is null)
            {
                return false;
            }

            return Math.Abs(emissiveColor.R) > 1e-6
                || Math.Abs(emissiveColor.G) > 1e-6
                || Math.Abs(emissiveColor.B) > 1e-6;
        }

        private static bool HasNonDefaultSpecularColor(ColorRgba? specularColor)
        {
            if (specularColor is null)
            {
                return false;
            }

            return Math.Abs(specularColor.R - 0.4) > 1e-6
                || Math.Abs(specularColor.G - 0.4) > 1e-6
                || Math.Abs(specularColor.B - 0.4) > 1e-6;
        }
    }
}
