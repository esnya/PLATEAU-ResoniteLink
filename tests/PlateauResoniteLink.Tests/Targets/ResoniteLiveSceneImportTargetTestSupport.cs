using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportSlotLocator;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

internal static class ResoniteLiveSceneImportTargetTestSupport
{
    private static BundledDefaultMaterialAssetStore CreateBundledDefaultMaterialAssetStore() => new();

    public static async Task BuildSceneAsync(
        ImportedSceneMetadata metadata,
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
        _ = await ExecuteSceneAsync(
            builder,
            metadata,
            workDirectory.Path,
            cityObjects,
            commonMaterials: CollectExecutionPlanCommonMaterials(metadata, cityObjects));
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

    public static ImportedSceneMetadata CreateMetadata(
        string datasetName,
        string meshCode,
        string datasetRoot,
        ResoniteLocalOrigin localOrigin,
        IReadOnlyList<string>? packageNames = null,
        IReadOnlyList<string>? sourceFiles = null,
        IReadOnlyList<string>? requestedMeshCodes = null)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: $"PLATEAU {datasetName} {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: datasetName,
                MeshCode: meshCode,
                Source: DatasetLocation.Local(datasetRoot),
                PackageNames: packageNames ?? ["bldg"]),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: packageNames ?? ["bldg"],
                SourceFiles: sourceFiles ?? [],
                SelectedMeshCodes: requestedMeshCodes),
            Attribution: new Attribution(
                DatasetLicense: new LicenseMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license"),
                MaterialLicenses: []),
            GeodeticOrigin: new GeodeticOrigin(
                localOrigin.Latitude,
                localOrigin.Longitude,
                localOrigin.Altitude));
    }

    public static async Task BuildSceneTwiceAsync(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> firstRunCityObjects,
        IReadOnlyList<ResoniteConstructionCityObject> secondRunCityObjects,
        SceneBuilderRecordingClient client,
        bool enableMeshBake = true)
    {
        using TemporaryDirectory firstWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget builder = CreateBuilder(client, enableMeshBake: enableMeshBake))
        {
            _ = await ExecuteSceneAsync(
                builder,
                metadata,
                firstWorkDirectory.Path,
                firstRunCityObjects,
                commonMaterials: CollectExecutionPlanCommonMaterials(metadata, firstRunCityObjects));
        }

        using TemporaryDirectory secondWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget builder = CreateBuilder(client, enableMeshBake: enableMeshBake))
        {
            _ = await ExecuteSceneAsync(
                builder,
                metadata,
                secondWorkDirectory.Path,
                secondRunCityObjects,
                commonMaterials: CollectExecutionPlanCommonMaterials(metadata, secondRunCityObjects));
        }
    }

    public static Task<SceneImportExecutionResult> ExecuteSceneAsync(
        ResoniteLiveSceneImportTarget builder,
        ImportedSceneMetadata metadata,
        string workDirectory,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        IReadOnlyList<MaterialBinding>? commonMaterials = null,
        CancellationToken cancellationToken = default)
    {
        return builder.ExecuteAsync(
            CreateExecutionPlan(
                metadata,
                workDirectory,
                commonMaterials: commonMaterials ?? CollectExecutionPlanCommonMaterials(metadata, cityObjects)),
            CreateImportedObjectUnitsAsync(cityObjects, cancellationToken),
            cancellationToken);
    }

    public static SceneImportExecutionPlan CreateExecutionPlan(
        ImportedSceneMetadata metadata,
        string workDirectory,
        PlateauImportRequest? normalizedRequest = null,
        IReadOnlyList<MaterialBinding>? commonMaterials = null)
    {
        PlateauImportRequest effectiveNormalizedRequest = normalizedRequest ?? metadata.Request;
        PlateauImportRequest resolvedRequest = CreateResolvedRequest(
            effectiveNormalizedRequest,
            metadata.Request,
            workDirectory);
        PlateauImportRequest buildRequest = CreateBuildRequest(effectiveNormalizedRequest, resolvedRequest);
        ImportedSceneMetadata effectiveMetadata = metadata with
        {
            Request = buildRequest,
        };

        return SceneImportExecutionPlan.Create(
            effectiveNormalizedRequest,
            resolvedRequest,
            effectiveMetadata,
            GetRequiredResolvedLocalSourcePath(resolvedRequest),
            workDirectory,
            commonMaterials ?? new CommonMaterialCatalog().CreateForPackages(metadata.SourceDataset.PackageNames));
    }

    private static PlateauImportRequest CreateBuildRequest(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest)
    {
        return normalizedRequest with
        {
            Source = resolvedRequest.Source,
            DemTextureSource = resolvedRequest.DemTextureSource,
        };
    }

    private static IReadOnlyList<MaterialBinding> CollectExecutionPlanCommonMaterials(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        Dictionary<string, ResoniteMaterialBinding> materialsByKey = new(StringComparer.Ordinal);

        foreach (MaterialBinding material in new CommonMaterialCatalog().CreateForPackages(metadata.SourceDataset.PackageNames))
        {
            AddNormalizedCommonMaterial(materialsByKey, SceneImportContractMapper.ToInternal(material));
        }

        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            IReadOnlyList<ResoniteMaterialBinding> candidateMaterials;
            try
            {
                candidateMaterials = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject).Materials;
            }
            catch (ArgumentException)
            {
                candidateMaterials = cityObject.Materials;
            }

            foreach (ResoniteMaterialBinding material in candidateMaterials)
            {
                AddNormalizedCommonMaterial(materialsByKey, material);
            }
        }

        return ToContractMaterials(
            materialsByKey.Values
                .OrderBy(static material => material.MaterialKey, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddNormalizedCommonMaterial(
        IDictionary<string, ResoniteMaterialBinding> materialsByKey,
        ResoniteMaterialBinding material)
    {
        ResoniteMaterialBinding normalizedCommonMaterial =
            ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);
        if (normalizedCommonMaterial.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            materialsByKey.TryAdd(normalizedCommonMaterial.MaterialKey, normalizedCommonMaterial);
            return;
        }

        if (ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
                material,
                out ResoniteMaterialBinding normalizedSharedMaterial,
                out _))
        {
            materialsByKey.TryAdd(normalizedSharedMaterial.MaterialKey, normalizedSharedMaterial);
        }
    }

    private static PlateauImportRequest CreateResolvedRequest(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest metadataRequest,
        string workDirectory)
    {
        string? resolvedSourcePath = ResolveLocalPath(normalizedRequest.Source, workDirectory, "source-archive")
            ?? metadataRequest.LocalSourcePath;
        if (string.IsNullOrWhiteSpace(resolvedSourcePath))
        {
            throw new ArgumentException("Metadata request must include a local source path.", nameof(metadataRequest));
        }

        DatasetLocation? resolvedDemTextureSource = metadataRequest.DemTextureSource;
        if (normalizedRequest.DemTextureSource is not null)
        {
            string? resolvedDemTexturePath = ResolveLocalPath(normalizedRequest.DemTextureSource, workDirectory, "source-ortho");
            resolvedDemTextureSource = resolvedDemTexturePath is null
                ? metadataRequest.DemTextureSource
                : DatasetLocation.Local(resolvedDemTexturePath);
        }

        return normalizedRequest with
        {
            Source = DatasetLocation.Local(resolvedSourcePath),
            DemTextureSource = resolvedDemTextureSource,
        };
    }

    private static string GetRequiredResolvedLocalSourcePath(PlateauImportRequest resolvedRequest)
    {
        return resolvedRequest.LocalSourcePath
            ?? throw new ArgumentException("Resolved request must include a local source path.", nameof(resolvedRequest));
    }

    private static string? ResolveLocalPath(
        DatasetLocation source,
        string workDirectory,
        string prefix)
    {
        return source switch
        {
            LocalDatasetLocation localSource => localSource.LocalSourcePath,
            RemoteDatasetLocation remoteSource => RemoteDatasetResourceLayout.GetRemoteResourcePath(
                workDirectory,
                remoteSource.ServerUri ?? throw new ArgumentException("Remote source must include a URI.", nameof(source)),
                prefix),
            _ => null,
        };
    }

    public static Slot FindUniqueSlotByPathSuffix(SceneBuilderRecordingClient client, string suffix)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && path.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static Slot[] FindSlotsByPathSuffix(SceneBuilderRecordingClient client, string suffix)
    {
        return client.SlotsById.Values
            .Where(slot => client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && path.EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(slot => client.SlotPaths[slot.ID!], StringComparer.Ordinal)
            .ThenBy(slot => slot.ID, StringComparer.Ordinal)
            .ToArray();
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
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                enableMeshBake,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session ?? new DelegatingClientSession(routedClient),
                diagnostics,
                terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(
                    new ResoniteSceneSlotLocator(),
                    new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                    new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateImportedObjectUnitsAsync(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportedCityObject importedCityObject = ImportedDynamicMaterialUvNormalizer.Normalize(ToImportedCityObject(cityObject));
            string scopeKey = importedCityObject.SourceFileRelativePath ?? importedCityObject.ObjectKey;
            string scopePath = importedCityObject.SourceFileRelativePath ?? scopeKey;
            yield return new ImportedObjectUnit(
                scopeKey,
                scopePath,
                importedCityObject.PackageName,
                importedCityObject.LodLevel,
                [importedCityObject],
                importedCityObject.ActualMeshCode);
        }
    }

    public static MaterialBinding[] ToContractMaterials(IReadOnlyList<ResoniteMaterialBinding> bindings)
    {
        return bindings.Select(ToContractMaterial).ToArray();
    }

    public static ImportedCityObject ToImportedCityObject(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContractTransform(cityObject.Transform),
                ToContractMesh(triangleMesh.Mesh),
                cityObject.Materials.Select(ToContractMaterial).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath),
            ResoniteHeightMapGridGeometry heightMap => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContractTransform(cityObject.Transform),
                new HeightMapGridGeometry(
                    heightMap.Width,
                    heightMap.Height,
                    ToContractFloat2(heightMap.Size),
                    heightMap.MinHeight,
                    heightMap.MaxHeight,
                    heightMap.HeightSamples,
                    heightMap.UvScale is null ? null : ToContractFloat2(heightMap.UvScale),
                    heightMap.UvOffset is null ? null : ToContractFloat2(heightMap.UvOffset)),
                cityObject.Materials.Select(ToContractMaterial).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    public static MaterialBinding ToContractMaterial(ResoniteMaterialBinding binding)
    {
        return new MaterialBinding(
            binding.MaterialKey,
            ToContractColor(binding.BaseColor),
            (MaterialType)binding.MaterialType,
            binding.TexturePayload is null ? null : ToContractTexturePayload(binding.TexturePayload),
            (TextureSourceKind)binding.TextureSourceKind,
            (MaterialProjection)binding.Projection,
            binding.DepthOffset is null ? null : new MaterialDepthOffset(binding.DepthOffset.Factor, binding.DepthOffset.Units),
            binding.SubmeshIndices,
            binding.TextureScale is null ? null : ToContractFloat2(binding.TextureScale),
            binding.Family,
            binding.TextureOffset is null ? null : ToContractFloat2(binding.TextureOffset),
            binding.AssetScope == ResoniteMaterialAssetScope.Common ? MaterialReuseScope.Shared : MaterialReuseScope.PerObject,
            binding.TerrainOverlay,
            binding.BundledVariantIndex);
    }

    private static Transform3D ToContractTransform(ResoniteTransform transform)
        => new(
            ToContractFloat3(transform.Position),
            transform.Rotation is null ? null : new Quaternion(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W));

    private static Float2 ToContractFloat2(ResoniteFloat2 value) => new(value.X, value.Y);

    private static Float3 ToContractFloat3(ResoniteFloat3 value) => new(value.X, value.Y, value.Z);

    private static ColorRgba ToContractColor(ResoniteColor value) => new(value.R, value.G, value.B, value.A);

    private static ImportedMesh ToContractMesh(ResoniteImportedMesh mesh)
        => new(
            mesh.Vertices.Select(static vertex => new MeshVertex(
                new Float3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
                new Float3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z),
                new Float2(vertex.UV0.X, vertex.UV0.Y),
                vertex.Color is null ? null : new ColorRgba(vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A))).ToArray(),
            mesh.Submeshes.Select(static submesh => new MeshSubmesh(submesh.Index, submesh.MaterialKey, submesh.TriangleVertexIndices)).ToArray());

    private static TexturePayload ToContractTexturePayload(ResoniteTexturePayload payload)
        => new(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload.AsSpan().ToArray(),
            payload.Identity,
            (TexturePayloadFormat)payload.Format);

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

    public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
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

        return Task.FromResult(
            new ResoniteTransportComponentCreationResult(new TransportComponentLocator(createdComponentId)));
    }

    public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
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

        return Task.FromResult(new ResoniteTransportSlotCreationResult(new TransportSlotLocator(createdSlotId)));
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
                        EntityId = (await AddSlotAsync(ResolveBatchAddSlot(addSlot, localSlotIds), cancellationToken)).Slot.Value,
                    });
                    break;
                case AddComponent addComponent:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addComponent.MessageID,
                        EntityId = (await AddComponentAsync(
                            ResolveBatchAddComponent(addComponent, localSlotIds, localComponentIds),
                            cancellationToken)).Component.Value,
                    });
                    break;
                case UpdateComponent updateComponent:
                    await UpdateComponentAsync(
                        new PlateauResoniteLink.Transport.ResoniteLink.ResoniteComponentUpdate
                        {
                            Component = new TransportComponentLocator(updateComponent.Data.ID!),
                            Members = new Dictionary<string, Member>(updateComponent.Data.Members, StringComparer.Ordinal),
                        },
                        cancellationToken);
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

    public Task<Component?> GetComponentAsync(TransportComponentLocator component, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ComponentsById.TryGetValue(component.Value, out Component? resolvedComponent);
            return Task.FromResult(resolvedComponent);
        }
    }

    public Task<Slot?> GetSlotAsync(TransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (slot.IsRoot)
        {
            return Task.FromResult<Slot?>(CreateSyntheticRoot(depth));
        }

        lock (gate)
        {
            return Task.FromResult<Slot?>(
                SlotsById.TryGetValue(slot.Value, out Slot? resolvedSlot)
                    ? CloneSlot(resolvedSlot, depth)
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

    public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            UpdatedComponents.Add(new UpdateComponent
            {
                Data = new Component
                {
                    ID = request.Component.Value,
                    Members = request.Members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            });
            if (!ComponentsById.TryGetValue(request.Component.Value, out Component? existingComponent))
            {
                return Task.CompletedTask;
            }

            foreach ((string memberName, Member member) in request.Members)
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

    private static string? TryResolveLocalId(string? id, IReadOnlyDictionary<string, string> localIds)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return localIds.TryGetValue(id, out string? resolvedId)
            ? resolvedId
            : id;
    }
}

internal sealed class RecordingTerrainTextureAssetGenerator(
    Func<TerrainTextureOverlay, GeneratedTerrainTexture> textureFactory) : ITerrainTextureAssetGenerator
{
    public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

    public Task<GeneratedTerrainTexture> EnsureTextureAsync(
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
    Func<LiveSendConnectionRequest, CancellationToken, Task>? ensureConnectedAsync = null) : ILiveSendClientSession
{
    private readonly IResoniteLinkClient? defaultRoutedClient = routedClient;

    public IResoniteLinkClient? ConnectedClient { get; set; } = routedClient;

    public ResoniteLinkSendDiagnostics Diagnostics { get; } = ResoniteLinkSendDiagnostics.Disabled;

    public IResoniteLinkClient GetRequiredClient()
    {
        return ConnectedClient
            ?? throw new InvalidOperationException("Connected ResoniteLink client is not available.");
    }

    public int EnsureConnectedCallCount { get; private set; }

    public int DisposeClientsCallCount { get; private set; }

    public int ResetClientsCallCount { get; private set; }

    public List<LiveSendConnectionRequest> EnsureConnectedRequests { get; } = [];

    public Task EnsureConnectedAsync(
        LiveSendConnectionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConnectedCallCount++;
        EnsureConnectedRequests.Add(request);
        ConnectedClient ??= defaultRoutedClient;
        return ensureConnectedAsync is null
            ? Task.CompletedTask
            : ensureConnectedAsync(request, cancellationToken);
    }

    public ValueTask ResetClientsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetClientsCallCount++;
        ConnectedClient = null;
        return ValueTask.CompletedTask;
    }

    public void DisposeClients()
    {
        DisposeClientsCallCount++;
        ConnectedClient = null;
    }
}
[CollectionDefinition(BundledCompanionTextureIsolationGroup.Name, DisableParallelization = true)]
public sealed class BundledCompanionTextureIsolationGroup
{
    public const string Name = "BundledCompanionTextureIsolation";
}
