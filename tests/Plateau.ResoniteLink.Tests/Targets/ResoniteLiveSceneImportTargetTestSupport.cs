using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

internal static class ResoniteLiveSceneImportTargetTestSupport
{
    public static async Task BuildSceneAsync(
        ResoniteConstructionMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        SceneBuilderRecordingClient client,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        bool enableMeshBake = true)
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder(
            client,
            terrainTextureAssetGenerator,
            enableMeshBake);

        using TemporaryDirectory workDirectory = new();
        _ = await ExecuteSceneAsync(builder, metadata, workDirectory.Path, cityObjects);
    }

    public static ResoniteImportedMesh CreateTriangleMesh(
        string materialKey,
        double firstY = 0.0,
        double secondY = 0.0,
        double thirdY = 0.0)
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, firstY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, secondY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, thirdY, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, materialKey, [0, 1, 2]),
            ]);
    }

    public static ResoniteTexturePayload CreateSolidColorPayload(
        byte r,
        byte g,
        byte b,
        string identity)
    {
        byte[] rawBytes =
        [
            r, g, b, 255,
            r, g, b, 255,
            r, g, b, 255,
            r, g, b, 255,
        ];
        return new ResoniteTexturePayload(2, 2, ResoniteTextureColorProfiles.Srgb, rawBytes, identity);
    }

    public static ResoniteConstructionMetadata CreateMetadata(
        string datasetName,
        string meshCode,
        string datasetRoot,
        ResoniteLocalOrigin localOrigin,
        IReadOnlyList<string>? packageNames = null,
        IReadOnlyList<string>? sourceFiles = null,
        IReadOnlyList<TerrainTextureOverlay>? terrainTextureOverlays = null,
        IReadOnlyList<string>? requestedMeshCodes = null)
    {
        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {datasetName} {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: datasetName,
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot,
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: packageNames ?? ["bldg"],
                SourceFiles: sourceFiles ?? [],
                TerrainTextureOverlays: terrainTextureOverlays ?? [],
                RequestedMeshCodes: requestedMeshCodes),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license"),
                MaterialLicenses: []),
            LocalOrigin: localOrigin);
    }

    public static async Task BuildSceneTwiceAsync(
        ResoniteConstructionMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> firstRunCityObjects,
        IReadOnlyList<ResoniteConstructionCityObject> secondRunCityObjects,
        SceneBuilderRecordingClient client)
    {
        using TemporaryDirectory firstWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget builder = CreateBuilder(client))
        {
            _ = await ExecuteSceneAsync(builder, metadata, firstWorkDirectory.Path, firstRunCityObjects);
        }

        using TemporaryDirectory secondWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget builder = CreateBuilder(client))
        {
            _ = await ExecuteSceneAsync(builder, metadata, secondWorkDirectory.Path, secondRunCityObjects);
        }
    }

    public static Task<SceneImportExecutionResult> ExecuteSceneAsync(
        ResoniteLiveSceneImportTarget builder,
        ResoniteConstructionMetadata metadata,
        string workDirectory,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        return builder.ExecuteAsync(
            CreateExecutionPlan(metadata, workDirectory),
            CreateImportedCityObjectsAsync(cityObjects, cancellationToken),
            cancellationToken);
    }

    public static SceneImportExecutionPlan CreateExecutionPlan(
        ResoniteConstructionMetadata metadata,
        string workDirectory,
        PlateauImportRequest? normalizedRequest = null,
        IPlateauDatasetContentSource? datasetContentSource = null)
    {
        return SceneImportExecutionPlan.Create(
            normalizedRequest ?? metadata.Request,
            SceneImportContractMapper.ToContract(metadata),
            datasetContentSource ?? new TestDatasetContentSource(metadata.Request.LocalSourcePath ?? throw new ArgumentException("Metadata request must include a local source path.", nameof(metadata))),
            workDirectory);
    }

    public static Slot FindUniqueSlotByPathSuffix(SceneBuilderRecordingClient client, string suffix)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && path.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static Slot FindUniqueSlotByNameOutsideAssets(SceneBuilderRecordingClient client, string name)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, name, StringComparison.Ordinal)
                && client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && !path.Contains("/Assets/", StringComparison.Ordinal));
    }

    public static bool IsDescendantOf(SceneBuilderRecordingClient client, string slotId, string ancestorSlotId)
    {
        string? currentSlotId = slotId;
        while (!string.IsNullOrWhiteSpace(currentSlotId)
               && client.SlotsById.TryGetValue(currentSlotId, out Slot? slot)
               && slot.Parent is not null)
        {
            if (string.Equals(slot.Parent.TargetID, ancestorSlotId, StringComparison.Ordinal))
            {
                return true;
            }

            currentSlotId = slot.Parent.TargetID;
        }

        return false;
    }

    public static ResoniteLiveSceneImportTarget CreateBuilder(
        IResoniteLinkClient routedClient,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        bool enableMeshBake = true,
        DelegatingClientSession? session = null)
    {
        return new ResoniteLiveSceneImportTarget(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session ?? new DelegatingClientSession(routedClient),
                terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator()),
            enableMeshBake,
            progressReporter: null);
    }

    private static async IAsyncEnumerable<ImportedCityObject> CreateImportedCityObjectsAsync(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return SceneImportContractMapper.ToContract(cityObject);
        }
    }
}

internal sealed class SceneBuilderRecordingClient : IResoniteLinkClient
{
    private readonly object gate = new();
    private int nextComponentId;
    private int nextSlotId;

    public List<AddComponent> AddedComponents { get; } = [];

    public List<AddSlot> AddedSlots { get; } = [];

    public List<ImportMeshRawData> ImportedMeshes { get; } = [];

    public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

    public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

    public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

    public List<UpdateComponent> UpdatedComponents { get; } = [];

    public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> SlotPaths { get; } = new(StringComparer.Ordinal);

    public int ConnectCallCount { get; private set; }

    public void Dispose()
    {
    }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCallCount++;
        return Task.CompletedTask;
    }

    public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string createdComponentId = string.IsNullOrWhiteSpace(request.Data.ID)
            ? AllocateComponentId()
            : request.Data.ID;
        lock (gate)
        {
            request.Data.ID = createdComponentId;
            ComponentsById[createdComponentId] = request.Data;
            if (SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
            {
                containerSlot.Components ??= [];
                containerSlot.Components.Add(request.Data);
            }

            AddedComponents.Add(request);
        }

        return Task.FromResult(createdComponentId);
    }

    public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
            ? AllocateSlotId()
            : request.Data.ID;
        lock (gate)
        {
            request.Data.ID = createdSlotId;
            SlotsById[createdSlotId] = request.Data;
            SlotPaths[createdSlotId] = CreateSlotPath(request.Data);
            AddedSlots.Add(request);
        }

        return Task.FromResult(createdSlotId);
    }

    public async Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, string> localSlotIds = operations
            .OfType<AddSlot>()
            .Where(static operation => !string.IsNullOrWhiteSpace(operation.Data.ID))
            .ToDictionary(static operation => operation.Data.ID, _ => AllocateSlotId(), StringComparer.Ordinal);
        Dictionary<string, string> localComponentIds = operations
            .OfType<AddComponent>()
            .Where(static operation => !string.IsNullOrWhiteSpace(operation.Data.ID))
            .ToDictionary(static operation => operation.Data.ID, _ => AllocateComponentId(), StringComparer.Ordinal);

        lock (gate)
        {
            Batches.Add(operations.ToArray());
        }

        List<Response> responses = [];
        foreach (DataModelOperation operation in operations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addSlot.MessageID,
                        EntityId = await AddSlotAsync(ResolveBatchAddSlot(addSlot, localSlotIds), cancellationToken),
                    });
                    break;
                case AddComponent addComponent:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addComponent.MessageID,
                        EntityId = await AddComponentAsync(
                            ResolveBatchAddComponent(addComponent, localSlotIds, localComponentIds),
                            cancellationToken),
                    });
                    break;
                case UpdateComponent updateComponent:
                    await UpdateComponentAsync(updateComponent, cancellationToken);
                    responses.Add(new Response
                    {
                        Success = true,
                        SourceMessageID = updateComponent.MessageID,
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
            }
        }

        return new BatchResponse
        {
            Success = true,
            Responses = responses,
        };
    }

    public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ComponentsById.TryGetValue(componentId, out Component? component);
            return Task.FromResult(component);
        }
    }

    public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(slotId, "Root", StringComparison.Ordinal))
        {
            return Task.FromResult<Slot?>(CreateSyntheticRoot(depth));
        }

        lock (gate)
        {
            return Task.FromResult<Slot?>(
                SlotsById.TryGetValue(slotId, out Slot? slot)
                    ? CloneSlot(slot, depth)
                    : null);
        }
    }

    public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ImportedMeshes.Add(request);
            return Task.FromResult(new Uri($"resdb:///mesh/{ImportedMeshes.Count - 1}", UriKind.Absolute));
        }
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            switch (textureImport)
            {
                case ResoniteRawTextureImport rawImport:
                    ImportedRawTextures.Add(rawImport);
                    break;
                case ResoniteRawHdrTextureImport rawHdrImport:
                    ImportedRawHdrTextures.Add(rawHdrImport);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
            }
            return Task.FromResult(new Uri($"resdb:///texture/{ImportedRawTextures.Count + ImportedRawHdrTextures.Count - 1}", UriKind.Absolute));
        }
    }

    public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            UpdatedComponents.Add(new UpdateComponent
            {
                Data = new Component
                {
                    ID = request.Data.ID,
                    Members = request.Data.Members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            });
            if (!ComponentsById.TryGetValue(request.Data.ID, out Component? existingComponent))
            {
                return Task.CompletedTask;
            }

            foreach ((string memberName, Member member) in request.Data.Members)
            {
                existingComponent.Members[memberName] = member;
            }
        }

        return Task.CompletedTask;
    }

    private string AllocateComponentId()
    {
        return string.Create(CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");
    }

    private string AllocateSlotId()
    {
        return string.Create(CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
    }

    private string CreateSlotPath(Slot slot)
    {
        string slotName = slot.Name?.Value ?? "<unnamed>";
        if (slot.Parent is null || string.IsNullOrWhiteSpace(slot.Parent.TargetID))
        {
            return slotName;
        }

        if (!SlotPaths.TryGetValue(slot.Parent.TargetID, out string? parentPath))
        {
            return slotName;
        }

        return $"{parentPath}/{slotName}";
    }

    private Slot CreateSyntheticRoot(int depth)
    {
        Slot root = new()
        {
            ID = "Root",
            Name = new Field_string
            {
                Value = "Root",
            },
        };

        if (depth <= 0)
        {
            return root;
        }

        lock (gate)
        {
            root.Children = SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
        }

        return root;
    }

    private Slot CloneSlot(Slot source, int depth)
    {
        Slot clone = new()
        {
            ID = source.ID,
            Parent = source.Parent,
            Name = source.Name,
            Position = source.Position,
            Rotation = source.Rotation,
            Components = source.Components,
        };

        if (depth <= 0)
        {
            return clone;
        }

        clone.Children = SlotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
            .Select(slot => CloneSlot(slot, depth - 1))
            .ToList();
        return clone;
    }

    private static AddSlot ResolveBatchAddSlot(AddSlot addSlot, IReadOnlyDictionary<string, string> localSlotIds)
    {
        return new AddSlot
        {
            MessageID = addSlot.MessageID,
            Data = new Slot
            {
                ID = TryResolveLocalId(addSlot.Data.ID, localSlotIds),
                Parent = addSlot.Data.Parent is null
                    ? null
                    : new Reference
                    {
                        TargetID = TryResolveLocalId(addSlot.Data.Parent.TargetID, localSlotIds),
                    },
                Name = addSlot.Data.Name,
                Position = addSlot.Data.Position,
                Rotation = addSlot.Data.Rotation,
                Tag = addSlot.Data.Tag,
            },
        };
    }

    private static AddComponent ResolveBatchAddComponent(
        AddComponent addComponent,
        IReadOnlyDictionary<string, string> localSlotIds,
        IReadOnlyDictionary<string, string> localComponentIds)
    {
        return new AddComponent
        {
            MessageID = addComponent.MessageID,
            ContainerSlotId = TryResolveLocalId(addComponent.ContainerSlotId, localSlotIds),
            Data = new Component
            {
                ID = TryResolveLocalId(addComponent.Data.ID, localComponentIds),
                ComponentType = addComponent.Data.ComponentType,
                Members = addComponent.Data.Members.ToDictionary(
                    static pair => pair.Key,
                    pair => pair.Value is Reference reference
                        ? (Member)new Reference
                        {
                            TargetID = TryResolveLocalId(
                                TryResolveLocalId(reference.TargetID, localSlotIds),
                                localComponentIds),
                        }
                        : pair.Value,
                    StringComparer.Ordinal),
            },
        };
    }

    private static string TryResolveLocalId(string id, IReadOnlyDictionary<string, string> localIds)
    {
        return localIds.TryGetValue(id, out string? resolvedId)
            ? resolvedId
            : id;
    }
}

internal sealed class RecordingTerrainTextureAssetGenerator(
    Func<TerrainTextureOverlay, ResoniteRawTextureImport> textureFactory) : ITerrainTextureAssetGenerator
{
    public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

    public Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedOverlays.Add(terrainTextureOverlay);
        return Task.FromResult(textureFactory(terrainTextureOverlay));
    }
}

internal sealed class DelegatingClientSession(
    IResoniteLinkClient? routedClient = null,
    Func<PlateauImportRequest, CancellationToken, Task>? ensureConnectedAsync = null) : ILiveSendClientSession
{
    private readonly IResoniteLinkClient? defaultRoutedClient = routedClient;

    public IResoniteLinkClient? RoutedClient { get; set; } = routedClient;

    public int BeginWorkerClientTrackingCallCount { get; private set; }

    public int EnsureConnectedCallCount { get; private set; }

    public int DisposeClientsCallCount { get; private set; }

    public int ResetClientsCallCount { get; private set; }

    public List<PlateauImportRequest> EnsureConnectedRequests { get; } = [];

    public void BeginWorkerClientTracking()
    {
        BeginWorkerClientTrackingCallCount++;
    }

    public Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConnectedCallCount++;
        EnsureConnectedRequests.Add(request);
        RoutedClient ??= defaultRoutedClient;
        return ensureConnectedAsync is null
            ? Task.CompletedTask
            : ensureConnectedAsync(request, cancellationToken);
    }

    public ValueTask ResetClientsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetClientsCallCount++;
        RoutedClient = null;
        return ValueTask.CompletedTask;
    }

    public void DisposeClients()
    {
        DisposeClientsCallCount++;
        RoutedClient = null;
    }
}

internal sealed class TestDatasetContentSource(string sourcePath) : IPlateauDatasetContentSource
{
    public string SourcePath { get; } = sourcePath;

    public IReadOnlyList<string> EnumerateFiles()
    {
        return [];
    }

    public bool FileExists(string relativePath)
    {
        return false;
    }

    public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<string> MaterializeFileAsync(
        string relativePath,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

[CollectionDefinition(BundledCompanionTextureIsolationGroup.Name, DisableParallelization = true)]
public sealed class BundledCompanionTextureIsolationGroup
{
    public const string Name = "BundledCompanionTextureIsolation";
}
