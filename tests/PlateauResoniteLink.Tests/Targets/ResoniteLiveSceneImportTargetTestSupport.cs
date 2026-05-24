using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

internal static class ResoniteLiveSceneImportTargetTestSupport
{
    private static BundledDefaultMaterialAssetStore CreateBundledDefaultMaterialAssetStore() => new();

    public static async Task ExecuteSceneAsync(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        SceneSinkRecordingClient client,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        bool enableMeshBake = true,
        CommonMaterialCatalog<DefaultCommonMaterialMember>? commonMaterials = null)
    {
        await using ResoniteLiveSceneImportTarget importTarget = CreateImportTarget(
            client,
            terrainTextureAssetGenerator,
            enableMeshBake);

        using TemporaryDirectory workDirectory = new();
        _ = await ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            cityObjects,
            commonMaterials: commonMaterials ?? CommonMaterialCatalog.Create());
    }

    public static ResoniteImportedMesh CreateTriangleMesh(
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
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
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
                CityGmlSource: DatasetLocation.Local(datasetRoot),
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
                    LicenseUrl: "https://example.invalid/license")),
            GeodeticOrigin: new GeodeticOrigin(
                localOrigin.Latitude,
                localOrigin.Longitude,
                localOrigin.Altitude));
    }

    public static async Task ExecuteSceneTwiceAsync(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> firstRunCityObjects,
        IReadOnlyList<ResoniteConstructionCityObject> secondRunCityObjects,
        SceneSinkRecordingClient client,
        bool enableMeshBake = true)
    {
        using TemporaryDirectory firstWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget importTarget = CreateImportTarget(client, enableMeshBake: enableMeshBake))
        {
            _ = await ExecuteSceneAsync(
                importTarget,
                metadata,
                firstWorkDirectory.Path,
                firstRunCityObjects,
                commonMaterials: CommonMaterialCatalog.Create());
        }

        using TemporaryDirectory secondWorkDirectory = new();
        await using (ResoniteLiveSceneImportTarget importTarget = CreateImportTarget(client, enableMeshBake: enableMeshBake))
        {
            _ = await ExecuteSceneAsync(
                importTarget,
                metadata,
                secondWorkDirectory.Path,
                secondRunCityObjects,
                commonMaterials: CommonMaterialCatalog.Create());
        }
    }

    public static Task<SceneImportExecutionResult> ExecuteSceneAsync(
        ResoniteLiveSceneImportTarget importTarget,
        ImportedSceneMetadata metadata,
        string workDirectory,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        CommonMaterialCatalog<DefaultCommonMaterialMember>? commonMaterials = null,
        CancellationToken cancellationToken = default)
    {
        return importTarget.ExecuteAsync(
            CreateExecutionPlan(
                metadata,
                workDirectory,
                commonMaterials: commonMaterials ?? CommonMaterialCatalog.Create()),
            CreateImportedObjectUnitsAsync(cityObjects, cancellationToken),
            cancellationToken);
    }

    public static SceneImportExecutionPlan CreateExecutionPlan(
        ImportedSceneMetadata metadata,
        string workDirectory,
        PlateauImportRequest? normalizedRequest = null,
        CommonMaterialCatalog<DefaultCommonMaterialMember>? commonMaterials = null)
    {
        PlateauImportRequest effectiveNormalizedRequest = normalizedRequest ?? metadata.Request;
        PlateauImportRequest resolvedRequest = CreateResolvedRequest(
            effectiveNormalizedRequest,
            metadata.Request,
            workDirectory);
        PlateauImportRequest importRequest = CreateImportRequest(effectiveNormalizedRequest, resolvedRequest);
        ImportedSceneMetadata effectiveMetadata = metadata with
        {
            Request = importRequest,
        };

        return SceneImportExecutionPlan.Create(
            effectiveNormalizedRequest,
            resolvedRequest,
            effectiveMetadata,
            GetRequiredResolvedLocalSourcePath(resolvedRequest),
            workDirectory,
            commonMaterials ?? CommonMaterialCatalog.Create());
    }

    private static PlateauImportRequest CreateImportRequest(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest)
    {
        return normalizedRequest with
        {
            CityGmlSource = resolvedRequest.CityGmlSource,
            DemTextureSource = resolvedRequest.DemTextureSource,
        };
    }

    private static PlateauImportRequest CreateResolvedRequest(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest metadataRequest,
        string workDirectory)
    {
        string? resolvedSourcePath = ResolveLocalPath(normalizedRequest.CityGmlSource, workDirectory, "source-archive")
            ?? metadataRequest.CityGmlLocalSourcePath;
        if (string.IsNullOrWhiteSpace(resolvedSourcePath))
        {
            throw new ArgumentException("Metadata request must include a local CityGML source path.", nameof(metadataRequest));
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
            CityGmlSource = DatasetLocation.Local(resolvedSourcePath),
            DemTextureSource = resolvedDemTextureSource,
        };
    }

    private static string GetRequiredResolvedLocalSourcePath(PlateauImportRequest resolvedRequest)
    {
        return resolvedRequest.CityGmlLocalSourcePath
            ?? throw new ArgumentException("Resolved request must include a local CityGML source path.", nameof(resolvedRequest));
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

    public static Slot FindUniqueSlotByPathSuffix(SceneSinkRecordingClient client, string suffix)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && path.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static Slot[] FindSlotsByPathSuffix(SceneSinkRecordingClient client, string suffix)
    {
        return client.SlotsById.Values
            .Where(slot => client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && path.EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(slot => client.SlotPaths[slot.ID!], StringComparer.Ordinal)
            .ThenBy(slot => slot.ID, StringComparer.Ordinal)
            .ToArray();
    }

    public static Slot FindUniqueSlotByNameOutsideAssets(SceneSinkRecordingClient client, string name)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, name, StringComparison.Ordinal)
                && client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && !path.Contains("/Assets/", StringComparison.Ordinal));
    }

    public static bool IsDescendantOf(SceneSinkRecordingClient client, string slotId, string ancestorSlotId)
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

    public static ResoniteLiveSceneImportTarget CreateImportTarget(
        IResoniteLinkClient routedClient,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        bool enableMeshBake = true,
        DelegatingClientSession? session = null,
        Action<string>? progressReporter = null)
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                enableMeshBake,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: progressReporter),
            new ResoniteLiveSceneImportDependencies(
                session ?? new DelegatingClientSession(routedClient),
                diagnostics,
                terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator(),
                new ResoniteSceneSetupInterpreter(
                    new ResoniteSceneSlotLocator(),
                    new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                materialPlanning,
                new ResoniteCommonMaterialSetupPreparer(materialPlanning, progressReporter),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory(new ResoniteTextureImageLoader())));
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateImportedObjectUnitsAsync(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportedCityObject importedCityObject = ImportedDynamicMaterialUvNormalizer.Normalize(ToImportedCityObject(cityObject));
            string sourceFileRelativePath = importedCityObject.SourceFileRelativePath ?? importedCityObject.ObjectKey;
            yield return new ImportedObjectUnit(
                sourceFileRelativePath,
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

    public static IAsyncEnumerable<ImportedObjectUnit> CreateImportedObjectUnitsForTestsAsync(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        return CreateImportedObjectUnitsAsync(cityObjects, cancellationToken);
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
            ResoniteTerrainGridGeometry heightMap => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContractTransform(cityObject.Transform),
                new TerrainGridGeometry(
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
            ResoniteDynamicTerrainGeometry dynamicTerrain => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContractTransform(cityObject.Transform),
                new DynamicTerrainGeometry(
                    new TriangleMeshGeometry(ToContractMesh(dynamicTerrain.StaticMesh.Mesh)),
                    new TerrainGridGeometry(
                        dynamicTerrain.GridMesh.Width,
                        dynamicTerrain.GridMesh.Height,
                        ToContractFloat2(dynamicTerrain.GridMesh.Size),
                        dynamicTerrain.GridMesh.MinHeight,
                        dynamicTerrain.GridMesh.MaxHeight,
                        dynamicTerrain.GridMesh.HeightSamples,
                        dynamicTerrain.GridMesh.UvScale is null ? null : ToContractFloat2(dynamicTerrain.GridMesh.UvScale),
                        dynamicTerrain.GridMesh.UvOffset is null ? null : ToContractFloat2(dynamicTerrain.GridMesh.UvOffset))),
                cityObject.Materials.Select(ToContractMaterial).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    public static MaterialBinding ToContractMaterial(ResoniteMaterialBinding binding)
    {
        return new MaterialBinding(
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
            binding.BundledVariantIndex,
            binding.TerrainMeshCode,
            binding.CommonMaterial);
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
            mesh.Submeshes.Select(static submesh => new MeshSubmesh(submesh.Index, submesh.TriangleVertexIndices)).ToArray());

    private static TexturePayload ToContractTexturePayload(ResoniteTexturePayload payload)
        => new(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload.AsSpan().ToArray(),
            payload.Identity,
            (TexturePayloadFormat)payload.Format);

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

internal sealed class SceneSinkRecordingClient : PlateauResoniteLink.Targets.Resonite.Diagnostics.SceneSinkRecordingClient
{
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
