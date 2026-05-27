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
    private readonly object sceneDemTerrainTextureOverlayGate = new();
    private readonly ConcurrentDictionary<string, Task<TerrainTextureOverlay[]>> demTerrainTextureOverlayTasks = new(StringComparer.Ordinal);
    private readonly X3DMaterialWarningStatistics x3DMaterialWarningStatistics = new();
    private readonly MeshCodeBounds[] requestedMeshCodeBounds;
    private readonly string[] selectedMeshCodes;
    private readonly TerrainTextureOverlay[] discoveryTerrainTextureOverlays;
    private readonly bool hasDemPackage;
    private Task<TerrainTextureOverlay[]>? sceneDemTerrainTextureOverlayTask;
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

        _ = await CreateSceneDemTerrainTextureOverlaysAsync(cancellationToken);
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
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays = await GetProjectionDemTerrainTextureOverlaysAsync(
            sourceFile,
            cancellationToken);
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
                         demTerrainTextureOverlays,
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
                         demTerrainTextureOverlays,
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

    private TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(string packageName)
    {
        if (!string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            || discoveryTerrainTextureOverlays.Length == 0)
        {
            return [];
        }

        return discoveryTerrainTextureOverlays.ToArray();
    }

    private async Task<TerrainTextureOverlay[]> GetProjectionDemTerrainTextureOverlaysAsync(
        SourceFilePipeline sourceFile,
        CancellationToken cancellationToken)
    {
        return string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? await GetDemTerrainTextureOverlaysAsync(sourceFile, cancellationToken)
            : await GetSceneDemTerrainTextureOverlaysAsync(cancellationToken);
    }

    private async Task<TerrainTextureOverlay[]> GetSceneDemTerrainTextureOverlaysAsync(
        CancellationToken cancellationToken)
    {
        if (!hasDemPackage)
        {
            return [];
        }

        if (discoveryTerrainTextureOverlays.Length > 0)
        {
            return discoveryTerrainTextureOverlays.ToArray();
        }

        Task<TerrainTextureOverlay[]> overlayTask;
        lock (sceneDemTerrainTextureOverlayGate)
        {
            overlayTask = sceneDemTerrainTextureOverlayTask ??= CreateSceneDemTerrainTextureOverlaysAsync(cancellationToken);
        }

        try
        {
            return await overlayTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (overlayTask.IsCanceled || overlayTask.IsFaulted)
            {
                lock (sceneDemTerrainTextureOverlayGate)
                {
                    if (ReferenceEquals(sceneDemTerrainTextureOverlayTask, overlayTask))
                    {
                        sceneDemTerrainTextureOverlayTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<TerrainTextureOverlay[]> CreateSceneDemTerrainTextureOverlaysAsync(
        CancellationToken cancellationToken)
    {
        DemTerrainOverlayRegion[] overlayRegions = DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
            selectedMeshCodes.Length == 0 ? [request.MeshCode] : selectedMeshCodes);
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

    private bool HasOverlayCoverage(
        ParsedSourceFileResult parsedSourceFile,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        foreach (ParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
        {
            if (DemTerrainOverlayAssignment.HasOverlayCoverage(
                    parsedCityObject,
                    overlays,
                    requestedMeshCodeBounds))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool HasAnyVertices(IEnumerable<ParsedCityObject> cityObjects)
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

        TerrainTextureOverlay[] discoveryOverlays = CreateDemTerrainTextureOverlays(parsedSourceFile.SourceFile.PackageName);
        if (discoveryOverlays.Length == 0)
        {
            return await CreateDemTerrainTextureOverlaysFromParsedSourceFileAsync(
                parsedSourceFile,
                preferRequestedMeshCodeSplit: true,
                cancellationToken);
        }

        if (HasOverlayCoverage(parsedSourceFile, discoveryOverlays))
        {
            return discoveryOverlays;
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
        DemTerrainBounds? fallbackBounds = MeshCodeBounds.TryMerge(requestedMeshCodeBounds) is { } requestedMeshBounds
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
                : DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
                    fallbackBounds,
                    selectedMeshCodes);
        }

        DemTerrainBounds? demBounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            [parsedSourceFile],
            fallbackBounds);
        return demBounds is null
            ? []
            : DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
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
