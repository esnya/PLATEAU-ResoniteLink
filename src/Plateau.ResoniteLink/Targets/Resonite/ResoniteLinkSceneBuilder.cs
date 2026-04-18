using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

using GeographicLib;

using ResoniteLink;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

public sealed class ResoniteLinkSceneBuilder : ISceneImportTarget
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;
    private const string RootSlotId = "Root";
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private const string CityGmlSlotDuplicateSuffixFormat = "{0:D4}";
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly ResoniteGeometryAssetAssembler geometryAssetAssembler;
#pragma warning disable CA1859
    private readonly ILiveSendClientSession clientSession;
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;
#pragma warning disable CA1859
    private readonly IResoniteSceneBootstrapCoordinator sceneBootstrapCoordinator;
#pragma warning restore CA1859
    private ResoniteSceneRunState? activeRunState;

    private SceneBootstrapInfo? bootstrapInfo
    {
        get => activeRunState?.BootstrapInfo;
        set => GetMutableRunState().BootstrapInfo = value;
    }

    private CreatedSlot? datasetRootSlot
    {
        get => activeRunState?.DatasetRootSlot;
        set => GetMutableRunState().DatasetRootSlot = value;
    }

    private CreatedSlot? datasetAssetsRootSlot
    {
        get => activeRunState?.DatasetAssetsRootSlot;
        set => GetMutableRunState().DatasetAssetsRootSlot = value;
    }

    private CreatedSlot? commonAssetsRootSlot
    {
        get => activeRunState?.CommonAssetsRootSlot;
        set => GetMutableRunState().CommonAssetsRootSlot = value;
    }

    private string? runRoot
    {
        get => activeRunState?.RunRoot;
        set => GetMutableRunState().RunRoot = value;
    }

    private AsyncCompletedResultCache<(string ParentSlotId, string SlotName), CreatedSlot> sharedSlotCache
        => GetMutableRunState().SharedSlotCache;

    private AsyncCompletedResultCache<CanonicalParentScopeKey, CanonicalParentScope> canonicalParentScopeCache
        => GetMutableRunState().CanonicalParentScopeCache;

    private ConcurrentDictionary<string, byte> createdSlotIds
        => GetMutableRunState().CreatedSlotIds;

    private ConcurrentDictionary<string, Task> commonMaterialFamilyWarmupTasks
        => GetMutableRunState().CommonMaterialFamilyWarmupTasks;

    private ConcurrentDictionary<string, Task<CreatedMaterialAsset>> commonMaterialCreationTasks
        => GetMutableRunState().CommonMaterialCreationTasks;

    private ConcurrentDictionary<string, CreatedSlot> sharedSlotIndex
        => GetMutableRunState().SharedSlotIndex;

    private ConcurrentDictionary<string, Slot> observedSlotSnapshotsById
        => GetMutableRunState().ObservedSlotSnapshotsById;

    private AsyncCompletedResultCache<TextureImportCacheKey, Uri>? importedTextureUriCache
    {
        get => activeRunState?.ImportedTextureUriCache;
        set => GetMutableRunState().ImportedTextureUriCache = value;
    }

    private Channel<QueuedCityObject>? cityObjectChannel
    {
        get => activeRunState?.CityObjectChannel;
        set => GetMutableRunState().CityObjectChannel = value;
    }

    private Task[]? processingTasks
    {
        get => activeRunState?.ProcessingTasks;
        set => GetMutableRunState().ProcessingTasks = value;
    }

    private CancellationTokenSource? processingCancellationSource
    {
        get => activeRunState?.ProcessingCancellationSource;
        set => GetMutableRunState().ProcessingCancellationSource = value;
    }

    private TaskCompletionSource<Exception>? firstProcessingFailureSource
    {
        get => activeRunState?.FirstProcessingFailureSource;
        set => GetMutableRunState().FirstProcessingFailureSource = value;
    }

    private CompositeCityObjectBaker? cityObjectBaker
    {
        get => activeRunState?.CityObjectBaker;
        set => GetMutableRunState().CityObjectBaker = value;
    }

    private AsyncWeightedGate? cityObjectMemoryGate
    {
        get => activeRunState?.CityObjectMemoryGate;
        set => GetMutableRunState().CityObjectMemoryGate = value;
    }

    private ref int attemptedCityObjectCount => ref GetMutableRunState().AttemptedCityObjectCount;

    private ref int processedCityObjectCount => ref GetMutableRunState().ProcessedCityObjectCount;

    private ref int failedCityObjectCount => ref GetMutableRunState().FailedCityObjectCount;

    private Stopwatch? sceneBuildStopwatch
    {
        get => activeRunState?.SceneBuildStopwatch;
        set => GetMutableRunState().SceneBuildStopwatch = value;
    }

    private ref int firstQueuedCityObjectLogged => ref GetMutableRunState().FirstQueuedCityObjectLogged;

    private ref int firstPreparedCityObjectLogged => ref GetMutableRunState().FirstPreparedCityObjectLogged;

    private ref int firstBuiltCityObjectLogged => ref GetMutableRunState().FirstBuiltCityObjectLogged;

    private ref int firstCityObjectPreparationStartedLogged => ref GetMutableRunState().FirstCityObjectPreparationStartedLogged;

    private ref int firstCommonMaterialPrepLogged => ref GetMutableRunState().FirstCommonMaterialPrepLogged;

    private ref int firstCityObjectStreamingStartedLogged => ref GetMutableRunState().FirstCityObjectStreamingStartedLogged;

    private ref int firstCityObjectDequeuedLogged => ref GetMutableRunState().FirstCityObjectDequeuedLogged;

    private IPlateauDatasetContentSource? datasetContentSource
    {
        get => activeRunState?.DatasetContentSource;
        set => GetMutableRunState().DatasetContentSource = value;
    }

    private SceneAnchor? sceneAnchor
    {
        get => activeRunState?.SceneAnchor;
        set => GetMutableRunState().SceneAnchor = value;
    }

    private ResoniteLocalOrigin? requestLocalOrigin
    {
        get => activeRunState?.RequestLocalOrigin;
        set => GetMutableRunState().RequestLocalOrigin = value;
    }

    private string? datasetLicenseComponentId
    {
        get => activeRunState?.DatasetLicenseComponentId;
        set => GetMutableRunState().DatasetLicenseComponentId = value;
    }

    private Dictionary<string, string>? cityGmlSlotNamesByRelativePath
    {
        get => activeRunState?.CityGmlSlotNamesByRelativePath;
        set => GetMutableRunState().CityGmlSlotNamesByRelativePath = value;
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(
            endpoint,
            4,
            ResoniteLinkSendDiagnostics.Disabled,
            CreateDefaultDependencies(
                endpoint,
                4,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, int connectionCount, Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            ResoniteLinkSendDiagnostics.Disabled,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            diagnostics,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        bool enableMeshBake,
        Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            diagnostics,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ResoniteLinkSceneBuilderDependencies dependencies,
        bool enableMeshBake = true,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.TerrainTextureAssetGenerator);

        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.diagnostics = diagnostics;
        this.terrainTextureAssetGenerator = dependencies.TerrainTextureAssetGenerator;
        MeshBakeEnabled = enableMeshBake;
        this.progressReporter = progressReporter;
        sceneBootstrapCoordinator = new ResoniteSceneBootstrapCoordinator(
            TryGetDatasetRootAsync,
            UpdateComponentAsync);
        geometryAssetAssembler = new ResoniteGeometryAssetAssembler(ReportProgress);
        clientSession = dependencies.ClientSession;
    }

    internal static ResoniteLinkSceneBuilderDependencies CreateDefaultDependencies(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
        Action<string>? progressReporter = null,
        Func<IResoniteLinkClient>? baseClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLinkSceneBuilderDependencies(
            ResoniteLinkTransportSessionFactory.Create(
                endpoint,
                connectionCount,
                diagnostics,
                progressReporter,
                baseClientFactory),
            terrainTextureAssetGenerator);
    }

    internal bool MeshBakeEnabled { get; }

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cityObjects);
        if (activeRunState is not null)
        {
            throw new InvalidOperationException("A live scene build run is already active on this scene builder instance.");
        }

        activeRunState = new ResoniteSceneRunState
        {
            RequestLocalOrigin = SceneImportContractMapper.ToInternal(plan.SceneBuildRequest.Metadata).LocalOrigin,
        };
        bool completedSuccessfully = false;

        try
        {
            SceneBuildRequest request = plan.SceneBuildRequest;
            await BeginCoreAsync(
                CreateBootstrapInfo(request),
                request.WorkRoot,
                request.DatasetContentSource,
                CommonMaterialCatalog.CreateForPackages(request.Metadata.SourceDataset.PackageNames),
                plan.NormalizedRequest,
                cancellationToken);

            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                await ProcessCityObjectCoreAsync(SceneImportContractMapper.ToInternal(cityObject), cancellationToken);
            }

            IReadOnlyList<string> destinations = await CompleteCoreAsync(cancellationToken);
            completedSuccessfully = true;
            return new SceneImportExecutionResult(
                destinations,
                processedCityObjectCount,
                failedCityObjectCount);
        }
        finally
        {
            await ResetRunStateAsync(
                disposeClients: false,
                resetClients: !completedSuccessfully);
        }
    }

    private async Task BeginCoreAsync(
        SceneBootstrapInfo bootstrapInfo,
        string workRoot,
        IPlateauDatasetContentSource? datasetContentSource,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        PlateauImportRequest normalizedRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrapInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        if (activeRunState is null)
        {
            throw new InvalidOperationException("Live scene run state must be initialized before starting execution.");
        }

        if (this.bootstrapInfo is not null || processingTasks is not null)
        {
            throw new InvalidOperationException("A live scene build run is already active on this scene builder instance.");
        }

        this.bootstrapInfo = bootstrapInfo;
        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        Directory.CreateDirectory(resolvedWorkRoot);
        runRoot = CreateRunRoot(resolvedWorkRoot);
        Directory.CreateDirectory(runRoot);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{bootstrapInfo.Dataset}' "
                + $"mesh '{bootstrapInfo.MeshCode}' at '{resolvedWorkRoot}'."));
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {endpoint} "
                + $"with {connectionCount} available routed connection(s)."));
        await clientSession.EnsureConnectedAsync(normalizedRequest, cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{bootstrapInfo.Dataset}', mesh='{bootstrapInfo.MeshCode}')."));
        IResoniteLinkClient routedClient = GetRoutedClient();
        firstCommonMaterialPrepLogged = 0;
        commonMaterialFamilyWarmupTasks.Clear();
        commonMaterialCreationTasks.Clear();
        sharedSlotCache.Clear();
        canonicalParentScopeCache.Clear();
        createdSlotIds.Clear();
        sharedSlotIndex.Clear();
        observedSlotSnapshotsById.Clear();
        importedTextureUriCache = new();
        string localSourcePath = datasetContentSource?.SourcePath ?? bootstrapInfo.LocalSourcePath;
        if (datasetContentSource is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localSourcePath);
            Stopwatch contentSourceStopwatch = Stopwatch.StartNew();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Opening resolved dataset content source '{localSourcePath}'."));
            datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(localSourcePath, cancellationToken);
            contentSourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Dataset content source opened in {contentSourceStopwatch.Elapsed.TotalSeconds:F2}s."));
            this.datasetContentSource = datasetContentSource;
        }
        else
        {
            this.datasetContentSource = datasetContentSource;
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    "Reusing dataset content source provided by caller."));
        }
        ResoniteTextureImageLoader textureImageLoader = new();
        ReportProgress(
            PlateauLog.Info("live", "Setting up mutable helpers (baker)."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                "Starting setup slot bootstrap: dataset root, assets root, common assets root, location slot, and source-file root reference."));
        Stopwatch bootstrapStopwatch = Stopwatch.StartNew();
        ResoniteSceneBootstrapState bootstrapState = await sceneBootstrapCoordinator.BootstrapAsync(
            routedClient,
            bootstrapInfo,
            commonMaterials,
            cancellationToken);
        bootstrapStopwatch.Stop();
        datasetRootSlot = bootstrapState.DatasetRootSlot;
        datasetAssetsRootSlot = bootstrapState.DatasetAssetsRootSlot;
        commonAssetsRootSlot = bootstrapState.CommonAssetsRootSlot;
        sceneAnchor = bootstrapState.SceneAnchor;
        IndexBootstrapHierarchy(bootstrapState);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Scene bootstrap complete in {bootstrapStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset_root={datasetRootSlot.Value.SlotName}, assets_root={datasetAssetsRootSlot.Value.SlotName}, "
                + $"common_root={commonAssetsRootSlot.Value.SlotName}, "
                + $"dataset_root_existed={bootstrapState.DatasetRootExisted}, "
                + $"location_slot='{bootstrapState.SceneAnchor.LocationSlotId}', "
                + $"anchor_mesh='{bootstrapState.SceneAnchor.MeshCode}', "
                + $"anchor_source_file_root='{bootstrapState.SceneAnchor.ReferenceSourceFileRootId ?? "<pending>"}')."));
        foreach ((string materialKey, CreatedMaterialAsset materialAsset) in bootstrapState.CommonMaterialAssetsByKey)
        {
            commonMaterialCreationTasks[materialKey] = Task.FromResult(materialAsset);
        }

        foreach (string family in bootstrapState.CommonMaterialFamilies)
        {
            commonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        if (bootstrapState.CommonMaterialAssetsByKey.Count > 0)
        {
            firstCommonMaterialPrepLogged = bootstrapState.CommonMaterialAssetsByKey.Count;
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Setup prepared {bootstrapState.CommonMaterialAssetsByKey.Count} common materials in bootstrap."));
        }
        else
        {
            ReportProgress(PlateauLog.Info("live", "No common materials needed setup creation during bootstrap."));
        }

        ReportProgress(
            PlateauLog.Info(
                "live",
                "Bootstrap fixed dataset license metadata/component before city-object streaming starts."));
        terrainTextureAssetGenerator.ResetUsageTracking();
        datasetLicenseComponentId = bootstrapState.DatasetLicenseComponentId;

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during bootstrap. "
                + $"Dataset root existed={bootstrapState.DatasetRootExisted}."));
        clientSession.BeginWorkerClientTracking();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
        cityObjectChannel = Channel.CreateBounded<QueuedCityObject>(
            new BoundedChannelOptions(Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount))
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        processedCityObjectCount = 0;
        attemptedCityObjectCount = 0;
        failedCityObjectCount = 0;
        sceneBuildStopwatch = Stopwatch.StartNew();
        firstQueuedCityObjectLogged = 0;
        firstPreparedCityObjectLogged = 0;
        firstBuiltCityObjectLogged = 0;
        firstCityObjectPreparationStartedLogged = 0;
        firstCityObjectStreamingStartedLogged = 0;
        firstCityObjectDequeuedLogged = 0;
        cityGmlSlotNamesByRelativePath = CreateCityGmlSlotNamesByRelativePath(bootstrapInfo.SourceFiles);
        cityObjectBaker = MeshBakeEnabled
            ? new CompositeCityObjectBaker(
                new Lod2AtlasCityObjectBaker(textureImageLoader),
                new FixedCellCityObjectMeshBaker())
            : null;
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        cityObjectMemoryGate = new AsyncWeightedGate(
            Math.Max(
                MaxInFlightCityObjectWorkingSetBytesFloor,
                connectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane));
        diagnostics.StartSendWindow(connectionCount);
        processingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstProcessingFailureSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        processingTasks = CreateProcessingTasks(bootstrapInfo, processingCancellationSource.Token);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount)})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private Task<CreatedMaterialAsset> CreateOrStartCommonMaterialWarmupTask(
        IResoniteLinkClient setupClient,
        string commonAssetsSlotId,
        ResoniteMaterialBinding material,
        Task priorFamilyTask,
        CancellationToken cancellationToken)
    {
        return CreateOrStartCommonMaterialWarmupTaskCore(setupClient, commonAssetsSlotId, material, priorFamilyTask, cancellationToken);
    }

    private async Task<CreatedMaterialAsset> CreateOrStartCommonMaterialWarmupTaskCore(
        IResoniteLinkClient setupClient,
        string commonAssetsSlotId,
        ResoniteMaterialBinding material,
        Task priorFamilyTask,
        CancellationToken cancellationToken)
    {
        await priorFamilyTask.ConfigureAwait(false);
        Stopwatch commonMaterialStopwatch = Stopwatch.StartNew();
        PlannedDedicatedMaterialAsset plannedMaterial = await ResoniteMaterialPlanning.PlanCommonMaterialAssetAsync(
            setupClient,
            material,
            cancellationToken);
        CreatedMaterialAsset asset = await ResoniteMaterialPlanning.EmitCommonMaterialAsync(
            setupClient,
            plannedMaterial,
            commonAssetsSlotId,
            CreateMaterialSlotName(material, useCommonMaterialAssets: true),
            (client, parentSlotId, slotName, ct) =>
                GetOrCreateSharedChildSlotByIdAsync(client, parentSlotId, slotName, null, null, ct),
            CreateComponentAsync,
            cancellationToken);
        commonMaterialStopwatch.Stop();
        int completedCount = Interlocked.Increment(ref firstCommonMaterialPrepLogged);
        if (completedCount == 1 || completedCount % 25 == 0)
        {
            ReportProgress(
                $"[live] Common material '{CreateMaterialSlotName(material, useCommonMaterialAssets: true)}' prepared in {commonMaterialStopwatch.Elapsed.TotalSeconds:F3}s "
                + $"(count={completedCount}).");
        }

        return asset;
    }

    private Task<CreatedSlot?> TryGetDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        return TryGetDatasetRootCoreAsync(client, slotName, cancellationToken);
    }

    private static async Task<CreatedSlot?> TryGetDatasetRootCoreAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        ResoniteSceneSlotSnapshot snapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            RootSlotId,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, RootSlotId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(lookup.SlotId!, slotName)
            : null;
    }

    private Task[] CreateProcessingTasks(
        SceneBootstrapInfo bootstrapInfo,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);

        Task[] tasks = new Task[connectionCount];
        for (int laneIndex = 0; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
                cityObjectChannel.Reader,
                bootstrapInfo,
                capturedLaneIndex,
                cancellationToken);
        }

        return tasks;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        QueuedCityObject? currentCityObject = null;
        try
        {
            if (Interlocked.CompareExchange(ref firstCityObjectStreamingStartedLogged, 1, 0) == 0)
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"City-object send pipeline is active and waiting for queue on lane {laneIndex + 1}/{connectionCount}."));
            }

            await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref firstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    ReportProgress(
                        PlateauLog.Info(
                            "live",
                            $"First city object dequeued on lane {laneIndex + 1}/{connectionCount} "
                            + $"after scene-start {GetSceneElapsedSeconds():F3}s: "
                            + $"{queuedCityObject.CityObject.DisplayName} "
                            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey})."));
                }

                await ProcessQueuedCityObjectAsync(queuedCityObject, cancellationToken);
                currentCityObject = null;
            }

            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Send lane {laneIndex + 1}/{connectionCount} drained."));
        }
        catch (OperationCanceledException)
        {
            ReportProgress($"[live][warn] Send lane {laneIndex + 1}/{connectionCount} canceled.");
            throw;
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            string cityObjectContext = currentCityObject is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $" while processing '{currentCityObject.CityObject.DisplayName}' "
                    + $"({currentCityObject.CityObject.PackageName}/{currentCityObject.CityObject.SlotKey}) "
                    + $"mesh='{currentCityObject.CityObject.ActualMeshCode}' "
                    + $"sourceUnit='{currentCityObject.CityObject.SourceUnitKey ?? "<null>"}'");
            ReportProgress($"[live][error] Send lane {laneIndex + 1}/{connectionCount} failed{cityObjectContext}: {exception.Message}");
            throw;
        }
    }

    private async Task ProcessQueuedCityObjectsOnLaneAsync(
        ChannelReader<QueuedCityObject> reader,
        SceneBootstrapInfo bootstrapInfo,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch laneClientStopwatch = Stopwatch.StartNew();
        if (laneIndex == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{connectionCount} is ready to consume from the routed connection pool."));
        }
        else
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Preparing send worker {laneIndex + 1}/{connectionCount} "
                    + $"against routed connections to {endpoint} for dataset '{bootstrapInfo.Dataset}' mesh '{bootstrapInfo.MeshCode}'."));
        }
        laneClientStopwatch.Stop();
        try
        {
            if (laneIndex == 0)
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Send worker {laneIndex + 1}/{connectionCount} ready against routed connections "
                        + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            }
            else
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Send worker {laneIndex + 1}/{connectionCount} ready against routed connections "
                        + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            }
            await ProcessQueuedCityObjectsAsync(reader, laneIndex, cancellationToken);
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            throw;
        }
    }

    private async Task ProcessCityObjectCoreAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(bootstrapInfo is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        if (cityObjectBaker is not null)
        {
            IReadOnlyList<ResoniteConstructionCityObject> queuedCityObjects = await cityObjectBaker.BufferAsync(
                cityObject,
                cancellationToken);
            if (queuedCityObjects.Count == 0)
            {
                return;
            }

            foreach (ResoniteConstructionCityObject queuedCityObject in queuedCityObjects)
            {
                await EnqueueCityObjectAsync(queuedCityObject, cancellationToken);
            }

            return;
        }

        await EnqueueCityObjectAsync(cityObject, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> CompleteCoreAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);
        ObjectDisposedException.ThrowIf(bootstrapInfo is null, this);
        ObjectDisposedException.ThrowIf(datasetRootSlot is null, this);
        IResoniteLinkClient routedClient = GetRoutedClient();

        if (cityObjectBaker is not null)
        {
            (string Name, int InputCount, int OutputCount)[] pendingBakeSummaries = cityObjectBaker
                .GetBakeSummaries()
                .Where(static summary => summary.InputCount > 0)
                .ToArray();
            if (pendingBakeSummaries.Length > 0)
            {
                string summaryText = string.Join(
                    ", ",
                    pendingBakeSummaries.Select(static summary =>
                        $"{summary.Name}: input={summary.InputCount}, currentOutput={summary.OutputCount}"));
                ReportProgress(PlateauLog.Info("live", $"Starting buffered bake flush: {summaryText}."));
            }

            Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
            int bakedCityObjectCount = 0;
            List<Task> bakeEnqueueTasks = [];
            int maxInFlightBakeEnqueueTasks = Math.Max(4, connectionCount * 2);
            await cityObjectBaker.FlushAllAsync(
                async (bakedCityObject, callbackCancellationToken) =>
                {
                    _ = Interlocked.Increment(ref bakedCityObjectCount);
                    bakeEnqueueTasks.Add(EnqueueCityObjectAsync(bakedCityObject, callbackCancellationToken));
                    if (bakeEnqueueTasks.Count >= maxInFlightBakeEnqueueTasks)
                    {
                        await AwaitOneTaskSlotAsync(bakeEnqueueTasks, callbackCancellationToken);
                    }
                },
                cancellationToken);
            if (bakeEnqueueTasks.Count > 0)
            {
                await Task.WhenAll(bakeEnqueueTasks).WaitAsync(cancellationToken);
            }
            bakeFlushStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Buffered bake flush produced {bakedCityObjectCount} baked city objects "
                    + $"in {bakeFlushStopwatch.Elapsed.TotalSeconds:F3}s."));

            foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
            {
                ReportProgress(
                    $"[live] {name} batched {inputCount} input city objects "
                    + $"into {outputCount} baked batch objects.");
            }
        }

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Completing live send. Closing lane writers (attempted={attemptedCityObjectCount}, "
                + $"prepared={processedCityObjectCount}, failed={failedCityObjectCount})."));
        cityObjectChannel.Writer.TryComplete();

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Awaiting {processingTasks.Length} send lane task(s) to drain after queue close."));
        Task allProcessingTasks = Task.WhenAll(processingTasks);
        if (firstProcessingFailureSource is not null)
        {
            Task completedTask = await Task.WhenAny(allProcessingTasks, firstProcessingFailureSource.Task).WaitAsync(cancellationToken);
            if (completedTask == firstProcessingFailureSource.Task)
            {
                CancelProcessing();
                Exception failure = await firstProcessingFailureSource.Task.WaitAsync(cancellationToken);
                throw failure;
            }
        }

        await allProcessingTasks.WaitAsync(cancellationToken);
        ReportProgress(PlateauLog.Info("live", "All send lanes drained and completion barrier passed."));
        datasetLicenseComponentId = await sceneBootstrapCoordinator.ApplyDatasetLicenseAsync(
            routedClient,
            datasetRootSlot.Value.SlotId,
            terrainTextureAssetGenerator.ResolveDatasetLicense(bootstrapInfo.DatasetLicense),
            datasetLicenseComponentId,
            allowUpdateExisting: true,
            cancellationToken);
        diagnostics.CompleteSendWindow();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Completed {processedCityObjectCount} city objects "
                + $"(failed={failedCityObjectCount}, attempted={attemptedCityObjectCount})."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send summary: attempted={attemptedCityObjectCount} sent={processedCityObjectCount} failed={failedCityObjectCount}."));

        return [$"{endpoint}#{sceneAnchor?.LocationSlotId ?? datasetRootSlot?.SlotId ?? string.Empty}"];
    }

    public async ValueTask DisposeAsync()
    {
        await ResetRunStateAsync(
            disposeClients: true,
            resetClients: false);
    }

    private async ValueTask ResetRunStateAsync(bool disposeClients, bool resetClients)
    {
        ResoniteSceneRunState? runState = activeRunState;
        if (runState is not null)
        {
            runState.CityObjectChannel?.Writer.TryComplete();

            try
            {
                if (runState.ProcessingCancellationSource is not null)
                {
                    await runState.ProcessingCancellationSource.CancelAsync();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (runState.ProcessingTasks is not null)
            {
                Task[] drainTasks = runState.ProcessingTasks
                    .Select(static task => task.ContinueWith(
                        static completedTask => _ = completedTask.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default))
                    .ToArray();
                await Task.WhenAll(drainTasks);
            }
        }

        try
        {
            if (disposeClients)
            {
                clientSession.DisposeClients();
            }
            else if (resetClients)
            {
                await clientSession.ResetClientsAsync();
            }
        }
        finally
        {
            activeRunState = null;
            if (runState is not null)
            {
                runState.ProcessingCancellationSource?.Dispose();
                TryDeleteDirectory(runState.RunRoot);
            }
        }
    }

    private ResoniteSceneRunState GetMutableRunState()
    {
        return activeRunState
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live scene run state is not initialized.");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateRunRoot(string datasetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        return Path.Combine(Path.GetFullPath(datasetRoot), "run", Guid.NewGuid().ToString("N"));
    }

    private static Dictionary<string, string> CreateCityGmlSlotNamesByRelativePath(
        IReadOnlyList<string> relativeSourceFiles)
    {
        Dictionary<string, string> slotNamesByPath = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> pathsByStem = new(StringComparer.Ordinal);

        foreach (string relativeSourceFile in relativeSourceFiles)
        {
            if (string.IsNullOrWhiteSpace(relativeSourceFile))
            {
                continue;
            }

            string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
            if (!pathsByStem.TryGetValue(fileStem, out List<string>? paths))
            {
                paths = [];
                pathsByStem.Add(fileStem, paths);
            }

            paths.Add(relativeSourceFile);
        }

        foreach ((string fileStem, List<string> paths) in pathsByStem)
        {
            paths.Sort(StringComparer.Ordinal);
            if (paths.Count == 1)
            {
                slotNamesByPath[paths[0]] = fileStem;
                continue;
            }

            foreach (string path in paths)
            {
                slotNamesByPath[path] = $"{fileStem}_{ComputeShortStableHash(path)}";
            }
        }

        return slotNamesByPath;
    }

    private static string ComputeShortStableHash(string value)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(input);
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Live send should log and skip individual city object send failures while keeping the lane alive.")]
    private async Task ProcessQueuedCityObjectAsync(
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attemptedCityObjectCount);
        try
        {
            PreparedCityObject preparedCityObject = await AwaitWithSlowCityObjectWarningAsync(
                queuedCityObject.PreparationTask,
                cancellationToken);
            await BuildPreparedCityObjectAsync(queuedCityObject, preparedCityObject, cancellationToken);

            int processedCount = Interlocked.Increment(ref processedCityObjectCount);
            ReportProgress(
                $"[live] Sent city object {processedCount}: "
                + $"{preparedCityObject.CityObject.DisplayName} "
                + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})",
                PlateauLogLevel.Info);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!IsRecoverableCityObjectSendFailure(exception))
            {
                throw;
            }

            int failedCount = Interlocked.Increment(ref failedCityObjectCount);
            ReportProgress(
                PlateauLog.Warning(
                    "live",
                    $"Skipping city object after send failure {failedCount}: "
                    + $"{queuedCityObject.CityObject.DisplayName} "
                    + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
                    + $"Reason: {exception.Message}"));
        }
        finally
        {
            await queuedCityObject.MemoryLease.DisposeAsync();
        }
    }

    private static bool IsRecoverableCityObjectSendFailure(Exception exception)
    {
        return FindResoniteLinkOperationException(exception) is { OperationName: "ImportMesh" or "ImportTexture" or "GetSlot" or "GetComponent" };
    }

    private static Task<T> AwaitWithSlowCityObjectWarningAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static ResoniteLinkOperationException? FindResoniteLinkOperationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ResoniteLinkOperationException operationException)
            {
                return operationException;
            }
        }

        return null;
    }

    private async Task EnqueueCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync();

        ObjectDisposedException.ThrowIf(cityObjectMemoryGate is null, this);
        long estimatedWorksetBytes = EstimateCityObjectWorkingSetBytes(cityObject);
        AsyncWeightedGate.Lease cityObjectMemoryLease = await cityObjectMemoryGate.AcquireAsync(
            estimatedWorksetBytes,
            cancellationToken);
        Task<PreparedCityObject> preparationTask = CreatePreparationTask(cityObject, cancellationToken);
        Task<ObjectSlotHierarchy> objectHierarchyTask = CreateObjectHierarchyTask(cityObject, cancellationToken);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object queued after {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"estimated_workset_bytes={estimatedWorksetBytes}."));
        }

        using CancellationTokenSource enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            processingCancellationSource?.Token ?? CancellationToken.None);
        try
        {
            await cityObjectChannel.Writer.WriteAsync(
                new QueuedCityObject(cityObject, preparationTask, objectHierarchyTask, cityObjectMemoryLease),
                enqueueCancellation.Token);
        }
        catch (OperationCanceledException) when (processingCancellationSource?.IsCancellationRequested == true)
        {
            await cityObjectMemoryLease.DisposeAsync();
            await AwaitProcessingTasksIfCompletedAsync();
            throw;
        }
        catch
        {
            await cityObjectMemoryLease.DisposeAsync();
            _ = ObserveTaskFailureAsync(preparationTask);
            _ = ObserveTaskFailureAsync(objectHierarchyTask);
            throw;
        }

        await AwaitProcessingTasksIfCompletedAsync();
    }

    private static async Task AwaitOneTaskSlotAsync(
        List<Task> tasks,
        CancellationToken cancellationToken)
    {
        for (int index = tasks.Count - 1; index >= 0; index--)
        {
            if (!tasks[index].IsCompleted)
            {
                continue;
            }

            Task completedTask = tasks[index];
            tasks.RemoveAt(index);
            await completedTask.WaitAsync(cancellationToken);
            return;
        }

        Task finishedTask = await Task.WhenAny(tasks).WaitAsync(cancellationToken);
        tasks.Remove(finishedTask);
        await finishedTask.WaitAsync(cancellationToken);
    }

    private static long EstimateCityObjectWorkingSetBytes(ResoniteConstructionCityObject cityObject)
    {
        const long minimumWeightBytes = 256L * 1024L;
        const long textureReferenceWeightBytes = 4L * 1024L * 1024L;
        const long heightSampleWeightBytes = sizeof(double);
        const long hdrHeightTextureWeightBytes = 4L * sizeof(float);
        const long materialBindingWeightBytes = 512L;
        const long vertexWeightBytes = 64L;
        const long indexWeightBytes = sizeof(int);
        const long perSubmeshWeightBytes = 256L;

        long geometryWeightBytes = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => EstimateTriangleMeshWorkingSetBytes(triangleMesh.Mesh),
            ResoniteHeightMapGridGeometry heightMap => checked(
                (heightMap.HeightSamples.Count * heightSampleWeightBytes)
                + ((long)heightMap.Width * heightMap.Height * hdrHeightTextureWeightBytes)),
            _ => minimumWeightBytes,
        };

        int distinctTextureCount = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(static material => material.TexturePayload!.Identity)
            .Distinct()
            .Count();
        long materialWeightBytes = checked((cityObject.Materials.Count * materialBindingWeightBytes) + (distinctTextureCount * textureReferenceWeightBytes));
        return Math.Max(minimumWeightBytes, geometryWeightBytes + materialWeightBytes);

        static long EstimateTriangleMeshWorkingSetBytes(ResoniteImportedMesh mesh)
        {
            long vertexBytes = mesh.Vertices.Count * vertexWeightBytes;
            long indexBytes = mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count * indexWeightBytes);
            long submeshBytes = mesh.Submeshes.Count * perSubmeshWeightBytes;
            return checked(vertexBytes + indexBytes + submeshBytes);
        }
    }

    private void ReportProgress(string message)
    {
        ReportProgress(message, null);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return clientSession.RoutedClient
            ?? throw new ObjectDisposedException(nameof(ILiveSendClientSession), "Routed ResoniteLink client is not connected.");
    }

    private void ReportProgress(string message, PlateauLogLevel? defaultLevel)
    {
        PlateauLogLevel resolvedDefaultLevel = defaultLevel ?? PlateauLog.InferLegacyDefaultLevel(message);
        progressReporter?.Invoke(PlateauLog.NormalizeLegacyMessage(message, resolvedDefaultLevel));
    }

    private Task<PreparedCityObject> CreatePreparationTask(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        if (Interlocked.CompareExchange(ref firstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"City object preparation started after {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"mesh='{cityObject.ActualMeshCode}'."));
        }

        CancellationToken processingCancellationToken = processingCancellationSource?.Token ?? CancellationToken.None;
        return PrepareCityObjectWithLinkedCancellationAsync(
            cityObject,
            callerCancellationToken,
            processingCancellationToken);
    }

    private Task<ObjectSlotHierarchy> CreateObjectHierarchyTask(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        CancellationToken processingCancellationToken = processingCancellationSource?.Token ?? CancellationToken.None;
        return CreateObjectHierarchyWithLinkedCancellationAsync(
            GetRoutedClient(),
            cityObject,
            callerCancellationToken,
            processingCancellationToken);
    }

    private async Task<ObjectSlotHierarchy> CreateObjectHierarchyWithLinkedCancellationAsync(
        IResoniteLinkClient setupClient,
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken,
        CancellationToken processingCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            processingCancellationToken);
        return await CreateObjectSlotHierarchyAsync(setupClient, cityObject, linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectWithLinkedCancellationAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken,
        CancellationToken processingCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            processingCancellationToken);
        return await PrepareCityObjectAsync(cityObject, linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(bootstrapInfo is null, this);
        ObjectDisposedException.ThrowIf(datasetContentSource is null, this);

        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            try
            {
                ValidateTriangleMeshBindings(cityObject, triangleGeometry.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw new ResoniteMeshValidationException(
                    $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                    + $"{CreateTriangleMeshDiagnosticSummary(cityObject, triangleGeometry.Mesh)} "
                    + $"Reason: {exception.Message}",
                    exception);
            }

        }

        TerrainTextureOverlay[] distinctTerrainOverlays = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .Select(static material => material.TerrainOverlay!)
            .Distinct()
            .OrderBy(static overlay => overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .ToArray();

        Task<PreparedTextureReference>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(terrainTextureOverlay => PrepareTerrainOverlayTextureReferenceAsync(terrainTextureOverlay, cancellationToken))
            .ToArray();
        Task<PreparedTextureReference>[] texturePreparationTasks = [];

        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteHeightMapGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedHeightMapGridGeometry(heightMap, PrepareHeightMapTexture(heightMap)),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await Task.WhenAll(
            texturePreparationTasks
                .Concat(terrainOverlayTexturePreparationTasks)
                .Concat(cityObject.Materials
                    .Where(static material => material.TexturePayload is not null)
                    .Select(PrepareDirectMaterialTextureReferenceAsync)
                    .ToArray()));
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref firstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={DescribePreparedGeometry(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private async Task<PreparedTextureReference> PrepareTerrainOverlayTextureReferenceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ResoniteTextureImport terrainTextureImport = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);

        return new PreparedTextureReference(
            TextureIdentity: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            TextureImport: terrainTextureImport,
            TerrainOverlay: terrainTextureOverlay);
    }

    private Task<PreparedTextureReference> PrepareDirectMaterialTextureReferenceAsync(
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(material.TexturePayload);

        return Task.FromResult(
            new PreparedTextureReference(
                TextureIdentity: material.TexturePayload.Identity,
                TextureSourceKind: material.TextureSourceKind,
                TextureImport: ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
                TerrainOverlay: null));
    }

    private async Task BuildPreparedCityObjectAsync(
        QueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(bootstrapInfo is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);

        IResoniteLinkClient routedClient = GetRoutedClient();
        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        using CancellationTokenSource buildStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity = CreatePreparedTextureDataByIdentity(preparedCityObject);
        Dictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureDataByOverlay = CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Stopwatch materialStopwatch = Stopwatch.StartNew();
        Stopwatch geometryStopwatch = new();
        Task<IReadOnlyList<PlannedMaterialAsset>> materialPlanningTask = PlanMaterialAssetsAsync(
            routedClient,
            cityObject,
            preparedTextureDataByIdentity,
            preparedTerrainTextureDataByOverlay,
            buildStepCancellation.Token);
        Task<PlannedGeometryAsset> geometryPlanningTask = PlanGeometryAssetAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            buildStepCancellation.Token);
        IReadOnlyList<PlannedMaterialAsset> plannedMaterialAssets;
        PlannedGeometryAsset plannedGeometryAsset;
        try
        {
            plannedMaterialAssets = await materialPlanningTask;
            materialStopwatch.Stop();

            ReportBuildStep(cityObject, $"Preparing geometry assets ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
            geometryStopwatch.Start();
            plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
        }
        catch
        {
            await buildStepCancellation.CancelAsync();
            await ObserveTaskFailuresAsync([materialPlanningTask, geometryPlanningTask]);
            throw;
        }

        PlannedSceneObjectEmission emissionPlan = new(
            plannedGeometryAsset,
            plannedMaterialAssets,
            new PlannedRenderer(
                plannedGeometryAsset.Identity,
                plannedMaterialAssets.Select(static asset => asset.Identity).ToArray()),
            new PlannedCollider(
                plannedGeometryAsset.Identity,
                cityObject.CollisionEnabled));

        ReportBuildStep(cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await CreateCityObjectBatchAsync(
            routedClient,
            objectSlots,
            cityObject,
            emissionPlan,
            cancellationToken);
        batchStopwatch.Stop();

        ReportBuildStep(cityObject, "Live build completed.");
        cityObjectStopwatch.Stop();
        ReportProgress(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' phase timings: "
                + $"slot_hierarchy_s={slotHierarchyStopwatch.Elapsed.TotalSeconds:F3} "
                + $"geometry_assets_s={geometryStopwatch.Elapsed.TotalSeconds:F3} "
                + $"materials_s={materialStopwatch.Elapsed.TotalSeconds:F3} "
                + $"batch_s={batchStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_send_s={cityObjectStopwatch.Elapsed.TotalSeconds:F3}."));
        sendScope.MarkSent();
        if (Interlocked.CompareExchange(ref firstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }
    }

    private static Dictionary<string, ResoniteTextureImport> CreatePreparedTextureDataByIdentity(
        PreparedCityObject preparedCityObject)
    {
        return preparedCityObject.Textures
            .Where(static texture => !string.IsNullOrWhiteSpace(texture.TextureIdentity))
            .ToDictionary(
                static texture => texture.TextureIdentity!,
                static texture => texture.TextureImport,
                StringComparer.Ordinal);
    }

    private static Dictionary<TerrainTextureOverlay, ResoniteTextureImport> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        return preparedCityObject.Textures
            .Where(static texture => texture.TerrainOverlay is not null)
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.TextureImport);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned import task failures after the primary send failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Task ObserveTaskFailuresAsync(IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks.Select(ObserveTaskFailureAsync));
    }

    private async Task<ObjectSlotHierarchy> CreateObjectSlotHierarchyAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(datasetRootSlot is null, this);
        ObjectDisposedException.ThrowIf(datasetAssetsRootSlot is null, this);

        string cityGmlScopeKey = ResolveCityGmlScopeKey(cityObject);
        string cityGmlSlotName = ResolveCityGmlSlotName(cityObject, cityGmlScopeKey);
        string rootMeshCode = ResolveRequiredSourceFileRootMeshCode(cityGmlSlotName, cityObject.ActualMeshCode);
        CanonicalParentScope parentScope = await canonicalParentScopeCache.GetOrCreateAsync(
            new CanonicalParentScopeKey(cityGmlScopeKey, rootMeshCode, cityObject.LodLevel),
            ct => CreateCanonicalParentScopeAsync(
                client,
                datasetRootSlot.Value,
                datasetAssetsRootSlot.Value,
                cityGmlSlotName,
                rootMeshCode,
                cityObject.LodLevel,
                ct),
            cancellationToken);

        return new ObjectSlotHierarchy(
            parentScope.AssetLodSlot,
            parentScope.LodSlot,
            CityObjectSlotName: cityObject.DisplayName,
            CityObjectIdentityTag: CreateCityObjectIdentityTag(cityObject),
            CityObjectLocalPosition: ResolveCityObjectLocalPosition(
                requestLocalOrigin ?? throw new ObjectDisposedException(nameof(ResoniteLocalOrigin), "Request local origin is not initialized."),
                rootMeshCode,
                cityObject.Transform.Position),
            CityObjectRotation: cityObject.Transform.Rotation);
    }

    private async Task<CanonicalParentScope> CreateCanonicalParentScopeAsync(
        IResoniteLinkClient client,
        CreatedSlot datasetRoot,
        CreatedSlot datasetAssetsRoot,
        string cityGmlSlotName,
        string rootMeshCode,
        int? lodLevel,
        CancellationToken cancellationToken)
    {
        string lodSlotName = FormatLodSlotName(lodLevel);
        ResoniteFloat3 rootPosition = ResolveMeshCodeRootPosition(rootMeshCode);
        CreatedSlot? cityGmlSlot = TryGetIndexedSharedChildSlot(datasetRoot.SlotId, cityGmlSlotName);
        CreatedSlot? assetCityGmlSlot = TryGetIndexedSharedChildSlot(datasetAssetsRoot.SlotId, cityGmlSlotName);
        CreatedSlot? lodSlot = cityGmlSlot is null ? null : TryGetIndexedSharedChildSlot(cityGmlSlot.Value.SlotId, lodSlotName);
        CreatedSlot? assetLodSlot = assetCityGmlSlot is null ? null : TryGetIndexedSharedChildSlot(assetCityGmlSlot.Value.SlotId, lodSlotName);

        if (cityGmlSlot is not null
            && assetCityGmlSlot is not null
            && lodSlot is not null
            && assetLodSlot is not null)
        {
            return new CanonicalParentScope(
                cityGmlSlot.Value,
                assetCityGmlSlot.Value,
                lodSlot.Value,
                assetLodSlot.Value);
        }

        cityGmlSlot ??= await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            cityGmlSlotName,
            rootPosition,
            null,
            cancellationToken);
        assetCityGmlSlot ??= await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            cityGmlSlotName,
            null,
            null,
            cancellationToken);
        lodSlot ??= await GetOrCreateSharedChildSlotAsync(
            client,
            cityGmlSlot.Value,
            lodSlotName,
            null,
            null,
            cancellationToken);
        assetLodSlot ??= await GetOrCreateSharedChildSlotAsync(
            client,
            assetCityGmlSlot.Value,
            lodSlotName,
            null,
            null,
            cancellationToken);

        if (sceneAnchor is { } anchor
            && (string.Equals(anchor.MeshCode, rootMeshCode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(anchor.ReferenceSourceFileRootId)))
        {
            sceneAnchor = anchor with
            {
                LocationSlotId = cityGmlSlot.Value.SlotId,
                MeshCode = rootMeshCode,
                Position = rootPosition,
                ReferenceSourceFileRootId = cityGmlSlot.Value.SlotId,
            };
        }

        return new CanonicalParentScope(
            cityGmlSlot ?? throw new InvalidOperationException($"CityGML slot '{cityGmlSlotName}' was not resolved."),
            assetCityGmlSlot ?? throw new InvalidOperationException($"Asset CityGML slot '{cityGmlSlotName}' was not resolved."),
            lodSlot ?? throw new InvalidOperationException($"LOD slot '{lodSlotName}' was not resolved."),
            assetLodSlot ?? throw new InvalidOperationException($"Asset LOD slot '{lodSlotName}' was not resolved."));
    }

    private static string ResolveCityGmlScopeKey(ResoniteConstructionCityObject cityObject)
    {
        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            return cityObject.SourceFileRelativePath!;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            return cityObject.SourceUnitKey!;
        }

        return cityObject.SlotKey;
    }

    private static string ResolveRequiredSourceFileRootMeshCode(string cityGmlSlotName, string actualMeshCode)
    {
        if (ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(cityGmlSlotName, out string meshCode))
        {
            return meshCode;
        }

        if (PlateauMeshCode.TryGetCenter(actualMeshCode, out _))
        {
            return actualMeshCode;
        }

        throw new InvalidOperationException(
            $"Source-file root '{cityGmlSlotName}' did not contain a concrete meshcode and actual mesh '{actualMeshCode}' was not concrete.");
    }

    private string ResolveCityGmlSlotName(
        ResoniteConstructionCityObject cityObject,
        string cityGmlScopeKey)
    {
        if (cityGmlSlotNamesByRelativePath is not null
            && cityGmlSlotNamesByRelativePath.TryGetValue(cityGmlScopeKey, out string? slotName)
            && !string.IsNullOrWhiteSpace(slotName))
        {
            return slotName;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            string fileStem = Path.GetFileNameWithoutExtension(cityObject.SourceFileRelativePath);
            if (!string.IsNullOrWhiteSpace(fileStem))
            {
                return fileStem;
            }
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            return cityObject.SourceUnitKey!;
        }

        return cityObject.SlotKey;
    }

    private double GetSceneElapsedSeconds()
    {
        return sceneBuildStopwatch?.Elapsed.TotalSeconds ?? 0.0;
    }

    private static string CreateDispatchDependencyKey(ResoniteConstructionCityObject cityObject)
    {
        string objectIdentity = cityObject.SourceObjectKey ?? cityObject.SlotKey;
        string lodKey = cityObject.LodLevel.HasValue
            ? cityObject.LodLevel.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{cityObject.ActualMeshCode}|{cityObject.PackageName}|{lodKey}|{objectIdentity}");
    }

    private static void ValidateTriangleMeshBindings(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        if (mesh.Submeshes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any submesh.");
        }

        if (cityObject.Materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any material.");
        }

        Dictionary<int, ResoniteMeshSubmesh> submeshByIndex = mesh.Submeshes.ToDictionary(
            static submesh => submesh.Index,
            static submesh => submesh);
        if (submeshByIndex.Count != mesh.Submeshes.Count)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' contained duplicate submesh indices.");
        }

        Dictionary<int, string> materialKeyBySubmeshIndex = new();
        foreach (ResoniteMaterialBinding material in cityObject.Materials)
        {
            if (material.SubmeshIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' material '{material.MaterialKey}' did not target any submesh.");
            }

            foreach (int submeshIndex in material.SubmeshIndices)
            {
                if (!submeshByIndex.ContainsKey(submeshIndex))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' material '{material.MaterialKey}' targeted missing submesh index {submeshIndex}.");
                }

                if (materialKeyBySubmeshIndex.TryGetValue(submeshIndex, out string? existingMaterialKey))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' assigned submesh index {submeshIndex} to both '{existingMaterialKey}' and '{material.MaterialKey}'.");
                }

                materialKeyBySubmeshIndex[submeshIndex] = material.MaterialKey;
            }
        }

        foreach (int submeshIndex in submeshByIndex.Keys.OrderBy(static index => index))
        {
            if (!materialKeyBySubmeshIndex.ContainsKey(submeshIndex))
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' left submesh index {submeshIndex} without a material assignment.");
            }
        }
    }

    private static PreparedTriangleMeshGeometry PrepareTriangleMeshGeometry(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        try
        {
            return new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(mesh));
        }
        catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
        {
            throw new ResoniteMeshValidationException(
                $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                + $"{CreateTriangleMeshDiagnosticSummary(cityObject, mesh)} "
                + $"Reason: {exception.Message}",
                exception);
        }
    }

    private static string CreateTriangleMeshDiagnosticSummary(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        int[] submeshIndices = mesh.Submeshes
            .Select(static submesh => submesh.Index)
            .OrderBy(static index => index)
            .ToArray();
        string materialSummary = string.Join(
            ", ",
            cityObject.Materials.Select(static material =>
                $"{material.MaterialKey}[{string.Join("/", material.SubmeshIndices.OrderBy(static index => index))}]"));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"mesh_code={cityObject.ActualMeshCode}, vertices={mesh.Vertices.Count}, submeshes={mesh.Submeshes.Count}, "
            + $"submesh_indices=[{string.Join(", ", submeshIndices)}], materials={cityObject.Materials.Count}, "
            + $"material_bindings=[{materialSummary}]");
    }

    private static string CreateCityObjectIdentityTag(ResoniteConstructionCityObject cityObject)
    {
        return CreateDispatchDependencyKey(cityObject);
    }

    private async Task<IReadOnlyList<PlannedMaterialAsset>> PlanMaterialAssetsAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        Dictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        Task<PlannedMaterialAsset>[] plannedMaterialTasks = new Task<PlannedMaterialAsset>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            if (material.AssetScope == ResoniteMaterialAssetScope.Common)
            {
                material = NormalizeCommonMaterialBinding(material);
                if (material.AssetScope != ResoniteMaterialAssetScope.Common)
                {
                    plannedMaterialTasks[materialIndex] = WrapDedicatedMaterialPlanningTask(
                        ResoniteMaterialPlanning.PlanDedicatedMaterialAssetAsync(
                            importClient,
                            material,
                            materialIndex,
                            cityObject.PackageName,
                            preparedTextureDataByIdentity,
                            preparedTerrainTextureDataByOverlay,
                            preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                            cancellationToken));
                    continue;
                }

                string family = material.Family ?? BundledDefaultMaterialFamilies.Other;
                if (commonMaterialFamilyWarmupTasks.TryGetValue(family, out Task? familyWarmupTask))
                {
                    await familyWarmupTask.WaitAsync(cancellationToken);
                }

                string materialKey = CreateCanonicalCommonMaterialKey(
                    family,
                    material.BundledVariantIndex ?? 0,
                    material.Projection,
                    material.TextureScale);
                if (!commonMaterialCreationTasks.TryGetValue(materialKey, out Task<CreatedMaterialAsset>? materialCreationTask))
                {
                    throw new InvalidOperationException(
                        $"Bootstrap did not produce common material '{materialKey}'.");
                }

                CreatedMaterialAsset existingMaterialAsset = await materialCreationTask.WaitAsync(cancellationToken);
                plannedMaterialTasks[materialIndex] = Task.FromResult<PlannedMaterialAsset>(
                    new PlannedReusableMaterialAsset(
                        new MaterialIdentity(materialKey),
                        existingMaterialAsset.MaterialComponentId));
            }
            else
            {
                plannedMaterialTasks[materialIndex] = WrapDedicatedMaterialPlanningTask(
                    ResoniteMaterialPlanning.PlanDedicatedMaterialAssetAsync(
                        importClient,
                        material,
                        materialIndex,
                        cityObject.PackageName,
                        preparedTextureDataByIdentity,
                        preparedTerrainTextureDataByOverlay,
                        preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                        cancellationToken));
            }
        }

        return await Task.WhenAll(plannedMaterialTasks);

        static async Task<PlannedMaterialAsset> WrapDedicatedMaterialPlanningTask(Task<PlannedDedicatedMaterialAsset> task)
        {
            return await task;
        }
    }

    internal static string CreateMaterialSlotName(ResoniteMaterialBinding material, bool useCommonMaterialAssets)
    {
        ArgumentNullException.ThrowIfNull(material);

        string componentKind = material.MaterialType switch
        {
            ResoniteMaterialType.Standard => material.Projection switch
            {
                ResoniteMaterialProjection.Uv => "pbs-uv",
                ResoniteMaterialProjection.Triplanar => "pbs-triplanar",
                _ => "material",
            },
            ResoniteMaterialType.VertexColor => "vertex-color",
            ResoniteMaterialType.Wireframe => "wireframe",
            _ => "material",
        };

        string projectionName = material.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => material.Projection.ToString().ToLowerInvariant(),
        };

        string sourceName = material.TerrainOverlay is not null
            ? CreateTerrainOverlayToken(material.TerrainOverlay)
            : material.TexturePayload is not null
                ? $"payload-{ComputeShortStableHash(material.MaterialKey)}"
            : material.AssetScope == ResoniteMaterialAssetScope.Common
                ? $"bundled-v{material.BundledVariantIndex ?? 0}"
            : material.MaterialType.ToString();

        string familyName = string.IsNullOrWhiteSpace(material.Family)
            ? "none"
            : material.Family!;
        string colorName = CreateCompactColorSuffix(material.BaseColor);
        string scaleName = material.TextureScale is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureScale.X:0.######}x{material.TextureScale.Y:0.######}")
            : "none";
        string offsetName = material.TextureOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureOffset.X:0.######}x{material.TextureOffset.Y:0.######}")
            : "none";
        string depthName = material.DepthOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.DepthOffset.Factor:0.######}x{material.DepthOffset.Units:0.######}")
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{componentKind}_{projectionName}_{sourceName}_{familyName}_{scaleName}_{offsetName}_{depthName}_{colorName}");
    }

    internal static ResoniteMaterialBinding NormalizeCommonMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return material;
        }

        if (!IsBundledCommonMaterialCandidate(material))
        {
            return material with { AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped };
        }

        string canonicalFamily = string.IsNullOrWhiteSpace(material.Family)
            ? BundledDefaultMaterialFamilies.Other
            : material.Family!;
        int canonicalVariantIndex = material.BundledVariantIndex ?? 0;
        ResoniteFloat2 defaultTextureScale = BundledDefaultMaterialProfiles.GetTilesPerMeter(
            BundledDefaultMaterialFamilies.GetVariant(canonicalFamily, canonicalVariantIndex));
        if (material.TextureScale is not null
            && (Math.Abs(material.TextureScale.X - defaultTextureScale.X) > 1e-9
                || Math.Abs(material.TextureScale.Y - defaultTextureScale.Y) > 1e-9))
        {
            return material with { AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped };
        }

        ResoniteFloat2 canonicalTextureScale = material.TextureScale
            ?? defaultTextureScale;

        return material with
        {
            MaterialKey = CreateCanonicalCommonMaterialKey(
                canonicalFamily,
                canonicalVariantIndex,
                material.Projection,
                canonicalTextureScale),
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType = ResoniteMaterialType.Standard,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = canonicalTextureScale,
            Family = canonicalFamily,
            TextureOffset = null,
            DepthOffset = null,
            BundledVariantIndex = canonicalVariantIndex,
        };
    }

    private static bool IsBundledCommonMaterialCandidate(ResoniteMaterialBinding material)
    {
        return material.TerrainOverlay is null
            && material.TexturePayload is null
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family);
    }

    internal static string CreateCanonicalCommonMaterialKey(
        string family,
        int bundledVariantIndex,
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale)
    {
        string scaleToken = textureScale is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{textureScale.X:0.######}x{textureScale.Y:0.######}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"common|{family}|variant:{bundledVariantIndex}|{projection}|scale:{scaleToken}");
    }

    private static string CreateCompactColorSuffix(ResoniteColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.###}-{color.G:0.###}-{color.B:0.###}-{color.A:0.###}");
    }

    private static string CreateTextureSourceToken(string texturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);

        string normalizedPath = texturePath.Replace('\\', '/').Trim('/');
        string? directoryName = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/').Trim('/');
        string fileStem = Path.GetFileNameWithoutExtension(normalizedPath);

        string normalizedStemPath = string.IsNullOrWhiteSpace(directoryName)
            ? fileStem
            : $"{directoryName}/{fileStem}";

        return normalizedStemPath.Replace('/', '_');
    }

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainTextureOverlay)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        string source = string.Create(
            CultureInfo.InvariantCulture,
            $"{terrainTextureOverlay.PackageName}|{terrainTextureOverlay.GeographicBounds.MinLatitude:0.######}|{terrainTextureOverlay.GeographicBounds.MinLongitude:0.######}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant()}");
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PlannedGeometryAsset> PlanGeometryAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(bootstrapInfo is null, this);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    triangleMesh.MeshImport,
                    cancellationToken)),
            PreparedHeightMapGridGeometry heightMap => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareHeightMapGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateHeightMapAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    heightMap.Geometry,
                    heightMap.HeightTextureImport,
                    cancellationToken)),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
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

    private static PlannedGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        PreparedGeometryAssetBatch preparedGeometryBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{cityObject.PackageName}|{cityObject.SlotKey}|{preparedGeometryBatch.MeshAssetSlotName}"));

        return preparedGeometryBatch switch
        {
            PreparedTriangleMeshAssetBatch triangleMesh => new PlannedTriangleMeshGeometryAsset(
                identity,
                triangleMesh.MeshAssetSlotName,
                triangleMesh.MeshUri),
            PreparedHeightMapGridAssetBatch heightMap => new PlannedHeightMapGridGeometryAsset(
                identity,
                heightMap.MeshAssetSlotName,
                heightMap.HeightMapAssetSlotName,
                heightMap.Geometry,
                heightMap.HeightTextureUri),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry asset batch type '{preparedGeometryBatch.GetType().Name}'."),
        };
    }

    private async Task CreateCityObjectBatchAsync(
        IResoniteLinkClient client,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        PlannedSceneObjectEmission emissionPlan,
        CancellationToken cancellationToken)
    {
        CityObjectBatchBuilder batchBuilder = new();
        PendingBatchSlot meshAssetSlot = batchBuilder.AddSlot(
            objectSlots.AssetLodSlot.SlotId,
            emissionPlan.GeometryAsset.MeshAssetSlotName,
            null,
            null);
        PendingBatchSlot? heightMapAssetSlot = null;
        PendingBatchComponent geometryComponent;

        switch (emissionPlan.GeometryAsset)
        {
            case PlannedTriangleMeshGeometryAsset triangleMesh:
                geometryComponent = batchBuilder.AddComponent(
                    meshAssetSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["URL"] = new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        },
                    });
                break;
            case PlannedHeightMapGridGeometryAsset heightMap:
                heightMapAssetSlot = batchBuilder.AddSlot(
                    objectSlots.AssetLodSlot.SlotId,
                    heightMap.HeightMapAssetSlotName,
                    null,
                    null);
                PendingBatchComponent heightTexture = batchBuilder.AddComponent(
                    heightMapAssetSlot.Value.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ResoniteGeometryAssetAssembler.CreateHeightMapTextureMembers(heightMap.HeightTextureUri));
                double displacementMagnitude = Math.Max(heightMap.Geometry.MaxHeight - heightMap.Geometry.MinHeight, 0.0);
                ReportProgress(
                    $"[live] HeightMap texture ready. Creating GridMesh "
                    + $"({heightMap.Geometry.Width}x{heightMap.Geometry.Height}, displacement={displacementMagnitude:F3}).");
                geometryComponent = batchBuilder.AddComponent(
                    meshAssetSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.GridMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["Points"] = new Field_int2
                        {
                            Value = new int2
                            {
                                x = heightMap.Geometry.Width,
                                y = heightMap.Geometry.Height,
                            },
                        },
                        ["Size"] = new Field_float2
                        {
                            Value = new float2
                            {
                                x = (float)heightMap.Geometry.Size.X,
                                y = (float)heightMap.Geometry.Size.Y,
                            },
                        },
                        ["DisplacementMagnitude"] = new Field_float
                        {
                            Value = (float)displacementMagnitude,
                        },
                        ["DisplacementTexture"] = new Reference
                        {
                            TargetID = heightTexture.LocalId,
                        },
                    });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{emissionPlan.GeometryAsset.GetType().Name}'.");
        }

        Dictionary<MaterialIdentity, string> emittedMaterialTargets = new();
        foreach (PlannedMaterialAsset materialAsset in emissionPlan.MaterialAssets)
        {
            switch (materialAsset)
            {
                case PlannedReusableMaterialAsset reusableMaterial:
                    emittedMaterialTargets[reusableMaterial.Identity] = reusableMaterial.TargetId;
                    break;
                case PlannedDedicatedMaterialAsset dedicatedMaterial:
                    PendingBatchComponent emittedMaterial = AddDedicatedMaterialOperations(
                        batchBuilder,
                        meshAssetSlot.LocalId,
                        dedicatedMaterial,
                        cancellationToken);
                    emittedMaterialTargets[dedicatedMaterial.Identity] = emittedMaterial.LocalId;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported planned material asset type '{materialAsset.GetType().Name}'.");
            }
        }

        Dictionary<string, Member> meshRendererMembers = new(StringComparer.Ordinal)
        {
            ["Mesh"] = new Reference
            {
                TargetID = geometryComponent.LocalId,
            },
            ["Materials"] = new SyncList
            {
                Elements = emissionPlan.Renderer.MaterialIdentities
                    .Select(materialIdentity => (Member)new Reference
                    {
                        TargetID = emittedMaterialTargets[materialIdentity],
                    })
                    .ToList(),
            },
        };

        PendingBatchSlot presentationSlot = batchBuilder.AddSlot(
            objectSlots.LodSlot.SlotId,
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation,
            objectSlots.CityObjectIdentityTag);
        batchBuilder.AddComponent(
            presentationSlot.LocalId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            meshRendererMembers);
        batchBuilder.AddComponent(
            presentationSlot.LocalId,
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Type"] = new Field_Enum
                {
                    Value = emissionPlan.Collider.CollisionEnabled ? "Static" : "NoCollision",
                },
                ["CharacterCollider"] = new Field_bool
                {
                    Value = emissionPlan.Collider.CollisionEnabled,
                },
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponent.LocalId,
                },
            });

        int operationCount = batchBuilder.Operations.Count;
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(batchBuilder.Operations, cancellationToken);
        batchStopwatch.Stop();
        ReportProgress(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' batch completed in {batchStopwatch.Elapsed.TotalSeconds:F3}s "
                + $"(operations={operationCount}, est_payload_bytes={EstimateBatchPayloadBytes(operationCount)})."));

        CanonicalBatchEntityMap canonicalBatchEntityMap = CanonicalBatchEntityMap.Create(batchResponse);
        canonicalBatchEntityMap.ValidateAll(batchBuilder.PendingOperations);
        _ = canonicalBatchEntityMap.ResolveSlot(meshAssetSlot);
        _ = canonicalBatchEntityMap.ResolveComponent(geometryComponent);
        _ = canonicalBatchEntityMap.ResolveSlot(presentationSlot);
        if (heightMapAssetSlot is not null)
        {
            _ = canonicalBatchEntityMap.ResolveSlot(heightMapAssetSlot.Value);
        }
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private static PendingBatchComponent AddDedicatedMaterialOperations(
        CityObjectBatchBuilder batchBuilder,
        string meshAssetSlotLocalId,
        PlannedDedicatedMaterialAsset plannedMaterial,
        CancellationToken cancellationToken)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        string materialContainerLocalId = meshAssetSlotLocalId;
        if (plannedMaterial.PreserveDedicatedMaterialSlot)
        {
            string materialSlotName = CreateMaterialSlotName(material, useCommonMaterialAssets: false);
            PendingBatchSlot materialSlot = batchBuilder.AddSlot(
                meshAssetSlotLocalId,
                materialSlotName,
                null,
                null);
            materialContainerLocalId = materialSlot.LocalId;
        }

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
        if (albedoTextureUri is not null)
        {
            PendingBatchComponent albedoTexture = batchBuilder.AddComponent(
                materialContainerLocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(albedoTextureUri));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.LocalId,
            };
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
        if (normalTextureUri is not null)
        {
            PendingBatchComponent normalTexture = batchBuilder.AddComponent(
                materialContainerLocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(normalTextureUri));
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.LocalId,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
        if (heightTextureUri is not null)
        {
            PendingBatchComponent heightTexture = batchBuilder.AddComponent(
                materialContainerLocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(heightTextureUri));
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.LocalId,
            };
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
        if (metallicTextureUri is not null)
        {
            PendingBatchComponent metallicTexture = batchBuilder.AddComponent(
                materialContainerLocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(metallicTextureUri));
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId,
            };
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
        if (emissionTextureUri is not null)
        {
            PendingBatchComponent emissionTexture = batchBuilder.AddComponent(
                materialContainerLocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(emissionTextureUri));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.LocalId,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return batchBuilder.AddComponent(
            materialContainerLocalId,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            materialMembers);
    }

    private Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return ImportOptionalTextureCoreAsync(importClient, textureImport, cancellationToken);
    }

    private async Task<Uri?> ImportOptionalTextureCoreAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await ImportTextureAsync(importClient, textureImport, cancellationToken);
    }

    internal static Dictionary<string, Member> CreateTextureMembers(Uri assetUri)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };
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

        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(requestOrigin, rootMeshCenter);
        return Subtract(cityObjectPosition, rootOffsetFromRequest);
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
        if (firstProcessingFailureSource?.Task.IsCompletedSuccessfully == true)
        {
            Exception failure = await firstProcessingFailureSource.Task;
            throw failure;
        }

        if (processingTasks is not null && Array.Exists(processingTasks, static task => task.IsCompleted))
        {
            await Task.WhenAll(processingTasks);
        }
    }

    private void TryMarkProcessingFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        firstProcessingFailureSource?.TrySetResult(exception);
    }

    private void CancelProcessing()
    {
        try
        {
            _ = processingCancellationSource?.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private Task<CreatedSlot> CreateSlotAsync(
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
        CreatedSlot createdSlot = await sharedSlotCache.GetOrCreateAsync(
            (parentId, slotName),
            ct => GetOrCreateSharedChildSlotCoreAsync(
                client,
                parentId,
                slotName,
                position,
                rotation,
                ct),
            cancellationToken);
        return createdSlot;
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        CreatedSlot? indexedSlot = TryGetIndexedSharedChildSlot(parentId, slotName);
        if (indexedSlot is not null)
        {
            createdSlotIds[indexedSlot.Value.SlotId] = 0;
            return indexedSlot.Value;
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
        PendingBatchComponent pendingComponent = new(
            LocalId: $"single_component_{Guid.NewGuid():N}",
            MessageId: $"single_component_message_{Guid.NewGuid():N}",
            ComponentType: componentType);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [CreateAddComponentOperation(containerSlotId, componentType, members, pendingComponent.LocalId, pendingComponent.MessageId)],
            cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveComponent(pendingComponent);
    }

    private static Task UpdateComponentAsync(
        IResoniteLinkClient client,
        string componentId,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        return client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = componentId,
                    Members = members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            },
            cancellationToken);
    }

    private async Task<CreatedSlot> CreateSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        PendingBatchSlot pendingSlot = new(
            LocalId: $"single_slot_{Guid.NewGuid():N}",
            MessageId: $"single_slot_message_{Guid.NewGuid():N}",
            SlotName: slotName);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [CreateAddSlotOperation(parentId, slotName, position, rotation, requestedSlotId: pendingSlot.LocalId, messageId: pendingSlot.MessageId)],
            cancellationToken);
        CreatedSlot createdSlot = CanonicalBatchEntityMap.Create(response).ResolveSlot(pendingSlot);
        createdSlotIds[createdSlot.SlotId] = 0;
        return IndexCreatedSharedSlot(parentId, createdSlot, position);
    }

    private void IndexBootstrapHierarchy(ResoniteSceneBootstrapState bootstrapState)
    {
        if (bootstrapState.DatasetRootSnapshot is not null)
        {
            observedSlotSnapshotsById.Clear();
            IndexObservedSlotSnapshot(bootstrapState.DatasetRootSnapshot);
        }
        else
        {
            IndexCreatedSharedSlot(RootSlotId, bootstrapState.DatasetRootSlot);
        }

        IndexCreatedSharedSlot(bootstrapState.DatasetRootSlot.SlotId, bootstrapState.DatasetAssetsRootSlot);
        IndexCreatedSharedSlot(bootstrapState.DatasetAssetsRootSlot.SlotId, bootstrapState.CommonAssetsRootSlot);
    }

    private void IndexObservedSlotSnapshot(Slot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.ID))
        {
            return;
        }

        observedSlotSnapshotsById[slot.ID] = slot;
        if (slot.Children is null || slot.Children.Count == 0)
        {
            return;
        }

        foreach (Slot child in slot.Children)
        {
            if (!string.IsNullOrWhiteSpace(child.ID) && !string.IsNullOrWhiteSpace(child.Name?.Value))
            {
                sharedSlotIndex[CreateSharedSlotIndexKey(slot.ID!, child.Name!.Value)] = new CreatedSlot(child.ID!, child.Name.Value);
            }

            IndexObservedSlotSnapshot(child);
        }
    }

    private CreatedSlot? TryGetIndexedSharedChildSlot(string parentId, string slotName)
    {
        return sharedSlotIndex.TryGetValue(CreateSharedSlotIndexKey(parentId, slotName), out CreatedSlot createdSlot)
            ? createdSlot
            : null;
    }

    private CreatedSlot IndexCreatedSharedSlot(string parentId, CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        sharedSlotIndex[CreateSharedSlotIndexKey(parentId, createdSlot.SlotName)] = createdSlot;
        observedSlotSnapshotsById[createdSlot.SlotId] = new Slot
        {
            ID = createdSlot.SlotId,
            Name = new Field_string
            {
                Value = createdSlot.SlotName,
            },
            Parent = new Reference
            {
                TargetID = parentId,
            },
            Position = position is null ? null : CreateFloat3(position),
        };
        return createdSlot;
    }

    private static string CreateSharedSlotIndexKey(string parentId, string slotName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{parentId}\n{slotName}");
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        ResoniteSceneSlotSnapshot snapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            parentId,
            1,
            cancellationToken);
        return TryFindUniqueChildSlotByName(snapshot.Root, slotName, parentId);
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameWithRetryAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        int attemptLimit,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            CreatedSlot? existingSlot = await TryGetUniqueChildSlotByNameAsync(
                client,
                parentId,
                slotName,
                cancellationToken);
            if (existingSlot is not null)
            {
                return existingSlot;
            }

            if (attempt < attemptLimit && retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        return null;
    }

    private static CreatedSlot? TryFindUniqueChildSlotByName(
        Slot? parentSlot,
        string slotName,
        string? parentId = null)
    {
        return TryFindUniqueMatchingChildSlot(
            parentSlot,
            slotName,
            static _ => true,
            parentId);
    }

    private static Slot SelectPreferredExistingSlot(IReadOnlyList<Slot> matches)
    {
        return matches
            .OrderByDescending(static slot => slot.Components?.Count ?? 0)
            .ThenBy(static slot => slot.ID, StringComparer.Ordinal)
            .First();
    }

    private static CreatedSlot? TryFindUniqueMatchingChildSlot(
        Slot? parentSlot,
        string slotName,
        Func<Slot, bool> predicate,
        string? parentId = null)
    {
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matches = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .Where(predicate)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        Slot preferredMatch = SelectPreferredExistingSlot(matches);
        string existingSlotId = preferredMatch.ID
            ?? throw new InvalidOperationException(
                $"Child slot '{slotName}' under parent '{parentId ?? parentSlot.ID ?? "<unknown>"}' did not surface an ID.");
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

        ObjectDisposedException.ThrowIf(importedTextureUriCache is null, this);
        return await importedTextureUriCache.GetOrCreateAsync(
            cacheKey.Value,
            ct => client.ImportTextureAsync(textureImport, ct),
            cancellationToken);
    }

    private static TextureImportCacheKey? TryCreateTextureImportCacheKey(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
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
        ResoniteFloatQ? rotation,
        string? slotTag = null,
        string? requestedSlotId = null,
        string? messageId = null)
    {
        return new AddSlot
        {
            MessageID = messageId,
            Data = new Slot
            {
                ID = requestedSlotId,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Tag = string.IsNullOrWhiteSpace(slotTag)
                    ? null
                    : new Field_string
                    {
                        Value = slotTag,
                    },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        string? requestedComponentId = null,
        string? messageId = null)
    {
        return new AddComponent
        {
            MessageID = messageId,
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = requestedComponentId,
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

    private static SceneBootstrapInfo CreateBootstrapInfo(SceneBuildRequest request)
    {
        ResoniteConstructionMetadata metadata = SceneImportContractMapper.ToInternal(request.Metadata);
        return SceneBootstrapInfo.CreateFromMetadata(
            metadata,
            request.DatasetContentSource.SourcePath);
    }

    private readonly record struct PendingBatchSlot(
        string LocalId,
        string MessageId,
        string SlotName);

    private readonly record struct PendingBatchComponent(
        string LocalId,
        string MessageId,
        string ComponentType);

    private readonly record struct PendingBatchOperation(
        string MessageId,
        string Description);

    private sealed class CityObjectBatchBuilder
    {
        private readonly string batchScopeToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        private int nextEntityId;
        private int nextMessageId;

        public List<DataModelOperation> Operations { get; } = [];
        public List<PendingBatchOperation> PendingOperations { get; } = [];

        public PendingBatchSlot AddSlot(
            string parentId,
            string slotName,
            ResoniteFloat3? position,
            ResoniteFloatQ? rotation,
            string? slotTag = null)
        {
            string localId = AllocateEntityId("local_slot");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddSlotOperation(parentId, slotName, position, rotation, slotTag, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"slot '{slotName}'"));
            return new PendingBatchSlot(localId, messageId, slotName);
        }

        public PendingBatchComponent AddComponent(
            string containerSlotId,
            string componentType,
            IReadOnlyDictionary<string, Member> members)
        {
            string localId = AllocateEntityId("local_component");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddComponentOperation(containerSlotId, componentType, members, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"component '{componentType}'"));
            return new PendingBatchComponent(localId, messageId, componentType);
        }

        private string AllocateEntityId(string prefix)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{batchScopeToken}_{++nextEntityId}");
        }

        private string AllocateMessageId()
        {
            return string.Create(CultureInfo.InvariantCulture, $"batch_message_{batchScopeToken}_{++nextMessageId}");
        }
    }

    private sealed class CanonicalBatchEntityMap
    {
        private readonly Dictionary<string, Response> responsesByMessageId;
        private readonly Queue<Response> responsesWithoutMessageId;

        private CanonicalBatchEntityMap(
            Dictionary<string, Response> responsesByMessageId,
            Queue<Response> responsesWithoutMessageId)
        {
            this.responsesByMessageId = responsesByMessageId;
            this.responsesWithoutMessageId = responsesWithoutMessageId;
        }

        public static CanonicalBatchEntityMap Create(BatchResponse batchResponse)
        {
            ArgumentNullException.ThrowIfNull(batchResponse);
            return new CanonicalBatchEntityMap(
                (batchResponse.Responses ?? [])
                    .Where(static response => !string.IsNullOrWhiteSpace(response.SourceMessageID))
                    .ToDictionary(response => response.SourceMessageID, StringComparer.Ordinal),
                new Queue<Response>(
                    (batchResponse.Responses ?? [])
                        .Where(static response => string.IsNullOrWhiteSpace(response.SourceMessageID))));
        }

        public CreatedSlot ResolveSlot(PendingBatchSlot pendingSlot)
        {
            Response response = ResolveResponse(pendingSlot.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for slot '{pendingSlot.SlotName}' did not include a canonical slot ID.");
            }

            return new CreatedSlot(newEntityId.EntityId, pendingSlot.SlotName);
        }

        public CreatedComponent ResolveComponent(PendingBatchComponent pendingComponent)
        {
            Response response = ResolveResponse(pendingComponent.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for component '{pendingComponent.ComponentType}' did not include a canonical component ID.");
            }

            return new CreatedComponent(newEntityId.EntityId, pendingComponent.ComponentType);
        }

        public void ValidateAll(IReadOnlyList<PendingBatchOperation> pendingOperations)
        {
            ArgumentNullException.ThrowIfNull(pendingOperations);
            foreach (PendingBatchOperation pendingOperation in pendingOperations)
            {
                _ = ResolveResponse(
                    pendingOperation.MessageId,
                    $"validate {pendingOperation.Description}");
            }
        }

        private Response ResolveResponse(string messageId)
        {
            return ResolveResponse(messageId, $"resolve batch message '{messageId}'");
        }

        private Response ResolveResponse(string messageId, string operationName)
        {
            if (!responsesByMessageId.TryGetValue(messageId, out Response? response))
            {
                if (responsesWithoutMessageId.Count == 0)
                {
                    throw new InvalidOperationException($"Batch response did not include message '{messageId}'.");
                }

                response = responsesWithoutMessageId.Dequeue();
            }

            ResoniteLinkClient.EnsureSuccess(response, operationName);
            return response;
        }
    }


    private sealed record ObjectSlotHierarchy(
        CreatedSlot AssetLodSlot,
        CreatedSlot LodSlot,
        string CityObjectSlotName,
        string CityObjectIdentityTag,
        ResoniteFloat3 CityObjectLocalPosition,
        ResoniteFloatQ? CityObjectRotation);

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask,
        Task<ObjectSlotHierarchy> ObjectHierarchyTask,
        AsyncWeightedGate.Lease MemoryLease);

    private sealed record CanonicalParentScope(
        CreatedSlot CityGmlSlot,
        CreatedSlot AssetCityGmlSlot,
        CreatedSlot LodSlot,
        CreatedSlot AssetLodSlot);

    private readonly record struct CanonicalParentScopeKey(
        string CityGmlScopeKey,
        string RootMeshCode,
        int? LodLevel);

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
        IReadOnlyList<PreparedTextureReference> Textures);

    private sealed record PreparedTextureReference(
        string? TextureIdentity,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteTextureImport TextureImport,
        TerrainTextureOverlay? TerrainOverlay = null);

    private sealed class ResoniteSceneRunState
    {
        public SceneBootstrapInfo? BootstrapInfo { get; set; }

        public CreatedSlot? DatasetRootSlot { get; set; }

        public CreatedSlot? DatasetAssetsRootSlot { get; set; }

        public CreatedSlot? CommonAssetsRootSlot { get; set; }

        public string? RunRoot { get; set; }

        public AsyncCompletedResultCache<(string ParentSlotId, string SlotName), CreatedSlot> SharedSlotCache { get; } = new();

        public AsyncCompletedResultCache<CanonicalParentScopeKey, CanonicalParentScope> CanonicalParentScopeCache { get; } = new();

        public ConcurrentDictionary<string, byte> CreatedSlotIds { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, Task> CommonMaterialFamilyWarmupTasks { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, Task<CreatedMaterialAsset>> CommonMaterialCreationTasks { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, CreatedSlot> SharedSlotIndex { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, Slot> ObservedSlotSnapshotsById { get; } = new(StringComparer.Ordinal);

        public AsyncCompletedResultCache<TextureImportCacheKey, Uri>? ImportedTextureUriCache { get; set; }

        public Channel<QueuedCityObject>? CityObjectChannel { get; set; }

        public Task[]? ProcessingTasks { get; set; }

        public CancellationTokenSource? ProcessingCancellationSource { get; set; }

        public TaskCompletionSource<Exception>? FirstProcessingFailureSource { get; set; }

        public CompositeCityObjectBaker? CityObjectBaker { get; set; }

        public AsyncWeightedGate? CityObjectMemoryGate { get; set; }

        public int AttemptedCityObjectCount;

        public int ProcessedCityObjectCount;

        public int FailedCityObjectCount;

        public Stopwatch? SceneBuildStopwatch { get; set; }

        public int FirstQueuedCityObjectLogged;

        public int FirstPreparedCityObjectLogged;

        public int FirstBuiltCityObjectLogged;

        public int FirstCityObjectPreparationStartedLogged;

        public int FirstCommonMaterialPrepLogged;

        public int FirstCityObjectStreamingStartedLogged;

        public int FirstCityObjectDequeuedLogged;

        public IPlateauDatasetContentSource? DatasetContentSource { get; set; }

        public SceneAnchor? SceneAnchor { get; set; }

        public ResoniteLocalOrigin? RequestLocalOrigin { get; set; }

        public string? DatasetLicenseComponentId { get; set; }

        public Dictionary<string, string>? CityGmlSlotNamesByRelativePath { get; set; }
    }

}
