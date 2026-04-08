using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

public sealed class ResoniteLinkSceneBuilder : IResoniteSceneBuilder
{
    private const int MaxQueuedCityObjects = 4;
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
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
    private ConcurrentDictionary<string, Task<string>>? resolvedTexturePathTasks;
    private Dictionary<string, TerrainTextureOverlay>? terrainTextureOverlaysByPath;
    private Channel<QueuedCityObject>? cityObjectChannel;
    private Task[]? processingTasks;
    private int processedCityObjectCount;
    private Stopwatch? sceneBuildStopwatch;
    private int firstQueuedCityObjectLogged;
    private int firstPreparedCityObjectLogged;
    private int firstBuiltCityObjectLogged;
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

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        this.metadata = metadata;
        generatedAssetsRoot = Path.Combine(Path.GetFullPath(workRoot), ".generated-assets");
        datasetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "dataset");
        meshCodeSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
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

        clients = Enumerable.Range(0, connectionCount)
            .Select(_ =>
            {
                IResoniteLinkClient client = clientFactory();
                return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
            })
            .ToList();
        IResoniteLinkClient setupClient = clients[0];
        licenseManager = new ResoniteLicenseManager(metadata.Attribution);
        slotEnsureTasks = new ConcurrentDictionary<string, Lazy<Task>>(StringComparer.Ordinal);
        componentEnsureTasks = new ConcurrentDictionary<string, Lazy<Task>>(StringComparer.Ordinal);
        assetComponentEnsureTasks = new ConcurrentDictionary<string, Lazy<Task<Uri>>>(StringComparer.Ordinal);
        materialAssetManager = new ResoniteMaterialAssetManager(
            metadata.Request.Dataset,
            (containerSlotId, componentId, componentType, uriMemberName, importAssetAsync, ct) =>
                EnsureAssetComponentUrlKnownAsync(
                    setupClient,
                    containerSlotId,
                    componentId,
                    componentType,
                    uriMemberName,
                    importAssetAsync,
                    ct),
            (containerSlotId, componentId, componentType, members, ct) =>
                EnsureComponentKnownAsync(
                    setupClient,
                    containerSlotId,
                    componentId,
                    componentType,
                    members,
                    ct));
        resolvedTexturePathTasks = new ConcurrentDictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(metadata.Request.LocalSourcePath!, cancellationToken);
        terrainTextureOverlaysByPath = metadata.SourceDataset.TerrainTextureOverlays.ToDictionary(
            static overlay => overlay.TexturePath,
            StringComparer.Ordinal);
        cityObjectChannel = Channel.CreateBounded<QueuedCityObject>(
            new BoundedChannelOptions(Math.Max(MaxQueuedCityObjects, connectionCount))
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        processedCityObjectCount = 0;
        sceneBuildStopwatch = Stopwatch.StartNew();
        firstQueuedCityObjectLogged = 0;
        firstPreparedCityObjectLogged = 0;
        firstBuiltCityObjectLogged = 0;
        diagnostics.StartSendWindow(connectionCount);
        await Task.WhenAll(clients.Select(client => client.ConnectAsync(endpoint, cancellationToken)));
        processingTasks = clients
            .Select(client => ProcessQueuedCityObjectsAsync(cityObjectChannel.Reader, client, cancellationToken))
            .ToArray();
        ReportProgress(
            $"[live] Connected {connectionCount} ResoniteLink sessions to {endpoint} for dataset '{metadata.Request.Dataset}' mesh '{metadata.Request.MeshCode}'.");

        await EnsureSlotKnownAsync(
            setupClient,
            datasetSlotId,
            "Root",
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 1.5, 0.0),
            cancellationToken);
        await licenseManager.EnsureDatasetLicenseAsync(setupClient, datasetSlotId, datasetLicenseComponentId, cancellationToken);
        ResoniteFloat3 meshCodeRootPosition = await ResolveMeshCodeRootPositionAsync(
            setupClient,
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            cancellationToken);
        await EnsureSlotKnownAsync(
            setupClient,
            meshCodeSlotId,
            datasetSlotId,
            metadata.Request.MeshCode,
            meshCodeRootPosition,
            cancellationToken);

        await EnsureSlotKnownAsync(setupClient, datasetAssetsSlotId, datasetSlotId, "Assets", null, cancellationToken);
        await EnsureSlotKnownAsync(setupClient, commonAssetsSlotId, datasetAssetsSlotId, CommonAssetsSlotName, null, cancellationToken);
        ReportProgress("[live] Dataset slots and asset groups are ready.");
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(clients is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);

        await AwaitProcessingTasksIfCompletedAsync();

        Task<PreparedCityObject> preparationTask = PrepareCityObjectAsync(cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object queued after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }

        await cityObjectChannel.Writer.WriteAsync(
            new QueuedCityObject(cityObject, preparationTask),
            cancellationToken);
        await AwaitProcessingTasksIfCompletedAsync();
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        cityObjectChannel.Writer.TryComplete();
        await Task.WhenAll(processingTasks).WaitAsync(cancellationToken);
        diagnostics.CompleteSendWindow();
        ReportProgress($"[live] Completed {processedCityObjectCount} city objects.");
        return [$"{endpoint}#{meshCodeSlotId}"];
    }

    public async ValueTask DisposeAsync()
    {
        cityObjectChannel?.Writer.TryComplete();
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
        resolvedTexturePathTasks = null;
        terrainTextureOverlaysByPath = null;
        cityObjectChannel = null;
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
            PreparedCityObject preparedCityObject = await queuedCityObject.PreparationTask.WaitAsync(cancellationToken);
            await BuildPreparedCityObjectAsync(client, preparedCityObject, cancellationToken);
            int processedCount = Interlocked.Increment(ref processedCityObjectCount);
            ReportProgress(
                $"[live] Sent city object {processedCount}: "
                + $"{preparedCityObject.CityObject.DisplayName} "
                + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})");
        }
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
        ObjectDisposedException.ThrowIf(generatedAssetsRoot is null, this);
        ObjectDisposedException.ThrowIf(resolvedTexturePathTasks is null, this);
        ObjectDisposedException.ThrowIf(datasetContentSource is null, this);
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
                string absoluteTexturePath = await resolvedTexturePathTasks.GetOrAdd(
                    CreateTextureCacheKey(texture.TexturePath, texture.TextureSourceKind),
                    _ => ResolveTextureImportPathAsync(
                        datasetContentSource,
                        texture.TexturePath,
                        texture.TextureSourceKind,
                        cancellationToken));

                return new PreparedTextureReference(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    absoluteTexturePath);
            })
            .ToArray();
        Task<ImportMeshRawData> meshImportTask = Task.Run(() => ResoniteMeshImportFactory.Create(cityObject.Mesh), cancellationToken);
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await Task.WhenAll(texturePreparationTasks);
        ImportMeshRawData meshImport = await meshImportTask;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref firstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                + $"after scene start {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} "
                + $"(textures={preparedTextures.Length}, vertices={cityObject.Mesh.Vertices.Count}, submeshes={cityObject.Mesh.Submeshes.Count})");
        }

        return new PreparedCityObject(
            cityObject,
            meshImport,
            preparedTextures);
    }

    private async Task<string> ResolveTextureImportPathAsync(
        IPlateauDatasetContentSource datasetContentSource,
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(generatedAssetsRoot is null, this);
        ObjectDisposedException.ThrowIf(terrainTextureOverlaysByPath is null, this);

        if (terrainTextureOverlaysByPath.TryGetValue(texturePath, out TerrainTextureOverlay? terrainTextureOverlay))
        {
            return await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay,
                generatedAssetsRoot,
                cancellationToken);
        }

        return textureSourceKind switch
        {
            ResoniteTextureSourceKind.Dataset => await datasetContentSource.MaterializeFileAsync(
                texturePath,
                generatedAssetsRoot,
                cancellationToken),
            ResoniteTextureSourceKind.Bundled => BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };
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
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        string rootMeshCode = cityObject.ActualMeshCode;
        string rootMeshCodeSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            metadata.Request.Dataset,
            rootMeshCode,
            "meshcode");
        ResoniteFloat3 rootMeshCodePosition = await ResolveMeshCodeRootPositionAsync(client, metadata.Request.Dataset, rootMeshCode, cancellationToken);
        ResoniteFloat3 cityObjectLocalPosition = ResolveCityObjectLocalPosition(
            metadata.Request.MeshCode,
            rootMeshCode,
            cityObject.Transform.Position);
        await EnsureSlotKnownAsync(
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
        string meshAssetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "meshslot",
            rootMeshCode,
            objectIdentity);
        string staticMeshId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "staticmesh",
            rootMeshCode,
            objectIdentity);
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

        await EnsureSlotKnownAsync(
            client,
            packageSlotId,
            rootMeshCodeSlotId,
            cityObject.PackageName,
            null,
            cancellationToken);
        await EnsureSlotKnownAsync(client, lodSlotId, packageSlotId, FormatLodSlotName(cityObject.LodLevel), null, cancellationToken);
        Slot? existingCityObjectSlot = await client.GetSlotAsync(cityObjectSlotId, 0, cancellationToken);
        if (existingCityObjectSlot is null)
        {
            await AddSlotDirectAsync(
                client,
                cityObjectSlotId,
                lodSlotId,
                cityObject.DisplayName,
                cityObjectLocalPosition,
                cancellationToken);
        }

        await EnsureSlotKnownAsync(client, assetPackageSlotId, datasetAssetsSlotId, cityObject.PackageName, null, cancellationToken);
        await EnsureSlotKnownAsync(client, assetLodSlotId, assetPackageSlotId, FormatLodSlotName(cityObject.LodLevel), null, cancellationToken);

        await EnsureMeshAssetSlotKnownAsync(
            client,
            meshAssetSlotId,
            cityObject.DisplayName,
            assetLodSlotId,
            cancellationToken);

        await EnsureAssetComponentUrlKnownAsync(
            client,
            meshAssetSlotId,
            staticMeshId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            "URL",
            () => client.ImportMeshAsync(preparedCityObject.MeshImport, cancellationToken),
            cancellationToken);

        Dictionary<string, string> preparedTexturePathsByKey = preparedCityObject.Textures.ToDictionary(
            static texture => ResoniteMaterialAssetManager.CreateTextureCacheKey(
                texture.TexturePath,
                texture.TextureSourceKind),
            static texture => texture.AbsoluteTexturePath,
            StringComparer.OrdinalIgnoreCase);
        List<string> materialIds = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            string materialInstanceKey = ResoniteLinkEntityIdFactory.CreateStableEntityId(
                metadata.Request.Dataset,
                rootMeshCode,
                objectIdentity,
                "material",
                material.MaterialKey);
            string materialId = await materialAssetManager.EnsureMaterialComponentAsync(
                client,
                material,
                preparedTexturePathsByKey,
                meshAssetSlotId,
                materialInstanceKey,
                cancellationToken);
            materialIds.Add(materialId);
        }

        await EnsureComponentKnownAsync(
            client,
            cityObjectSlotId,
            rendererId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = new Reference
                {
                    TargetID = staticMeshId,
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

        await EnsureComponentKnownAsync(
            client,
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
                    TargetID = staticMeshId,
                },
            },
            cancellationToken);

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
            // DEM objects are shared across sibling mesh sends; keep identity stable even if source keys differ.
            return cityObject.SlotKey;
        }

        return cityObject.SourceObjectKey ?? cityObject.SlotKey;
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTextureCacheKey(string? texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        return texturePath is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{textureSourceKind}:{texturePath}");
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

    private static ResoniteFloat3 GetPositionOrDefault(Slot slot)
    {
        return slot.Position is Field_float3 position
            ? new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z)
            : new ResoniteFloat3(0.0, 0.0, 0.0);
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
        string requestMeshCode,
        string rootMeshCode,
        ResoniteFloat3 cityObjectPosition)
    {
        if (string.Equals(requestMeshCode, rootMeshCode, StringComparison.Ordinal))
        {
            return cityObjectPosition;
        }

        // City objects are produced in the request mesh-code frame; convert them
        // to the target mesh-code local frame because root mesh slots already carry
        // the inter-mesh-code offset in Resonite.
        ResoniteFloat3 rootOffsetFromRequest = ComputeMeshCodeOffset(requestMeshCode, rootMeshCode);
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
            Y: eun.z,
            Z: eun.y);
    }

    private async Task<ResoniteFloat3> ResolveMeshCodeRootPositionAsync(
        IResoniteLinkClient client,
        string dataset,
        string meshCode,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(datasetSlotId is null, this);

        string targetMeshCodeSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            dataset,
            meshCode,
            "meshcode");
        Slot? existingMeshCodeSlot = await client.GetSlotAsync(targetMeshCodeSlotId, 0, cancellationToken);
        if (existingMeshCodeSlot is not null)
        {
            return GetPositionOrDefault(existingMeshCodeSlot);
        }

        Slot? datasetSlot = await client.GetSlotAsync(datasetSlotId, 1, cancellationToken);
        Slot? referenceSlot = datasetSlot?.Children?
            .FirstOrDefault(slot =>
                !string.Equals(slot.ID, targetMeshCodeSlotId, StringComparison.Ordinal)
                && TryGetMeshCodeName(slot, out _));
        if (referenceSlot is null || !TryGetMeshCodeName(referenceSlot, out string referenceMeshCode))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return Add(
            GetPositionOrDefault(referenceSlot),
            ComputeMeshCodeOffset(referenceMeshCode, meshCode));
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
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(slotEnsureTasks is null, this);
        await GetOrRunOnceAsync(
            slotEnsureTasks,
            slotId,
            () => EnsureSlotAsync(client, slotId, parentId, slotName, position, cancellationToken),
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
        await EnsureSlotKnownAsync(client, slotId, parentId, slotName, null, cancellationToken);
    }

    private async Task<Uri> EnsureAssetComponentUrlKnownAsync(
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
            () => EnsureAssetComponentUrlAsync(
                client,
                containerSlotId,
                componentId,
                componentType,
                uriMemberName,
                importAssetAsync,
                cancellationToken),
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
        CancellationToken cancellationToken)
    {
        Slot? existingSlot = await client.GetSlotAsync(slotId, 0, cancellationToken);
        if (existingSlot is not null)
        {
            return;
        }

        await client.AddSlotAsync(
            new AddSlot
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
                },
            },
            cancellationToken);
    }

    private static async Task AddSlotDirectAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        CancellationToken cancellationToken)
    {
        await client.AddSlotAsync(
            new AddSlot
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
                },
            },
            cancellationToken);
    }

    private static async Task<Uri> EnsureAssetComponentUrlAsync(
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
            return existingUri;
        }

        Uri assetUri = await importAssetAsync();
        Dictionary<string, Member> members = new(StringComparer.Ordinal)
        {
            [uriMemberName] = new Field_Uri
            {
                Value = assetUri,
            },
        };

        if (existingComponent is null)
        {
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

    private static async Task EnsureComponentAsync(
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
            return;
        }

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

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask);

    private sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        ImportMeshRawData MeshImport,
        IReadOnlyList<PreparedTextureReference> Textures)
    {
        public bool TryGetTexturePath(
            string texturePath,
            ResoniteTextureSourceKind textureSourceKind,
            out string? absoluteTexturePath)
        {
            PreparedTextureReference? preparedTexture = Textures.FirstOrDefault(texture =>
                string.Equals(texture.TexturePath, texturePath, StringComparison.Ordinal)
                && texture.TextureSourceKind == textureSourceKind);
            absoluteTexturePath = preparedTexture?.AbsoluteTexturePath;
            return preparedTexture is not null;
        }
    }

    private sealed record PreparedTextureReference(
        string TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        string AbsoluteTexturePath);
}
