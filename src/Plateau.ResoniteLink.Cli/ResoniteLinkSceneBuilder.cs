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
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private const double SlotPositionTolerance = 0.0001;
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly SemaphoreSlim clientInitializationGate = new(1, 1);
    private readonly Action<string>? progressReporter;
    private List<IResoniteLinkClient>? clients;
    private ResoniteConstructionMetadata? metadata;
    private string? datasetSlotId;
    private string? meshCodeSlotId;
    private string? datasetAssetsSlotId;
    private string? commonAssetsSlotId;
    private ResoniteMaterialAssetManager? materialAssetManager;
    private ResoniteLicenseManager? licenseManager;
    private string? generatedAssetsRoot;
    private ConcurrentDictionary<string, Lazy<Task>>? slotEnsureTasks;
    private ConcurrentDictionary<string, Lazy<Task>>? componentEnsureTasks;
    private ConcurrentDictionary<string, Lazy<Task<Uri>>>? assetComponentEnsureTasks;
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
    private IPlateauDatasetContentSource? datasetContentSource;

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

        await EnsureClientsConnectedAsync(request, cancellationToken);
    }

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        this.metadata = metadata;
        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        Directory.CreateDirectory(resolvedWorkRoot);
        generatedAssetsRoot = Path.Combine(resolvedWorkRoot, ".generated-assets");
        string completionMeshCode = ResolveCompletionMeshCode(metadata);
        datasetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "dataset");
        meshCodeSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            completionMeshCode,
            "meshcode");
        datasetAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assets");
        string datasetLicenseComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "license");
        commonAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "common");

        await EnsureClientsConnectedAsync(metadata.Request, cancellationToken);
        ObjectDisposedException.ThrowIf(clients is null, this);
        IResoniteLinkClient setupClient = clients[0];
        licenseManager = new ResoniteLicenseManager(metadata.Attribution);
        slotEnsureTasks = new ConcurrentDictionary<string, Lazy<Task>>(StringComparer.Ordinal);
        componentEnsureTasks = new ConcurrentDictionary<string, Lazy<Task>>(StringComparer.Ordinal);
        assetComponentEnsureTasks = new ConcurrentDictionary<string, Lazy<Task<Uri>>>(StringComparer.Ordinal);
        dispatchLaneAllocator = new DispatchLaneAllocator(connectionCount);
        materialAssetManager = new ResoniteMaterialAssetManager(
            (containerSlotId, componentId, componentType, uriMemberName, importAssetAsync, ct) =>
                EnsureStaticAssetComponentUrlKnownAsync(
                    setupClient,
                    containerSlotId,
                    componentId,
                    componentType,
                    uriMemberName,
                    importAssetAsync,
                    ct),
            (containerSlotId, componentId, componentType, uriMemberName, importAssetAsync, ct) =>
                UpsertDedicatedAssetComponentUrlAsync(
                    setupClient,
                    containerSlotId,
                    componentId,
                    componentType,
                    uriMemberName,
                    importAssetAsync,
                    ct),
            (slotId, parentId, slotName, ct) =>
                EnsureAssetSlotKnownAsync(
                    setupClient,
                    slotId,
                    parentId,
                    slotName,
                    ct),
            (containerSlotId, componentId, componentType, members, ct) =>
                EnsureComponentKnownAsync(
                    setupClient,
                    containerSlotId,
                    componentId,
                    componentType,
                    members,
                    ct),
            ReportProgress);
        PlateauLocalImportSource localSource = metadata.Request.Source as PlateauLocalImportSource
            ?? throw new InvalidOperationException("Live scene building requires a resolved local dataset source.");

        datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(localSource.LocalSourcePath!, cancellationToken);
        textureImportResolver = new ResoniteTextureImportResolver(
            datasetContentSource,
            generatedAssetsRoot,
            metadata.SourceDataset.TerrainTextureOverlays,
            terrainTextureAssetGenerator);
        cityObjectChannels = clients
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
        diagnostics.StartSendWindow(connectionCount);
        processingTasks = clients
            .Zip(
                cityObjectChannels,
                (client, channel) => ProcessQueuedCityObjectsAsync(channel.Reader, client, cancellationToken))
            .ToArray();

        await EnsureSlotKnownAsync(
            setupClient,
            datasetSlotId,
            "Root",
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 0.0, 0.0),
            null,
            cancellationToken);
        await EnsureSlotYPositionAsync(
            setupClient,
            datasetSlotId,
            "Root",
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 0.0, 0.0),
            cancellationToken);
        await licenseManager.EnsureDatasetLicenseAsync(setupClient, datasetSlotId, datasetLicenseComponentId, cancellationToken);
        await EnsureSlotsKnownBatchAsync(
            setupClient,
            [
                CreateSlotDefinition(datasetAssetsSlotId, datasetSlotId, "Assets"),
                CreateSlotDefinition(commonAssetsSlotId, datasetAssetsSlotId, CommonAssetsSlotName),
            ],
            cancellationToken);
        SceneAnchor? existingAnchor = await ResolveSceneAnchorAsync(setupClient, cancellationToken);
        if (existingAnchor is null)
        {
            await EnsureSlotKnownAsync(
                setupClient,
                meshCodeSlotId,
                datasetSlotId,
                completionMeshCode,
                new ResoniteFloat3(0.0, 0.0, 0.0),
                null,
                cancellationToken);
            await EnsureSlotPositionAsync(
                setupClient,
                meshCodeSlotId,
                datasetSlotId,
                completionMeshCode,
                new ResoniteFloat3(0.0, 0.0, 0.0),
                cancellationToken);
        }
        else
        {
            await EnsureSlotPositionAsync(
                setupClient,
                existingAnchor.Value.SlotId,
                datasetSlotId,
                existingAnchor.Value.MeshCode,
                existingAnchor.Value.Position,
                cancellationToken);
        }

        ReportProgress("[live] Dataset slots and asset groups are ready.");
    }

    private async Task EnsureClientsConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        await clientInitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (clients is not null)
            {
                return;
            }

            List<IResoniteLinkClient> createdClients = Enumerable.Range(0, connectionCount)
                .Select(_ =>
                {
                    IResoniteLinkClient client = new RetryingResoniteLinkClient(
                        clientFactory,
                        ReportProgress);
                    return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
                })
                .ToList();

            try
            {
                await Task.WhenAll(createdClients.Select(client => client.ConnectAsync(endpoint, cancellationToken)));
                clients = createdClients;
                ReportProgress(
                    $"[live] Connected {connectionCount} ResoniteLink sessions to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
            }
            catch
            {
                foreach (IResoniteLinkClient client in createdClients)
                {
                    client.Dispose();
                }

                throw;
            }
        }
        finally
        {
            clientInitializationGate.Release();
        }
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(clients is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);

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
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
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
        return [$"{endpoint}#{meshCodeSlotId}"];
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

        if (clients is not null)
        {
            foreach (IResoniteLinkClient client in clients)
            {
                client.Dispose();
            }
        }

        clients = null;
        metadata = null;
        datasetContentSource = null;
        datasetSlotId = null;
        meshCodeSlotId = null;
        datasetAssetsSlotId = null;
        commonAssetsSlotId = null;
        materialAssetManager = null;
        licenseManager = null;
        generatedAssetsRoot = null;
        slotEnsureTasks = null;
        componentEnsureTasks = null;
        assetComponentEnsureTasks = null;
        dispatchLaneAllocator = null;
        textureImportResolver = null;
        cityObjectChannels = null;
        processingTasks = null;
        sceneBuildStopwatch = null;
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
        Exception? lastException = null;
        bool retried = false;

        for (int attempt = 1; attempt <= CityObjectSendAttemptLimit; attempt++)
        {
            try
            {
                PreparedCityObject preparedCityObject = attempt == 1
                    ? await queuedCityObject.PreparationTask.WaitAsync(cancellationToken)
                    : await PrepareCityObjectAsync(queuedCityObject.CityObject, cancellationToken);
                await BuildPreparedCityObjectAsync(client, preparedCityObject, cancellationToken);

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
        IResoniteLinkClient client,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(datasetSlotId is null, this);
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(datasetAssetsSlotId is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsSlotId is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        string rootMeshCode = cityObject.ActualMeshCode;
        string rootMeshCodeSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            rootMeshCode,
            "meshcode");
        ResoniteFloat3 rootMeshCodePosition = await ResolveMeshCodeRootPositionAsync(client, rootMeshCode, cancellationToken);
        ResoniteFloat3 cityObjectLocalPosition = ResolveCityObjectLocalPosition(
            metadata.LocalOrigin,
            rootMeshCode,
            cityObject.Transform.Position);
        await EnsureSlotKnownAsync(
            client,
            rootMeshCodeSlotId,
            datasetSlotId,
            rootMeshCode,
            rootMeshCodePosition,
            null,
            cancellationToken);
        await EnsureSlotPositionAsync(
            client,
            rootMeshCodeSlotId,
            datasetSlotId,
            rootMeshCode,
            rootMeshCodePosition,
            cancellationToken);

        string objectIdentity = GetCityObjectIdentity(cityObject);
        string cityObjectSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            rootMeshCode,
            "cityobject",
            objectIdentity);
        string meshAssetLogicalKey = CreateMeshAssetLogicalKey(cityObject);
        string meshAssetSlotId = GetAssetSlotId(
            metadata.Request.Dataset,
            rootMeshCode,
            meshAssetLogicalKey);
        string heightMapAssetLogicalKey = CreateHeightMapAssetLogicalKey(meshAssetLogicalKey);
        string heightMapAssetSlotId = GetAssetSlotId(
            metadata.Request.Dataset,
            rootMeshCode,
            heightMapAssetLogicalKey);
        string rendererId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            rootMeshCode,
            "renderer",
            objectIdentity);
        string colliderId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            rootMeshCode,
            "collider",
            objectIdentity);
        string packageSlotId = GetMeshCodePackageSlotId(metadata.Request.Dataset, rootMeshCode, cityObject.PackageName);
        string lodSlotId = GetMeshCodeLodSlotId(metadata.Request.Dataset, rootMeshCode, cityObject.PackageName, cityObject.LodLevel);
        string assetPackageSlotId = GetAssetPackageSlotId(metadata.Request.Dataset, rootMeshCode, cityObject.PackageName);
        string assetLodSlotId = GetAssetLodSlotId(metadata.Request.Dataset, rootMeshCode, cityObject.PackageName, cityObject.LodLevel);

        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        ReportBuildStep(cityObject, "Ensuring package and LOD slots.");

        await EnsureSlotKnownAsync(
            client,
            packageSlotId,
            rootMeshCodeSlotId,
            cityObject.PackageName,
            null,
            null,
            cancellationToken);
        await EnsureSlotKnownAsync(client, lodSlotId, packageSlotId, FormatLodSlotName(cityObject.LodLevel), null, null, cancellationToken);
        Slot? existingCityObjectSlot = await client.GetSlotAsync(cityObjectSlotId, 0, cancellationToken);
        if (existingCityObjectSlot is null)
        {
            await AddSlotDirectAsync(
                client,
                cityObjectSlotId,
                lodSlotId,
                cityObject.DisplayName,
                cityObjectLocalPosition,
                cityObject.Transform.Rotation,
                cancellationToken);
        }

        await EnsureSlotKnownAsync(client, assetPackageSlotId, datasetAssetsSlotId, cityObject.PackageName, null, null, cancellationToken);
        await EnsureSlotKnownAsync(client, assetLodSlotId, assetPackageSlotId, FormatLodSlotName(cityObject.LodLevel), null, null, cancellationToken);

        await EnsureMeshAssetSlotKnownAsync(
            client,
            meshAssetSlotId,
            meshAssetLogicalKey,
            assetLodSlotId,
            cancellationToken);
        if (preparedCityObject.Geometry is PreparedHeightMapGridGeometry)
        {
            await EnsureAssetSlotKnownAsync(
                client,
                heightMapAssetSlotId,
                assetLodSlotId,
                heightMapAssetLogicalKey,
                cancellationToken);
        }

        ReportBuildStep(cityObject, $"Ensuring geometry component ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
        string geometryComponentId = await EnsureGeometryComponentAsync(
            client,
            preparedCityObject.Geometry is PreparedHeightMapGridGeometry ? heightMapAssetSlotId : meshAssetSlotId,
            cityObjectSlotId,
            metadata.Request.Dataset,
            rootMeshCode,
            objectIdentity,
            preparedCityObject,
            cancellationToken);

        Dictionary<string, ResoniteTextureImport> preparedTextureDataByKey = preparedCityObject.Textures.ToDictionary(
            static texture => ResoniteMaterialAssetManager.CreateTextureCacheKey(
                texture.TexturePath,
                texture.TextureSourceKind),
            static texture => texture.TextureImport,
            StringComparer.OrdinalIgnoreCase);
        List<string> materialIds = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Ensuring material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            bool useCommonMaterialAssets = ShouldUseCommonMaterialAssets(material);
            string materialInstanceKey = CreateScopedMaterialInstanceKey(
                material,
                useCommonMaterialAssets ? null : meshAssetSlotId);
            string materialSlotId = useCommonMaterialAssets
                ? GetCommonMaterialAssetSlotId(metadata.Request.Dataset, materialInstanceKey)
                : meshAssetSlotId;
            string? materialSlotParentId = useCommonMaterialAssets ? commonAssetsSlotId : null;
            string materialSlotName = CreateMaterialSlotName(material, useCommonMaterialAssets);
            string materialId = await materialAssetManager.EnsureMaterialComponentAsync(
                client,
                material,
                preparedTextureDataByKey,
                materialSlotId,
                materialSlotParentId,
                materialSlotName,
                materialInstanceKey,
                cancellationToken);
            materialIds.Add(materialId);
        }

        ReportBuildStep(cityObject, "Ensuring MeshRenderer component.");
        ComponentDefinition rendererDefinition = CreateComponentDefinition(
            cityObjectSlotId,
            rendererId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponentId,
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
            });
        ComponentDefinition colliderDefinition = CreateComponentDefinition(
            cityObjectSlotId,
            colliderId,
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
                    TargetID = geometryComponentId,
                },
            });
        if (existingCityObjectSlot is null)
        {
            await EnsureComponentsKnownBatchAsync(
                client,
                [rendererDefinition, colliderDefinition],
                cancellationToken);
        }
        else
        {
            await UpsertComponentMembersAsync(
                client,
                rendererDefinition.ContainerSlotId,
                rendererDefinition.ComponentId,
                rendererDefinition.ComponentType,
                rendererDefinition.Members,
                cancellationToken);
            await UpsertComponentMembersAsync(
                client,
                colliderDefinition.ContainerSlotId,
                colliderDefinition.ComponentId,
                colliderDefinition.ComponentType,
                colliderDefinition.Members,
                cancellationToken);
        }

        ReportBuildStep(cityObject, "Live build completed.");
        sendScope.MarkSent();

        if (Interlocked.CompareExchange(ref firstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }
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

    private static string CreateScopedMaterialInstanceKey(ResoniteMaterialBinding material, string? scopeKey)
    {
        ArgumentNullException.ThrowIfNull(material);

        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(incrementalHash, scopeKey ?? string.Empty);
        AppendString(incrementalHash, material.MaterialType.ToString());
        AppendString(incrementalHash, material.Projection.ToString());
        AppendString(incrementalHash, material.TextureSourceKind.ToString());
        AppendString(incrementalHash, material.TexturePath ?? string.Empty);
        AppendString(incrementalHash, material.Family ?? string.Empty);
        AppendDouble(incrementalHash, material.BaseColor.R);
        AppendDouble(incrementalHash, material.BaseColor.G);
        AppendDouble(incrementalHash, material.BaseColor.B);
        AppendDouble(incrementalHash, material.BaseColor.A);

        if (material.TextureScale is not null)
        {
            AppendBoolean(incrementalHash, true);
            AppendDouble(incrementalHash, material.TextureScale.X);
            AppendDouble(incrementalHash, material.TextureScale.Y);
        }
        else
        {
            AppendBoolean(incrementalHash, false);
        }

        if (material.TextureOffset is not null)
        {
            AppendBoolean(incrementalHash, true);
            AppendDouble(incrementalHash, material.TextureOffset.X);
            AppendDouble(incrementalHash, material.TextureOffset.Y);
        }
        else
        {
            AppendBoolean(incrementalHash, false);
        }

        if (material.DepthOffset is not null)
        {
            AppendBoolean(incrementalHash, true);
            AppendDouble(incrementalHash, material.DepthOffset.Factor);
            AppendDouble(incrementalHash, material.DepthOffset.Units);
        }
        else
        {
            AppendBoolean(incrementalHash, false);
        }

        string materialFingerprint = Convert.ToHexString(incrementalHash.GetHashAndReset());
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{material.MaterialKey}_{materialFingerprint[..16]}");
    }

    private static bool ShouldUseCommonMaterialAssets(ResoniteMaterialBinding material)
    {
        return material.TextureSourceKind == ResoniteTextureSourceKind.Bundled;
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

    private async Task<string> EnsureGeometryComponentAsync(
        IResoniteLinkClient client,
        string meshAssetSlotId,
        string cityObjectSlotId,
        string dataset,
        string rootMeshCode,
        string objectIdentity,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => await EnsureTriangleMeshComponentAsync(
                client,
                meshAssetSlotId,
                dataset,
                rootMeshCode,
                objectIdentity,
                triangleMesh,
                cancellationToken),
            PreparedHeightMapGridGeometry heightMap => await EnsureHeightMapGridComponentAsync(
                client,
                meshAssetSlotId,
                cityObjectSlotId,
                dataset,
                rootMeshCode,
                objectIdentity,
                heightMap,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private async Task<string> EnsureTriangleMeshComponentAsync(
        IResoniteLinkClient client,
        string meshAssetSlotId,
        string dataset,
        string rootMeshCode,
        string objectIdentity,
        PreparedTriangleMeshGeometry preparedGeometry,
        CancellationToken cancellationToken)
    {
        string staticMeshId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "assetcomponent",
            rootMeshCode,
            objectIdentity,
            "mesh");
        await UpsertDedicatedAssetComponentUrlAsync(
            client,
            meshAssetSlotId,
            staticMeshId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            "URL",
            () => client.ImportMeshAsync(preparedGeometry.MeshImport, cancellationToken),
            cancellationToken);
        return staticMeshId;
    }

    private async Task<string> EnsureHeightMapGridComponentAsync(
        IResoniteLinkClient client,
        string meshAssetSlotId,
        string cityObjectSlotId,
        string dataset,
        string rootMeshCode,
        string objectIdentity,
        PreparedHeightMapGridGeometry preparedGeometry,
        CancellationToken cancellationToken)
    {
        string heightTextureId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "assetcomponent",
            rootMeshCode,
            objectIdentity,
            "heightmap-texture");
        string gridMeshId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "gridmesh",
            rootMeshCode,
            objectIdentity);

        ReportProgress(
            $"[live] HeightMap '{preparedGeometry.Geometry.Width}x{preparedGeometry.Geometry.Height}' importing displacement texture "
            + "via raw payload.");
        await UpsertHeightMapTextureComponentAsync(
            client,
            meshAssetSlotId,
            heightTextureId,
            preparedGeometry.HeightTextureImport,
            cancellationToken);
        ReportProgress(
            $"[live] HeightMap texture imported for '{objectIdentity}'. Updating StaticTexture2D settings.");
        await UpdateComponentMembersAsync(
            client,
            heightTextureId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            CreateHeightMapTextureMembers(includeSamplingMembers: true),
            cancellationToken);

        double displacementMagnitude = Math.Max(
            preparedGeometry.Geometry.MaxHeight - preparedGeometry.Geometry.MinHeight,
            0.0);
        ReportProgress(
            $"[live] HeightMap texture ready for '{objectIdentity}'. Updating GridMesh "
            + $"({preparedGeometry.Geometry.Width}x{preparedGeometry.Geometry.Height}, displacement={displacementMagnitude:F3}).");
        await UpsertComponentMembersAsync(
            client,
            cityObjectSlotId,
            gridMeshId,
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
                    TargetID = heightTextureId,
                },
            },
            cancellationToken);
        ReportProgress($"[live] GridMesh ready for '{objectIdentity}'.");
        return gridMeshId;
    }

    private async Task UpsertHeightMapTextureComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        ResoniteRawHdrTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        ReportProgress(
            $"[live] Importing asset for component '{componentId}' ([FrooxEngine]FrooxEngine.StaticTexture2D).");
        Uri assetUri = await client.ImportTextureAsync(textureImport, cancellationToken);
        ReportProgress(
            $"[live] Asset import completed for component '{componentId}' ([FrooxEngine]FrooxEngine.StaticTexture2D) -> '{assetUri}'.");

        if (existingComponent is not null)
        {
            await UpdateComponentMembersAsync(
                client,
                componentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateHeightMapTextureMembers(includeSamplingMembers: false, assetUri),
                cancellationToken);
            return;
        }

        await AddHeightMapTextureComponentAsync(
            client,
            containerSlotId,
            componentId,
            assetUri,
            cancellationToken);
    }

    private async Task AddHeightMapTextureComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        Uri assetUri,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] Adding asset component '{componentId}' ([FrooxEngine]FrooxEngine.StaticTexture2D) to slot '{containerSlotId}'.");
        await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = containerSlotId,
                Data = new Component
                {
                    ID = componentId,
                    ComponentType = "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    Members = CreateHeightMapTextureMembers(includeSamplingMembers: true, assetUri),
                },
            },
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

    private async Task UpsertComponentMembersAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (existingComponent is null)
        {
            ReportProgress(
                $"[live] Adding component '{componentId}' ({componentType}) to slot '{containerSlotId}'.");
            await client.AddComponentAsync(
                new AddComponent
                {
                    ContainerSlotId = containerSlotId,
                    Data = new Component
                    {
                        ID = componentId,
                        ComponentType = componentType,
                        Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                    },
                },
                cancellationToken);
            return;
        }

        await UpdateComponentMembersAsync(client, componentId, componentType, members, cancellationToken);
    }

    private async Task UpdateComponentMembersAsync(
        IResoniteLinkClient client,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] Updating component '{componentId}' ({componentType}).");
        await client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = componentId,
                    Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                },
            },
            cancellationToken);
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

    private static ResoniteFloat3 GetPositionOrDefault(Slot slot)
    {
        return slot.Position is Field_float3 position
            ? new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z)
            : new ResoniteFloat3(0.0, 0.0, 0.0);
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

    private static async Task EnsureSlotYPositionAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3 expectedPosition,
        CancellationToken cancellationToken)
    {
        Slot? existingSlot = await client.GetSlotAsync(slotId, 0, cancellationToken);
        if (existingSlot is null)
        {
            return;
        }

        if (IsApproximatelyEqual(expectedPosition.Y, existingSlot.Position))
        {
            return;
        }

        ResoniteFloat3 nextPosition = new(
            existingSlot.Position?.Value.x ?? expectedPosition.X,
            expectedPosition.Y,
            existingSlot.Position?.Value.z ?? expectedPosition.Z);
        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = slotId,
                    Parent = existingSlot.Parent ?? new Reference
                    {
                        TargetID = parentId,
                    },
                    Name = string.IsNullOrWhiteSpace(existingSlot.Name?.Value)
                        ? new Field_string { Value = slotName }
                        : existingSlot.Name,
                    Position = CreateFloat3(nextPosition),
                    Rotation = existingSlot.Rotation,
                },
            },
            cancellationToken);
    }

    private static bool IsApproximatelyEqual(double expectedY, Field_float3? existingPosition)
    {
        return existingPosition is not null
            && Math.Abs(existingPosition.Value.y - (float)expectedY) < SlotPositionTolerance;
    }

    private static bool IsApproximatelyEqual(ResoniteFloat3 expectedPosition, Field_float3? existingPosition)
    {
        return existingPosition is not null
            && Math.Abs(existingPosition.Value.x - (float)expectedPosition.X) < SlotPositionTolerance
            && Math.Abs(existingPosition.Value.y - (float)expectedPosition.Y) < SlotPositionTolerance
            && Math.Abs(existingPosition.Value.z - (float)expectedPosition.Z) < SlotPositionTolerance;
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

    private async Task<ResoniteFloat3> ResolveMeshCodeRootPositionAsync(
        IResoniteLinkClient client,
        string meshCode,
        CancellationToken cancellationToken)
    {
        SceneAnchor? anchor = await ResolveSceneAnchorAsync(client, cancellationToken);
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

    private static async Task EnsureSlotPositionAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3 expectedPosition,
        CancellationToken cancellationToken)
    {
        Slot? existingSlot = await client.GetSlotAsync(slotId, 0, cancellationToken);
        if (existingSlot is null)
        {
            return;
        }

        if (IsApproximatelyEqual(expectedPosition, existingSlot.Position))
        {
            return;
        }

        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = slotId,
                    Parent = existingSlot.Parent ?? new Reference
                    {
                        TargetID = parentId,
                    },
                    Name = string.IsNullOrWhiteSpace(existingSlot.Name?.Value)
                        ? new Field_string { Value = slotName }
                        : existingSlot.Name,
                    Position = CreateFloat3(expectedPosition),
                    Rotation = existingSlot.Rotation,
                },
            },
            cancellationToken);
    }

    private async Task<SceneAnchor?> ResolveSceneAnchorAsync(
        IResoniteLinkClient client,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(datasetSlotId is null, this);

        Slot? datasetSlot = await client.GetSlotAsync(datasetSlotId, 1, cancellationToken);
        Slot? anchorSlot = datasetSlot?.Children?.FirstOrDefault(static slot => TryGetMeshCodeName(slot, out _));
        if (anchorSlot is null || !TryGetMeshCodeName(anchorSlot, out string anchorMeshCode))
        {
            return null;
        }

        return new SceneAnchor(
            anchorSlot.ID,
            anchorMeshCode,
            NormalizeMeshRootPosition(GetPositionOrDefault(anchorSlot)));
    }

    private async Task AwaitProcessingTasksIfCompletedAsync()
    {
        if (processingTasks is not null && Array.Exists(processingTasks, static task => task.IsCompleted))
        {
            await Task.WhenAll(processingTasks);
        }
    }

    private async Task EnsureSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(slotEnsureTasks is null, this);
        await GetOrRunOnceAsync(
            slotEnsureTasks,
            slotId,
            () => EnsureSlotAsync(client, slotId, parentId, slotName, position, rotation, cancellationToken),
            cancellationToken);
    }

    private async Task EnsureMeshAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        string parentId,
        CancellationToken cancellationToken)
    {
        await EnsureAssetSlotKnownAsync(client, slotId, parentId, slotName, cancellationToken);
    }

    private async Task EnsureAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        await EnsureSlotKnownAsync(client, slotId, parentId, slotName, null, null, cancellationToken);
    }

    private async Task<Uri> EnsureStaticAssetComponentUrlKnownAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(assetComponentEnsureTasks is null, this);
        return await GetOrRunOnceAsync(
            assetComponentEnsureTasks,
            componentId,
            () => EnsureStaticAssetComponentUrlAsync(
                client,
                containerSlotId,
                componentId,
                componentType,
                uriMemberName,
                importAssetAsync,
                cancellationToken),
            cancellationToken);
    }

    private static async Task EnsureSlotsKnownBatchAsync(
        IResoniteLinkClient client,
        IReadOnlyList<SlotDefinition> definitions,
        CancellationToken cancellationToken)
    {
        if (definitions.Count == 0)
        {
            return;
        }

        List<SlotDefinition> missingDefinitions = [];
        foreach (SlotDefinition definition in definitions)
        {
            Slot? existingSlot = await client.GetSlotAsync(definition.SlotId, 0, cancellationToken);
            if (existingSlot is null)
            {
                missingDefinitions.Add(definition);
            }
        }

        if (missingDefinitions.Count == 0)
        {
            return;
        }

        if (missingDefinitions.Count == 1)
        {
            SlotDefinition definition = missingDefinitions[0];
            await AddSlotDirectAsync(
                client,
                definition.SlotId,
                definition.ParentId,
                definition.SlotName,
                definition.Position,
                definition.Rotation,
                cancellationToken);
            return;
        }

        await client.RunDataModelOperationBatchAsync(
            missingDefinitions
                .Select(static definition => (DataModelOperation)CreateAddSlotOperation(
                    definition.SlotId,
                    definition.ParentId,
                    definition.SlotName,
                    definition.Position,
                    definition.Rotation))
                .ToArray(),
            cancellationToken);
    }

    private async Task EnsureComponentKnownAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(componentEnsureTasks is null, this);
        await GetOrRunOnceAsync(
            componentEnsureTasks,
            componentId,
            () => EnsureComponentAsync(
                client,
                containerSlotId,
                componentId,
                componentType,
                members,
                cancellationToken),
            cancellationToken);
    }

    private static async Task EnsureComponentsKnownBatchAsync(
        IResoniteLinkClient client,
        IReadOnlyList<ComponentDefinition> definitions,
        CancellationToken cancellationToken)
    {
        if (definitions.Count == 0)
        {
            return;
        }

        List<ComponentDefinition> missingDefinitions = [];
        foreach (ComponentDefinition definition in definitions)
        {
            Component? existingComponent = await client.GetComponentAsync(definition.ComponentId, cancellationToken);
            if (existingComponent is null)
            {
                missingDefinitions.Add(definition);
            }
        }

        if (missingDefinitions.Count == 0)
        {
            return;
        }

        if (missingDefinitions.Count == 1)
        {
            ComponentDefinition definition = missingDefinitions[0];
            await AddComponentDirectAsync(
                client,
                definition.ContainerSlotId,
                definition.ComponentId,
                definition.ComponentType,
                definition.Members,
                cancellationToken);
            return;
        }

        await client.RunDataModelOperationBatchAsync(
            missingDefinitions
                .Select(static definition => (DataModelOperation)CreateAddComponentOperation(
                    definition.ContainerSlotId,
                    definition.ComponentId,
                    definition.ComponentType,
                    definition.Members))
                .ToArray(),
            cancellationToken);
    }

    private static async Task GetOrRunOnceAsync(
        ConcurrentDictionary<string, Lazy<Task>> tasks,
        string key,
        Func<Task> factory,
        CancellationToken cancellationToken)
    {
        Lazy<Task> lazyTask = tasks.GetOrAdd(
            key,
            _ => new Lazy<Task>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
        await lazyTask.Value.WaitAsync(cancellationToken);
    }

    private static async Task<T> GetOrRunOnceAsync<T>(
        ConcurrentDictionary<string, Lazy<Task<T>>> tasks,
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        Lazy<Task<T>> lazyTask = tasks.GetOrAdd(
            key,
            _ => new Lazy<Task<T>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazyTask.Value.WaitAsync(cancellationToken);
    }

    private static async Task EnsureSlotAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        Slot? existingSlot = await client.GetSlotAsync(slotId, 0, cancellationToken);
        if (existingSlot is not null)
        {
            return;
        }

        await client.AddSlotAsync(
            CreateAddSlotOperation(slotId, parentId, slotName, position, rotation),
            cancellationToken);
    }

    private static async Task AddSlotDirectAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        await client.AddSlotAsync(
            CreateAddSlotOperation(slotId, parentId, slotName, position, rotation),
            cancellationToken);
    }

    private async Task<Uri> EnsureStaticAssetComponentUrlAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (TryGetUri(existingComponent, uriMemberName) is Uri existingUri)
        {
            ReportProgress(
                $"[live] Reusing existing asset component '{componentId}' ({componentType}) on logical asset slot '{containerSlotId}'.");
            return existingUri;
        }

        return await ImportAndUpsertAssetComponentUrlAsync(
            client,
            containerSlotId,
            componentId,
            componentType,
            uriMemberName,
            importAssetAsync,
            existingComponent,
            cancellationToken);
    }

    private async Task<Uri> UpsertDedicatedAssetComponentUrlAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        return await ImportAndUpsertAssetComponentUrlAsync(
            client,
            containerSlotId,
            componentId,
            componentType,
            uriMemberName,
            importAssetAsync,
            existingComponent,
            cancellationToken);
    }

    private async Task<Uri> ImportAndUpsertAssetComponentUrlAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        Component? existingComponent,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] Importing asset for component '{componentId}' ({componentType}).");
        Uri assetUri = await importAssetAsync();
        ReportProgress(
            $"[live] Asset import completed for component '{componentId}' ({componentType}) -> '{assetUri}'.");
        Dictionary<string, Member> members = new(StringComparer.Ordinal)
        {
            [uriMemberName] = new Field_Uri
            {
                Value = assetUri,
            },
        };

        if (existingComponent is null)
        {
            ReportProgress(
                $"[live] Adding asset component '{componentId}' ({componentType}) to slot '{containerSlotId}'.");
            await client.AddComponentAsync(
                new AddComponent
                {
                    ContainerSlotId = containerSlotId,
                    Data = new Component
                    {
                        ID = componentId,
                        ComponentType = componentType,
                        Members = members,
                    },
                },
                cancellationToken);
        }
        else
        {
            ReportProgress(
                $"[live] Updating asset component '{componentId}' ({componentType}) on slot '{containerSlotId}'.");
            await client.UpdateComponentAsync(
                new UpdateComponent
                {
                    Data = new Component
                    {
                        ID = componentId,
                        Members = members,
                    },
                },
                cancellationToken);
        }
        return assetUri;
    }

    private async Task EnsureComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (existingComponent is not null)
        {
            ReportProgress($"[live] Updating existing component '{componentId}' ({componentType}).");
            await client.UpdateComponentAsync(
                new UpdateComponent
                {
                    Data = new Component
                    {
                        ID = componentId,
                        Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                    },
                },
                cancellationToken);
            return;
        }

        ReportProgress(
            $"[live] Adding component '{componentId}' ({componentType}) to slot '{containerSlotId}'.");
        await client.AddComponentAsync(
            CreateAddComponentOperation(containerSlotId, componentId, componentType, members),
            cancellationToken);
    }

    private static async Task AddComponentDirectAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        await client.AddComponentAsync(
            CreateAddComponentOperation(containerSlotId, componentId, componentType, members),
            cancellationToken);
    }

    private static SlotDefinition CreateSlotDefinition(
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position = null,
        ResoniteFloatQ? rotation = null)
    {
        return new SlotDefinition(slotId, parentId, slotName, position, rotation);
    }

    private static ComponentDefinition CreateComponentDefinition(
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members)
    {
        return new ComponentDefinition(containerSlotId, componentId, componentType, members);
    }

    private static AddSlot CreateAddSlotOperation(
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation)
    {
        return new AddSlot
        {
            Data = new Slot
            {
                ID = slotId,
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

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members)
    {
        return new AddComponent
        {
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = componentId,
                ComponentType = componentType,
                Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
            },
        };
    }

    private static Uri? TryGetUri(Component? component, string memberName)
    {
        if (component is null
            || !component.Members.TryGetValue(memberName, out Member? member)
            || member is not Field_Uri fieldUri)
        {
            return null;
        }

        return fieldUri.Value;
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static string GetMeshCodePackageSlotId(string dataset, string meshCode, string packageName)
    {
        return ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, meshCode, "package", packageName);
    }

    private static string GetMeshCodeLodSlotId(string dataset, string meshCode, string packageName, int? lodLevel)
    {
        return ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, meshCode, "lod", packageName, FormatLodSlotName(lodLevel));
    }

    private static string GetAssetPackageSlotId(string dataset, string meshCode, string packageName)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "assetpackage", meshCode, packageName);
    }

    private static string GetAssetLodSlotId(string dataset, string meshCode, string packageName, int? lodLevel)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "assetlod", meshCode, packageName, FormatLodSlotName(lodLevel));
    }

    private static string GetAssetSlotId(string dataset, string meshCode, string logicalKey)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "asset", meshCode, logicalKey);
    }

    private static string GetCommonMaterialAssetSlotId(string dataset, string logicalKey)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "commonasset", logicalKey);
    }

    private static string CreateMeshAssetLogicalKey(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.DisplayName;
    }

    private static string CreateHeightMapAssetLogicalKey(string meshAssetLogicalKey)
    {
        return string.Concat(meshAssetLogicalKey, HeightMapAssetSlotSuffix);
    }

    private sealed record SlotDefinition(
        string SlotId,
        string ParentId,
        string SlotName,
        ResoniteFloat3? Position,
        ResoniteFloatQ? Rotation);

    private sealed record ComponentDefinition(
        string ContainerSlotId,
        string ComponentId,
        string ComponentType,
        IReadOnlyDictionary<string, Member> Members);

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
