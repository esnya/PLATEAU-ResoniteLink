using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using System.Diagnostics.CodeAnalysis;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

public sealed class ResoniteLinkSceneBuilder : IResoniteSceneBuilder
{
    private const int MaxQueuedCityObjects = 4;
    private const int CityObjectSendAttemptLimit = 2;
    private const int VisibilityPollDelayMilliseconds = 50;
    private const int VisibilityPollAttemptLimit = 200;
    private const string RootSlotId = "Root";
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly SemaphoreSlim clientInitializationGate = new(1, 1);
    private readonly Action<string>? progressReporter;
    private readonly ConcurrentDictionary<(string ParentSlotId, string SlotName), Lazy<Task<CreatedSlot>>> sharedSlotTasks = [];
    private IResoniteLinkClient? setupClient;
    private ConcurrentBag<IResoniteLinkClient>? backgroundClients;
    private ResoniteConstructionMetadata? metadata;
    private CreatedSlot? datasetRootSlot;
    private CreatedSlot? datasetAssetsRootSlot;
    private CreatedSlot? commonAssetsRootSlot;
    private ResoniteMaterialAssetManager? materialAssetManager;
    private string? generatedAssetsRoot;
    private ConcurrentDictionary<TextureImportCacheKey, Lazy<Task<Uri>>>? importedTextureUriTasks;
    private DispatchLaneAllocator? dispatchLaneAllocator;
    private ResoniteTextureImportResolver? textureImportResolver;
    private Channel<QueuedCityObject>[]? cityObjectChannels;
    private Task[]? processingTasks;
    private int processedCityObjectCount;
    private Stopwatch? sceneBuildStopwatch;
    private int firstQueuedCityObjectLogged;
    private int firstPreparedCityObjectLogged;
    private int firstBuiltCityObjectLogged;
    private int retriedCityObjectCount;
    private int skippedFailedCityObjectCount;
    private int retriedAndRecoveredCityObjectCount;
    private readonly ConcurrentQueue<string> skippedCityObjectErrors = new();
    private IPlateauDatasetContentSource? datasetContentSource;
    private SceneAnchor? sceneAnchor;

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(endpoint, 4, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, int connectionCount, Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, diagnostics, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Func<IResoniteLinkClient> clientFactory,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        Action<string>? progressReporter = null)
    {
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.diagnostics = diagnostics;
        this.clientFactory = clientFactory;
        this.terrainTextureAssetGenerator = terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator();
        this.progressReporter = progressReporter;
    }

    public async Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureSetupClientConnectedAsync(request, cancellationToken);
    }

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        if (this.metadata is not null || processingTasks is not null)
        {
            throw new InvalidOperationException("A live scene build run is already active on this scene builder instance.");
        }

        this.metadata = metadata;
        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        Directory.CreateDirectory(resolvedWorkRoot);
        generatedAssetsRoot = Path.Combine(resolvedWorkRoot, ".generated-assets");
        string completionMeshCode = ResolveCompletionMeshCode(metadata);

        ReportProgress(
            $"[live] Initializing scene state for dataset '{metadata.Request.Dataset}' "
            + $"mesh '{metadata.Request.MeshCode}' at '{resolvedWorkRoot}'.");
        ReportProgress(
            $"[live] Connecting setup ResoniteLink session to {endpoint} "
            + $"and scheduling {Math.Max(connectionCount - 1, 0)} worker session(s).");
        await EnsureSetupClientConnectedAsync(metadata.Request, cancellationToken);
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        importedTextureUriTasks = [];
        dispatchLaneAllocator = new DispatchLaneAllocator(connectionCount);
        materialAssetManager = new ResoniteMaterialAssetManager(
            (containerSlotId, componentType, importAssetAsync, ct) =>
                CreateSharedAssetComponentAsync(
                    setupClient,
                    containerSlotId,
                    componentType,
                    importAssetAsync,
                    ct),
            (containerSlotId, componentType, importAssetAsync, ct) =>
                CreateDedicatedAssetComponentAsync(
                    setupClient,
                    containerSlotId,
                    componentType,
                    importAssetAsync,
                    ct),
            (parentSlotId, slotName, ct) =>
                GetOrCreateSharedChildSlotByIdAsync(
                    setupClient,
                    parentSlotId,
                    slotName,
                    null,
                    null,
                    ct),
            (containerSlotId, componentType, members, ct) =>
                CreateComponentAsync(
                    setupClient,
                    containerSlotId,
                    componentType,
                    members,
                    ct),
            ImportTextureAsync,
            ReportProgress);
        PlateauLocalImportSource localSource = metadata.Request.Source as PlateauLocalImportSource
            ?? throw new InvalidOperationException("Live scene building requires a resolved local dataset source.");

        ReportProgress("[live] Opening resolved dataset content source for texture materialization.");
        datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(localSource.LocalSourcePath!, cancellationToken);
        textureImportResolver = new ResoniteTextureImportResolver(
            datasetContentSource,
            generatedAssetsRoot,
            metadata.SourceDataset.TerrainTextureOverlays,
            terrainTextureAssetGenerator);
        ReportProgress("[live] Creating dataset root, asset groups, and anchor slots.");
        (datasetRootSlot, datasetAssetsRootSlot, commonAssetsRootSlot) =
            await CreateSetupSlotHierarchyAsync(setupClient, cancellationToken);
        await CreateComponentAsync(
            setupClient,
            datasetRootSlot.Value.SlotId,
            "[FrooxEngine]FrooxEngine.License",
            CreateDatasetLicenseMembers(metadata.Attribution.DatasetLicense),
            cancellationToken);
        CreatedSlot createdAnchor = await GetOrCreateSharedChildSlotAsync(
            setupClient,
            datasetRootSlot.Value,
            completionMeshCode,
            new ResoniteFloat3(0.0, 0.0, 0.0),
            null,
            cancellationToken);
        sceneAnchor = new SceneAnchor(
            createdAnchor.SlotId,
            completionMeshCode,
            new ResoniteFloat3(0.0, 0.0, 0.0));

        ReportProgress("[live] Dataset slots and asset groups are ready.");
        backgroundClients = [];
        cityObjectChannels = Enumerable.Range(0, connectionCount)
            .Select(_ => Channel.CreateBounded<QueuedCityObject>(
                new BoundedChannelOptions(Math.Max(MaxQueuedCityObjects, connectionCount))
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                }))
            .ToArray();
        processedCityObjectCount = 0;
        sceneBuildStopwatch = Stopwatch.StartNew();
        firstQueuedCityObjectLogged = 0;
        firstPreparedCityObjectLogged = 0;
        firstBuiltCityObjectLogged = 0;
        retriedCityObjectCount = 0;
        skippedFailedCityObjectCount = 0;
        retriedAndRecoveredCityObjectCount = 0;
        while (skippedCityObjectErrors.TryDequeue(out _))
        {
        }
        diagnostics.StartSendWindow(connectionCount);
        processingTasks = CreateProcessingTasks(metadata.Request, cancellationToken);
        ReportProgress($"[live] Send lanes ready (setup=1, workers={Math.Max(connectionCount - 1, 0)}).");
    }

    private async Task<(CreatedSlot DatasetRoot, CreatedSlot DatasetAssetsRoot, CreatedSlot CommonAssetsRoot)> CreateSetupSlotHierarchyAsync(
        IResoniteLinkClient client,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);

        CreatedSlot datasetRoot = await GetOrCreateSharedChildSlotByIdAsync(
            client,
            RootSlotId,
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 0.0, 0.0),
            null,
            cancellationToken);
        CreatedSlot datasetAssetsRoot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            "Assets",
            null,
            null,
            cancellationToken);
        CreatedSlot commonAssetsRoot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            CommonAssetsSlotName,
            null,
            null,
            cancellationToken);
        return (datasetRoot, datasetAssetsRoot, commonAssetsRoot);
    }

    private async Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        await clientInitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (setupClient is not null)
            {
                return;
            }

            IResoniteLinkClient createdClient = CreateConfiguredClient();
            try
            {
                await createdClient.ConnectAsync(endpoint, cancellationToken);
                setupClient = createdClient;
                ReportProgress(
                    $"[live] Connected setup ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
            }
            catch
            {
                createdClient.Dispose();
                throw;
            }
        }
        finally
        {
            clientInitializationGate.Release();
        }
    }

    private IResoniteLinkClient CreateConfiguredClient()
    {
        IResoniteLinkClient client = new RetryingResoniteLinkClient(
            clientFactory,
            ReportProgress);
        return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
    }

    private Task[] CreateProcessingTasks(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(backgroundClients is null, this);

        Task[] tasks = new Task[connectionCount];
        tasks[0] = ProcessQueuedCityObjectsAsync(cityObjectChannels[0].Reader, setupClient, cancellationToken);

        for (int laneIndex = 1; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ConnectWorkerAndProcessQueuedCityObjectsAsync(
                cityObjectChannels[capturedLaneIndex].Reader,
                request,
                capturedLaneIndex,
                cancellationToken);
        }

        return tasks;
    }

    private async Task ConnectWorkerAndProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(backgroundClients is null, this);

        IResoniteLinkClient client = CreateConfiguredClient();
        bool addedToBackgroundClients = false;
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken);
            backgroundClients.Add(client);
            addedToBackgroundClients = true;
            ReportProgress(
                $"[live] Connected worker ResoniteLink session {laneIndex + 1}/{connectionCount} "
                + $"to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
            await ProcessQueuedCityObjectsAsync(reader, client, cancellationToken);
        }
        catch
        {
            if (!addedToBackgroundClients)
            {
                client.Dispose();
            }

            throw;
        }
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        await AwaitProcessingTasksIfCompletedAsync();

        Task<PreparedCityObject> preparationTask = PrepareCityObjectAsync(cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object queued after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }

        ObjectDisposedException.ThrowIf(dispatchLaneAllocator is null, this);

        int dispatchLane = dispatchLaneAllocator.GetLane(cityObject);
        await cityObjectChannels[dispatchLane].Writer.WriteAsync(
            new QueuedCityObject(cityObject, preparationTask),
            cancellationToken);
        await AwaitProcessingTasksIfCompletedAsync();
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        foreach (Channel<QueuedCityObject> channel in cityObjectChannels)
        {
            channel.Writer.TryComplete();
        }

        await Task.WhenAll(processingTasks).WaitAsync(cancellationToken);
        diagnostics.CompleteSendWindow();
        ReportProgress($"[live] Completed {processedCityObjectCount} city objects.");
        ReportProgress(
            $"[live] Send summary: attempted={processedCityObjectCount + skippedFailedCityObjectCount} "
            + $"sent={processedCityObjectCount} skipped_failed={skippedFailedCityObjectCount} "
            + $"retried={retriedCityObjectCount} retried_recovered={retriedAndRecoveredCityObjectCount}.");
        if (skippedFailedCityObjectCount > 0)
        {
            string[] failureSamples = skippedCityObjectErrors.Take(3).ToArray();
            string failureSummary = failureSamples.Length == 0
                ? "See [live][warn] logs above."
                : string.Join(" | ", failureSamples);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Live send skipped {skippedFailedCityObjectCount} city object(s) after retry. {failureSummary}"));
        }

        return [$"{endpoint}#{sceneAnchor?.SlotId ?? datasetRootSlot?.SlotId ?? string.Empty}"];
    }

    public async ValueTask DisposeAsync()
    {
        if (cityObjectChannels is not null)
        {
            foreach (Channel<QueuedCityObject> channel in cityObjectChannels)
            {
                channel.Writer.TryComplete();
            }
        }

        if (processingTasks is not null)
        {
            try
            {
                await Task.WhenAll(processingTasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (setupClient is not null)
        {
            setupClient.Dispose();
        }

        if (backgroundClients is not null)
        {
            foreach (IResoniteLinkClient client in backgroundClients)
            {
                client.Dispose();
            }
        }

        setupClient = null;
        backgroundClients = null;
        metadata = null;
        datasetContentSource = null;
        datasetRootSlot = null;
        datasetAssetsRootSlot = null;
        commonAssetsRootSlot = null;
        materialAssetManager = null;
        generatedAssetsRoot = null;
        sharedSlotTasks.Clear();
        importedTextureUriTasks = null;
        dispatchLaneAllocator = null;
        textureImportResolver = null;
        cityObjectChannels = null;
        processingTasks = null;
        sceneBuildStopwatch = null;
        sceneAnchor = null;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        IResoniteLinkClient client,
        CancellationToken cancellationToken)
    {
        await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
        {
            await ProcessQueuedCityObjectWithRetryAsync(client, queuedCityObject, cancellationToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "City object send retries intentionally handle arbitrary per-object failures and retry isolated work.")]
    private async Task ProcessQueuedCityObjectWithRetryAsync(
        IResoniteLinkClient client,
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(setupClient is null, this);

        Exception? lastException = null;
        bool retried = false;

        for (int attempt = 1; attempt <= CityObjectSendAttemptLimit; attempt++)
        {
            try
            {
                PreparedCityObject preparedCityObject = attempt == 1
                    ? await queuedCityObject.PreparationTask.WaitAsync(cancellationToken)
                    : await PrepareCityObjectAsync(queuedCityObject.CityObject, cancellationToken);
                await BuildPreparedCityObjectAsync(setupClient, client, preparedCityObject, cancellationToken);

                int processedCount = Interlocked.Increment(ref processedCityObjectCount);
                if (retried)
                {
                    Interlocked.Increment(ref retriedAndRecoveredCityObjectCount);
                }

                ReportProgress(
                    $"[live] Sent city object {processedCount}: "
                    + $"{preparedCityObject.CityObject.DisplayName} "
                    + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt >= CityObjectSendAttemptLimit)
                {
                    break;
                }

                retried = true;
                Interlocked.Increment(ref retriedCityObjectCount);
                ReportProgress(
                    $"[live][warn] City object send failed on attempt {attempt}/{CityObjectSendAttemptLimit}: "
                    + $"{queuedCityObject.CityObject.DisplayName} "
                    + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
                    + $"Retrying. Reason: {exception.Message}");
            }
        }

        Interlocked.Increment(ref skippedFailedCityObjectCount);
        skippedCityObjectErrors.Enqueue(
            $"{queuedCityObject.CityObject.DisplayName} ({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}): "
            + $"{lastException?.Message ?? "Unknown error"}");
        ReportProgress(
            $"[live][warn] City object skipped after {CityObjectSendAttemptLimit} failed attempts: "
            + $"{queuedCityObject.CityObject.DisplayName} "
            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
            + $"Reason: {lastException?.Message ?? "Unknown error"}");
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(textureImportResolver is null, this);
        (string TexturePath, ResoniteTextureSourceKind TextureSourceKind)[] distinctTextures = cityObject.Materials
            .Where(static material => !string.IsNullOrWhiteSpace(material.TexturePath))
            .Select(static material => (TexturePath: material.TexturePath!, TextureSourceKind: material.TextureSourceKind))
            .Distinct()
            .OrderBy(static texture => texture.TextureSourceKind)
            .ThenBy(static texture => texture.TexturePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Task<PreparedTextureReference>[] texturePreparationTasks = distinctTextures
            .Select(async texture =>
            {
                ResoniteTextureImport textureImport = await textureImportResolver.ResolveAsync(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    cancellationToken);

                return new PreparedTextureReference(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    textureImport);
            })
            .ToArray();
        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(triangleMesh.Mesh)),
                cancellationToken),
            ResoniteHeightMapGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedHeightMapGridGeometry(heightMap, PrepareHeightMapTexture(heightMap)),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await Task.WhenAll(texturePreparationTasks);
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref firstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                + $"after scene start {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} "
                + $"(textures={preparedTextures.Length}, geometry={DescribePreparedGeometry(preparedGeometry)})");
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private static void AppendInt32(IncrementalHash incrementalHash, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        incrementalHash.AppendData(bytes);
    }

    private static void AppendBoolean(IncrementalHash incrementalHash, bool value)
    {
        incrementalHash.AppendData(new[] { value ? byte.MaxValue : byte.MinValue });
    }

    private static void AppendDouble(IncrementalHash incrementalHash, double value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        incrementalHash.AppendData(bytes);
    }

    private static void AppendString(IncrementalHash incrementalHash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(incrementalHash, bytes.Length);
        incrementalHash.AppendData(bytes);
    }

    private static string ResolveCompletionMeshCode(ResoniteConstructionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string meshCode = metadata.Request.MeshCode;
        if (PlateauMeshCode.TryGetCenter(meshCode, out _))
        {
            return meshCode;
        }

        string? requestedMeshCode = metadata.SourceDataset.RequestedMeshCodes?
            .FirstOrDefault(static candidate => PlateauMeshCode.TryGetCenter(candidate, out _));
        if (!string.IsNullOrWhiteSpace(requestedMeshCode))
        {
            return requestedMeshCode;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Live Offset V2 requires a concrete meshcode anchor, but '{meshCode}' did not resolve to any concrete meshcode."));
    }

    private async Task BuildPreparedCityObjectAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(datasetRootSlot is null, this);
        ObjectDisposedException.ThrowIf(datasetAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        ObjectSlotHierarchy objectSlots = await CreateObjectSlotHierarchyAsync(
            mutationClient,
            datasetRootSlot.Value,
            datasetAssetsRootSlot.Value,
            cityObject,
            preparedCityObject.Geometry is PreparedHeightMapGridGeometry,
            cancellationToken);

        ReportBuildStep(cityObject, $"Creating geometry component ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
        CreatedComponent geometryComponent = await CreateGeometryComponentAsync(
            mutationClient,
            importClient,
            objectSlots,
            cityObject,
            preparedCityObject,
            cancellationToken);

        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey = preparedCityObject.Textures.ToDictionary(
            static texture => ResoniteMaterialAssetManager.CreateTextureReferenceKey(
                texture.TexturePath,
                texture.TextureSourceKind),
            static texture => texture.TextureImport);
        List<string> materialIds = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            CreatedComponent materialComponent = await CreateMaterialComponentAsync(
                importClient,
                material,
                preparedTextureDataByKey,
                objectSlots,
                cancellationToken);
            materialIds.Add(materialComponent.ComponentId);
        }

        ReportBuildStep(cityObject, "Creating MeshRenderer component.");
        await CreatePresentationComponentsAsync(
            mutationClient,
            objectSlots,
            cityObject,
            geometryComponent,
            materialIds,
            cancellationToken);

        ReportBuildStep(cityObject, "Live build completed.");
        sendScope.MarkSent();

        if (Interlocked.CompareExchange(ref firstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }
    }

    private async Task<ObjectSlotHierarchy> CreateObjectSlotHierarchyAsync(
        IResoniteLinkClient client,
        CreatedSlot datasetRoot,
        CreatedSlot datasetAssetsRoot,
        ResoniteConstructionCityObject cityObject,
        bool includeHeightMapAssetSlot,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        string rootMeshCode = cityObject.ActualMeshCode;
        ResoniteFloat3 cityObjectLocalPosition = ResolveCityObjectLocalPosition(
            metadata.LocalOrigin,
            rootMeshCode,
            cityObject.Transform.Position);
        ResoniteFloat3 rootMeshCodePosition = ResolveMeshCodeRootPosition(rootMeshCode);
        CreatedSlot meshRootSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            rootMeshCode,
            rootMeshCodePosition,
            null,
            cancellationToken);
        CreatedSlot assetPackageSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            cityObject.PackageName,
            null,
            null,
            cancellationToken);
        CreatedSlot packageSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            meshRootSlot,
            cityObject.PackageName,
            null,
            null,
            cancellationToken);
        CreatedSlot assetLodSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            assetPackageSlot,
            FormatLodSlotName(cityObject.LodLevel),
            null,
            null,
            cancellationToken);
        CreatedSlot lodSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            packageSlot,
            FormatLodSlotName(cityObject.LodLevel),
            null,
            null,
            cancellationToken);
        CreatedSlot meshAssetSlot = await CreateSlotAsync(
            client,
            assetLodSlot.SlotId,
            CreateMeshAssetSlotName(cityObject),
            null,
            null,
            cancellationToken);
        CreatedSlot? heightMapAssetSlot = includeHeightMapAssetSlot
            ? await CreateSlotAsync(
                client,
                assetLodSlot.SlotId,
                CreateHeightMapAssetSlotName(cityObject),
                null,
                null,
                cancellationToken)
            : null;
        CreatedSlot cityObjectSlot = await CreateSlotAsync(
            client,
            lodSlot.SlotId,
            cityObject.DisplayName,
            cityObjectLocalPosition,
            cityObject.Transform.Rotation,
            cancellationToken);
        return new ObjectSlotHierarchy(
            meshRootSlot,
            assetPackageSlot,
            packageSlot,
            assetLodSlot,
            lodSlot,
            meshAssetSlot,
            heightMapAssetSlot,
            cityObjectSlot);
    }

    private double GetSceneElapsedSeconds()
    {
        return sceneBuildStopwatch?.Elapsed.TotalSeconds ?? 0.0;
    }

    private static string GetCityObjectIdentity(ResoniteConstructionCityObject cityObject)
    {
        if (IsDemPackage(cityObject.PackageName))
        {
            // Keep DEM identity stable even if slot/source keys vary across sibling mesh sends.
            return GetDemStructuralIdentity(cityObject);
        }

        return cityObject.SourceObjectKey ?? cityObject.SlotKey;
    }

    private static string CreateDispatchDependencyKey(ResoniteConstructionCityObject cityObject)
    {
        string objectIdentity = GetCityObjectIdentity(cityObject);
        string lodKey = cityObject.LodLevel.HasValue
            ? cityObject.LodLevel.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{cityObject.ActualMeshCode}|{cityObject.PackageName}|{lodKey}|{objectIdentity}");
    }

    private async Task<CreatedComponent> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey,
        ObjectSlotHierarchy objectSlots,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        bool useCommonMaterialAssets = ShouldUseCommonMaterialAssets(material);
        string materialScopeId = useCommonMaterialAssets
            ? commonAssetsRootSlot.Value.SlotId
            : objectSlots.MeshAssetSlot.SlotId;
        string? materialSlotParentId = useCommonMaterialAssets ? commonAssetsRootSlot.Value.SlotId : null;
        string materialSlotName = CreateMaterialSlotName(material, useCommonMaterialAssets);
        return await materialAssetManager.CreateMaterialComponentAsync(
            client,
            material,
            preparedTextureDataByKey,
            materialScopeId,
            materialSlotParentId,
            materialSlotName,
            cancellationToken);
    }

    private static bool ShouldUseCommonMaterialAssets(ResoniteMaterialBinding material)
    {
        return material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.TexturePath)
            && !IsGeneratedDemTexturePath(material.TexturePath)
            && material.TexturePath.StartsWith("default-materials/", StringComparison.Ordinal);
    }

    private static string CreateMaterialSlotName(ResoniteMaterialBinding material, bool useCommonMaterialAssets)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!useCommonMaterialAssets)
        {
            return material.MaterialKey;
        }

        string projectionName = material.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => material.Projection.ToString().ToLowerInvariant(),
        };

        string sourceName = material.TexturePath is not null
            ? Path.GetFileNameWithoutExtension(material.TexturePath).Replace("_Color", string.Empty, StringComparison.Ordinal)
            : material.MaterialType.ToString();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{material.MaterialKey}_{projectionName}_{sourceName}");
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedDemTexturePath(string? texturePath)
    {
        return !string.IsNullOrWhiteSpace(texturePath)
            && texturePath.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, StringComparison.Ordinal);
    }

    private static string GetDemStructuralIdentity(ResoniteConstructionCityObject cityObject)
    {
        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        StringBuilder identityBuilder = new(capacity: 256);

        AppendDemIdentityHeader(identityBuilder, cityObject);
        AppendUtf8HashChunk(incrementalHash, identityBuilder);

        identityBuilder.Clear();
        AppendDemIdentityMaterials(identityBuilder, cityObject.Materials);
        AppendUtf8HashChunk(incrementalHash, identityBuilder);

        identityBuilder.Clear();
        switch (cityObject.Geometry)
        {
            case ResoniteTriangleMeshGeometry triangleMesh:
                AppendDemIdentityVertices(identityBuilder, triangleMesh.Mesh.Vertices);
                AppendUtf8HashChunk(incrementalHash, identityBuilder);

                identityBuilder.Clear();
                AppendDemIdentitySubmeshes(identityBuilder, triangleMesh.Mesh.Submeshes);
                break;
            case ResoniteHeightMapGridGeometry heightMap:
                AppendDemIdentityHeightMap(identityBuilder, heightMap);
                break;
            default:
                throw new InvalidOperationException($"Unsupported DEM geometry type '{cityObject.Geometry.GetType().Name}'.");
        }

        AppendUtf8HashChunk(incrementalHash, identityBuilder);

        byte[] hashBytes = incrementalHash.GetHashAndReset();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"demshape_{Convert.ToHexString(hashBytes.AsSpan(0, 16))}");
    }

    private static void AppendUtf8HashChunk(IncrementalHash incrementalHash, StringBuilder identityBuilder)
    {
        if (identityBuilder.Length == 0)
        {
            return;
        }

        byte[] identityBytes = Encoding.UTF8.GetBytes(identityBuilder.ToString());
        incrementalHash.AppendData(identityBytes);
    }

    private static void AppendDemIdentityHeader(StringBuilder identityBuilder, ResoniteConstructionCityObject cityObject)
    {
        identityBuilder.Append(cityObject.PackageName)
            .Append('|')
            .Append(cityObject.ActualMeshCode)
            .Append('|')
            .Append(cityObject.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    private static void AppendDemIdentityMaterials(StringBuilder identityBuilder, IReadOnlyList<ResoniteMaterialBinding> materials)
    {
        foreach (ResoniteMaterialBinding material in materials)
        {
            identityBuilder.Append("|m:")
                .Append(material.MaterialKey)
                .Append(',')
                .Append(material.MaterialType)
                .Append(',')
                .Append(material.Projection)
                .Append(',')
                .Append(material.TextureSourceKind)
                .Append(',')
                .Append(material.TexturePath ?? string.Empty);

            foreach (int submeshIndex in material.SubmeshIndices)
            {
                identityBuilder.Append(',').Append(submeshIndex.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static void AppendDemIdentityVertices(StringBuilder identityBuilder, IReadOnlyList<ResoniteMeshVertex> vertices)
    {
        foreach (ResoniteMeshVertex vertex in vertices)
        {
            identityBuilder.Append("|v:")
                .Append(vertex.Position.X.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(vertex.Position.Y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(vertex.Position.Z.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(vertex.UV0.X.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(vertex.UV0.Y.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private static void AppendDemIdentitySubmeshes(StringBuilder identityBuilder, IReadOnlyList<ResoniteMeshSubmesh> submeshes)
    {
        foreach (ResoniteMeshSubmesh submesh in submeshes)
        {
            identityBuilder.Append("|s:")
                .Append(submesh.MaterialKey)
                .Append(',')
                .Append(submesh.Index.ToString(CultureInfo.InvariantCulture));

            foreach (int triangleIndex in submesh.TriangleVertexIndices)
            {
                identityBuilder.Append(',').Append(triangleIndex.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static void AppendDemIdentityHeightMap(StringBuilder identityBuilder, ResoniteHeightMapGridGeometry heightMap)
    {
        identityBuilder.Append("|g:")
            .Append(heightMap.Width.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(heightMap.Height.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(heightMap.Size.X.ToString("R", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(heightMap.Size.Y.ToString("R", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(heightMap.MinHeight.ToString("R", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(heightMap.MaxHeight.ToString("R", CultureInfo.InvariantCulture));

        foreach (double sample in heightMap.HeightSamples)
        {
            identityBuilder.Append(',').Append(sample.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private async Task<CreatedComponent> CreateGeometryComponentAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => await CreateTriangleMeshComponentAsync(
                mutationClient,
                importClient,
                objectSlots.MeshAssetSlot.SlotId,
                triangleMesh,
                cancellationToken),
            PreparedHeightMapGridGeometry heightMap => await CreateHeightMapGridComponentAsync(
                mutationClient,
                importClient,
                objectSlots.HeightMapAssetSlot!.Value.SlotId,
                objectSlots.CityObjectSlot.SlotId,
                heightMap,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private async Task<CreatedComponent> CreateTriangleMeshComponentAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        string meshAssetSlotId,
        PreparedTriangleMeshGeometry preparedGeometry,
        CancellationToken cancellationToken)
    {
        return await CreateDedicatedAssetComponentAsync(
            mutationClient,
            meshAssetSlotId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            ct => importClient.ImportMeshAsync(preparedGeometry.MeshImport, ct),
            cancellationToken);
    }

    private async Task<CreatedComponent> CreateHeightMapGridComponentAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        string meshAssetSlotId,
        string cityObjectSlotId,
        PreparedHeightMapGridGeometry preparedGeometry,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] HeightMap '{preparedGeometry.Geometry.Width}x{preparedGeometry.Geometry.Height}' importing displacement texture "
            + "via raw payload.");
        CreatedComponent heightTexture = await CreateHeightMapTextureComponentAsync(
            mutationClient,
            importClient,
            meshAssetSlotId,
            preparedGeometry.HeightTextureImport,
            cancellationToken);

        double displacementMagnitude = Math.Max(
            preparedGeometry.Geometry.MaxHeight - preparedGeometry.Geometry.MinHeight,
            0.0);
        ReportProgress(
            $"[live] HeightMap texture ready. Creating GridMesh "
            + $"({preparedGeometry.Geometry.Width}x{preparedGeometry.Geometry.Height}, displacement={displacementMagnitude:F3}).");
        CreatedComponent gridMesh = await CreateComponentAsync(
            mutationClient,
            cityObjectSlotId,
            "[FrooxEngine]FrooxEngine.GridMesh",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Points"] = new Field_int2
                {
                    Value = new int2
                    {
                        x = preparedGeometry.Geometry.Width,
                        y = preparedGeometry.Geometry.Height,
                    },
                },
                ["Size"] = new Field_float2
                {
                    Value = new float2
                    {
                        x = (float)preparedGeometry.Geometry.Size.X,
                        y = (float)preparedGeometry.Geometry.Size.Y,
                    },
                },
                ["DisplacementMagnitude"] = new Field_float
                {
                    Value = (float)displacementMagnitude,
                },
                ["DisplacementTexture"] = new Reference
                {
                    TargetID = heightTexture.ComponentId,
                },
            },
            cancellationToken);
        ReportProgress("[live] GridMesh ready.");
        return gridMesh;
    }

    private async Task<CreatedComponent> CreateHeightMapTextureComponentAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        string containerSlotId,
        ResoniteRawHdrTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await CreateAssetComponentAsync(
            mutationClient,
            containerSlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            CreateHeightMapTextureMembers(includeSamplingMembers: true),
            ct => importClient.ImportTextureAsync(textureImport, ct),
            cancellationToken);
    }

    private static Dictionary<string, Member> CreateHeightMapTextureMembers(
        bool includeSamplingMembers,
        Uri? assetUri = null)
    {
        Dictionary<string, Member> members = new(StringComparer.Ordinal)
        {
            ["Readable"] = new Field_bool { Value = true },
            ["Uncompressed"] = new Field_bool { Value = true },
            ["DirectLoad"] = new Field_bool { Value = true },
            ["MipMaps"] = new Field_bool { Value = false },
        };

        if (assetUri is not null)
        {
            members["URL"] = new Field_Uri
            {
                Value = assetUri,
            };
        }

        if (!includeSamplingMembers)
        {
            return members;
        }

        members["WrapModeU"] = new Field_Enum { Value = "Clamp" };
        members["WrapModeV"] = new Field_Enum { Value = "Clamp" };
        members["FilterMode"] = new Field_Nullable_Enum { Value = "Point" };
        return members;
    }

    private static ResoniteRawHdrTextureImport PrepareHeightMapTexture(ResoniteHeightMapGridGeometry geometry)
    {
        float[] rawPixels = new float[geometry.Width * geometry.Height * 4];
        double heightRange = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);

        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                // FrooxEngine.GridMesh uses `color.r + color.g + color.b / 3` for displacement.
                // Encode the inverted height into blue only (scaled by 3) so the effective sampled height stays 1x.
                double heightSample = geometry.HeightSamples[(y * geometry.Width) + x];
                double normalizedHeight = heightRange <= 1e-9
                    ? 0.0
                    : Math.Clamp((heightSample - geometry.MinHeight) / heightRange, 0.0, 1.0);
                float heightValue = (float)(1.0 - normalizedHeight);
                int pixelIndex = (y * geometry.Width * 4) + (x * 4);
                rawPixels[pixelIndex] = 0.0f;
                rawPixels[pixelIndex + 1] = 0.0f;
                rawPixels[pixelIndex + 2] = heightValue * 3.0f;
                rawPixels[pixelIndex + 3] = 1.0f;
            }
        }

        byte[] rawBytes = new byte[rawPixels.Length * sizeof(float)];
        Buffer.BlockCopy(rawPixels, 0, rawBytes, 0, rawBytes.Length);
        return new ResoniteRawHdrTextureImport(geometry.Width, geometry.Height, rawBytes);
    }

    private static async Task CreatePresentationComponentsAsync(
        IResoniteLinkClient client,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        CreatedComponent geometryComponent,
        IReadOnlyList<string> materialIds,
        CancellationToken cancellationToken)
    {
        await CreateComponentAsync(
            client,
            objectSlots.CityObjectSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponent.ComponentId,
                },
                ["Materials"] = new SyncList
                {
                    Elements = materialIds
                        .Select(materialId => (Member)new Reference
                        {
                            TargetID = materialId,
                        })
                        .ToList(),
                },
            },
            cancellationToken);
        await CreateComponentAsync(
            client,
            objectSlots.CityObjectSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Type"] = new Field_Enum
                {
                    Value = cityObject.CollisionEnabled ? "Static" : "NoCollision",
                },
                ["CharacterCollider"] = new Field_bool
                {
                    Value = cityObject.CollisionEnabled,
                },
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponent.ComponentId,
                },
            },
            cancellationToken);
    }

    private void ReportBuildStep(ResoniteConstructionCityObject cityObject, string step)
    {
        ReportProgress(
            $"[live] Building '{cityObject.DisplayName}' ({cityObject.PackageName}/{cityObject.SlotKey}): {step}");
    }

    private static string DescribePreparedGeometry(PreparedConstructionGeometry geometry)
    {
        return geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh =>
                $"triangle-mesh(vertices={triangleMesh.MeshImport.VertexCount}, submeshes={triangleMesh.MeshImport.Submeshes.Count})",
            PreparedHeightMapGridGeometry heightMap =>
                $"heightmap-grid({heightMap.Geometry.Width}x{heightMap.Geometry.Height})",
            _ => geometry.GetType().Name,
        };
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static Field_floatQ CreateFloatQ(ResoniteFloatQ value)
    {
        return new Field_floatQ
        {
            Value = new floatQ
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
                w = (float)value.W,
            },
        };
    }

    private static ResoniteFloat3 NormalizeMeshRootPosition(ResoniteFloat3 position)
    {
        return new ResoniteFloat3(position.X, 0.0, position.Z);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static ResoniteFloat3 ResolveCityObjectLocalPosition(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        ResoniteFloat3 cityObjectPosition)
    {
        if (!PlateauMeshCode.TryGetCenter(rootMeshCode, out ResoniteLocalOrigin rootMeshCenter))
        {
            return cityObjectPosition;
        }

        // City objects are produced in the request-local-origin frame; convert them
        // to the target mesh-code local frame because root mesh slots already carry
        // the inter-mesh-code offset in Resonite.
        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(requestOrigin, rootMeshCenter);
        return Subtract(cityObjectPosition, rootOffsetFromRequest);
    }

    private static bool TryGetMeshCodeName(Slot slot, out string meshCode)
    {
        meshCode = slot.Name?.Value ?? string.Empty;
        return PlateauMeshCode.TryGetCenter(meshCode, out _);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter)
            || !PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    private static ResoniteFloat3 ComputeOriginOffset(
        ResoniteLocalOrigin referenceCenter,
        ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(
            X: eun.x,
            // Mesh-root offsets should only reposition neighboring imports in-plane.
            // Keeping the local tangent frame's vertical component here introduces a
            // false Y drift between DEM parent meshes and detailed meshes.
            Y: 0.0,
            Z: eun.y);
    }

    private ResoniteFloat3 ResolveMeshCodeRootPosition(string meshCode)
    {
        SceneAnchor? anchor = sceneAnchor;
        if (anchor is null)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        if (string.Equals(anchor.Value.MeshCode, meshCode, StringComparison.Ordinal))
        {
            return anchor.Value.Position;
        }

        return Add(anchor.Value.Position, ComputeMeshCodeOffset(anchor.Value.MeshCode, meshCode));
    }

    private async Task AwaitProcessingTasksIfCompletedAsync()
    {
        if (processingTasks is not null && Array.Exists(processingTasks, static task => task.IsCompleted))
        {
            await Task.WhenAll(processingTasks);
        }
    }

    private static Task<CreatedSlot> CreateSlotAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return CreateSlotCoreAsync(client, parentId, slotName, position, rotation, cancellationToken);
    }

    private Task<CreatedSlot> GetOrCreateSharedChildSlotAsync(
        IResoniteLinkClient client,
        CreatedSlot parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return GetOrCreateSharedChildSlotByIdAsync(client, parent.SlotId, slotName, position, rotation, cancellationToken);
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotByIdAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        (string ParentSlotId, string SlotName) slotKey = (parentId, slotName);
        Lazy<Task<CreatedSlot>> creation = sharedSlotTasks.GetOrAdd(
            slotKey,
            _ => new Lazy<Task<CreatedSlot>>(
                () => GetOrCreateSharedChildSlotCoreAsync(
                    client,
                    parentId,
                    slotName,
                    position,
                    rotation,
                    CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        Task<CreatedSlot> sharedTask = creation.Value;

        try
        {
            CreatedSlot createdSlot = await sharedTask.WaitAsync(cancellationToken);
            await WaitForSlotAvailableAsync(client, createdSlot.SlotId, cancellationToken);
            return createdSlot;
        }
        catch
        {
            if (sharedTask.IsFaulted || sharedTask.IsCanceled)
            {
                sharedSlotTasks.TryRemove(
                    new KeyValuePair<(string ParentSlotId, string SlotName), Lazy<Task<CreatedSlot>>>(slotKey, creation));
            }

            throw;
        }
    }

    private static async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, parentId, cancellationToken);
        CreatedSlot? existingSlot = await TryGetUniqueChildSlotByNameAsync(
            client,
            parentId,
            slotName,
            cancellationToken);
        if (existingSlot is not null)
        {
            return existingSlot.Value;
        }

        return await CreateSlotCoreAsync(client, parentId, slotName, position, rotation, cancellationToken);
    }

    private static async Task<CreatedComponent> CreateComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, containerSlotId, cancellationToken);
        string createdComponentId = await client.AddComponentAsync(
            CreateAddComponentOperation(containerSlotId, componentType, members),
            cancellationToken);
        await WaitForComponentVisibleAsync(client, createdComponentId, cancellationToken);
        await WaitForComponentAttachedToSlotAsync(client, containerSlotId, createdComponentId, cancellationToken);
        return new CreatedComponent(createdComponentId, componentType);
    }

    private Task<CreatedComponent> CreateSharedAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        return CreateAssetComponentAsync(
            client,
            containerSlotId,
            componentType,
            new Dictionary<string, Member>(StringComparer.Ordinal),
            importAssetAsync,
            cancellationToken);
    }

    private Task<CreatedComponent> CreateDedicatedAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        return CreateAssetComponentAsync(
            client,
            containerSlotId,
            componentType,
            new Dictionary<string, Member>(StringComparer.Ordinal),
            importAssetAsync,
            cancellationToken);
    }

    private async Task<CreatedComponent> CreateAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        ReportProgress($"[live] Importing asset for component type '{componentType}'.");
        Uri assetUri = await importAssetAsync(cancellationToken);
        ReportProgress($"[live] Asset import completed for component type '{componentType}' -> '{assetUri}'.");
        Dictionary<string, Member> componentMembers = new(members, StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };
        return await CreateComponentAsync(
            client,
            containerSlotId,
            componentType,
            componentMembers,
            cancellationToken);
    }

    private static async Task<CreatedSlot> CreateSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, parentId, cancellationToken);
        string createdSlotId = await client.AddSlotAsync(
            CreateAddSlotOperation(parentId, slotName, position, rotation),
            cancellationToken);
        await WaitForSlotVisibleAsync(client, createdSlotId, parentId, cancellationToken);
        return new CreatedSlot(createdSlotId, slotName);
    }

    private static async Task WaitForSlotVisibleAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? slot = await client.GetSlotAsync(slotId, 0, cancellationToken);
            if (slot is not null)
            {
                return;
            }

            Slot? parentSlot = await client.GetSlotAsync(parentId, 1, cancellationToken);
            if (parentSlot?.Children?.Any(child => string.Equals(child.ID, slotId, StringComparison.Ordinal)) == true)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slotId}' after creation.");
    }

    private static async Task WaitForSlotAvailableAsync(
        IResoniteLinkClient client,
        string slotId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(slotId, RootSlotId, StringComparison.Ordinal))
        {
            return;
        }

        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? slot = await client.GetSlotAsync(slotId, 0, cancellationToken);
            if (slot is not null)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slotId}'.");
    }

    private static async Task WaitForComponentVisibleAsync(
        IResoniteLinkClient client,
        string componentId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Component? component = await client.GetComponentAsync(componentId, cancellationToken);
            if (component is not null)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ResoniteLink did not surface component '{componentId}' after creation.");
    }

    private static async Task WaitForComponentAttachedToSlotAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? containerSlot = await client.GetSlotAsync(containerSlotId, 0, cancellationToken);
            if (containerSlot?.Components?.Any(component => string.Equals(component.ID, componentId, StringComparison.Ordinal)) == true)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"ResoniteLink did not attach component '{componentId}' to slot '{containerSlotId}' after creation.");
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        Slot? parentSlot = await client.GetSlotAsync(parentId, 1, cancellationToken);
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matches = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Parent slot '{parentId}' contains multiple child slots named '{slotName}'.");
        }

        string existingSlotId = matches[0].ID
            ?? throw new InvalidOperationException(
                $"Child slot '{slotName}' under parent '{parentId}' did not surface an ID.");
        return new CreatedSlot(existingSlotId, slotName);
    }

    private async Task<Uri> ImportTextureAsync(
        IResoniteLinkClient client,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        TextureImportCacheKey? cacheKey = TryCreateTextureImportCacheKey(textureImport);
        if (cacheKey is null)
        {
            return await client.ImportTextureAsync(textureImport, cancellationToken);
        }

        ObjectDisposedException.ThrowIf(importedTextureUriTasks is null, this);
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        IResoniteLinkClient cachedImportClient = setupClient;
        Lazy<Task<Uri>> importTask = importedTextureUriTasks.GetOrAdd(
            cacheKey.Value,
            _ => new Lazy<Task<Uri>>(
                () => cachedImportClient.ImportTextureAsync(textureImport, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        Task<Uri> sharedTask = importTask.Value;
        try
        {
            return await sharedTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (sharedTask.IsFaulted || sharedTask.IsCanceled)
            {
                importedTextureUriTasks.TryRemove(
                    new KeyValuePair<TextureImportCacheKey, Lazy<Task<Uri>>>(cacheKey.Value, importTask));
            }

            throw;
        }
    }

    private static TextureImportCacheKey? TryCreateTextureImportCacheKey(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
            ResoniteFileTextureImport fileImport => new TextureImportCacheKey("file", fileImport.AbsolutePath),
            ResoniteRawTextureImport rawImport when rawImport.Identity is not null => new TextureImportCacheKey(
                "raw",
                rawImport.Identity,
                rawImport.ColorProfile),
            _ => null,
        };
    }

    private static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation)
    {
        return new AddSlot
        {
            Data = new Slot
            {
                ID = null!,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        ResoniteLicenseComponentMetadata license)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = license.RequireCredit,
            },
            ["CreditString"] = new Field_string
            {
                Value = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{license.CreditText} License: {license.LicenseName} ({license.LicenseUrl})"),
            },
        };
    }

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members)
    {
        return new AddComponent
        {
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = null!,
                ComponentType = componentType,
                Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
            },
        };
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static string CreateMeshAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.DisplayName;
    }

    private static string CreateHeightMapAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return string.Concat(CreateMeshAssetSlotName(cityObject), HeightMapAssetSlotSuffix);
    }

    internal readonly record struct CreatedSlot(
        string SlotId,
        string SlotName);

    internal readonly record struct CreatedComponent(
        string ComponentId,
        string ComponentType);

    private sealed record ObjectSlotHierarchy(
        CreatedSlot MeshRootSlot,
        CreatedSlot AssetPackageSlot,
        CreatedSlot PackageSlot,
        CreatedSlot AssetLodSlot,
        CreatedSlot LodSlot,
        CreatedSlot MeshAssetSlot,
        CreatedSlot? HeightMapAssetSlot,
        CreatedSlot CityObjectSlot);

    internal sealed class DispatchLaneAllocator
    {
        private readonly int connectionCount;
        private readonly ConcurrentDictionary<string, int> lanesByDependencyKey = new(StringComparer.Ordinal);
        private int nextLane = -1;

        public DispatchLaneAllocator(int connectionCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);
            this.connectionCount = connectionCount;
        }

        public int GetLane(ResoniteConstructionCityObject cityObject)
        {
            ArgumentNullException.ThrowIfNull(cityObject);

            if (connectionCount == 1)
            {
                return 0;
            }

            string dependencyKey = CreateDispatchDependencyKey(cityObject);
            return lanesByDependencyKey.GetOrAdd(
                dependencyKey,
                _ => Interlocked.Increment(ref nextLane) % connectionCount);
        }
    }

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask);

    private readonly record struct SceneAnchor(
        string SlotId,
        string MeshCode,
        ResoniteFloat3 Position);

    private abstract record PreparedConstructionGeometry;

    private sealed record PreparedTriangleMeshGeometry(
        ImportMeshRawData MeshImport)
        : PreparedConstructionGeometry;

    private sealed record PreparedHeightMapGridGeometry(
        ResoniteHeightMapGridGeometry Geometry,
        ResoniteRawHdrTextureImport HeightTextureImport)
        : PreparedConstructionGeometry;

    private sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        PreparedConstructionGeometry Geometry,
        IReadOnlyList<PreparedTextureReference> Textures)
    {
        public bool TryGetTextureImport(
            string texturePath,
            ResoniteTextureSourceKind textureSourceKind,
            out ResoniteTextureImport? textureImport)
        {
            PreparedTextureReference? preparedTexture = Textures.FirstOrDefault(texture =>
                string.Equals(texture.TexturePath, texturePath, StringComparison.Ordinal)
                && texture.TextureSourceKind == textureSourceKind);
            textureImport = preparedTexture?.TextureImport;
            return preparedTexture is not null;
        }
    }

    private sealed record PreparedTextureReference(
        string TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteTextureImport TextureImport);
}
