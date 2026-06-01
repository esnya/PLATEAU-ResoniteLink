using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

namespace PlateauResoniteLink.Tests.Targets;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLiveSceneImportTargetLifecycleTests
{
    private static BundledDefaultMaterialAssetStore CreateBundledDefaultMaterialAssetStore() => new();

    [Fact]
    public async Task ExecuteAsync_DelegatesNormalizedRequestsToInjectedSession()
    {
        using TemporaryDirectory resolvedDatasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));

        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Remote(new Uri("https://example.invalid/tokyo23ku/source-archive.zip")));
        ImportedSceneMetadata metadata = CreateMetadata(
            CreateRequest(resolvedDatasetDirectory.Path),
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                firstWorkDirectory.Path,
                normalizedRequest: normalizedRequest),
            EmptyImportedObjectUnits());
        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                secondWorkDirectory.Path,
                normalizedRequest: normalizedRequest),
            EmptyImportedObjectUnits());

        Assert.Equal(2, session.EnsureConnectedCallCount);
        Assert.Equal(
            [
                new LiveSendConnectionRequest(normalizedRequest.Dataset, normalizedRequest.MeshCode),
                new LiveSendConnectionRequest(normalizedRequest.Dataset, normalizedRequest.MeshCode),
            ],
            session.EnsureConnectedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesInjectedSessionFailure()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        DelegatingClientSession session = new(
            ensureConnectedAsync: static (_, _) => Task.FromException(new InvalidOperationException("connect failed")));
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));

        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => importTarget.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
                EmptyImportedObjectUnits()));
        Assert.Equal(1, session.EnsureConnectedCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotLaunchWorkersWhenRunSetupFails()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        RecordingWorkerLauncher workerLauncher = new();
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                diagnostics,
                new ResoniteLiveSendRunStarter(
                    new LiveSendRunPlanFactory(),
                    new ResoniteLiveSendConnectionInitializer(),
                    new ThrowingRunSetupPreparer(),
                    new LiveSendRunStateFactory(
                        new ResoniteBufferedCityObjectBakerFactory(
                            new NonDemSourceFileBakeEmitterFactory(new ResoniteTextureImageLoader())),
                        new LiveSendRunRuntimeComponentsFactory()),
                    workerLauncher)));

        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => importTarget.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
                EmptyImportedObjectUnits()));
        Assert.Equal("setup failed", exception.Message);
        Assert.Equal(1, session.EnsureConnectedCallCount);
        Assert.Equal(0, workerLauncher.LaunchCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsConcurrentRunsBeforeSetupCompletes()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        TaskCompletionSource enteredEnsureConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseEnsureConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(
            routedClient,
            async (_, cancellationToken) =>
            {
                enteredEnsureConnected.TrySetResult();
                await releaseEnsureConnected.Task.WaitAsync(cancellationToken);
            });
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        Task<SceneImportExecutionResult> firstRun = importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            EmptyImportedObjectUnits());

        await enteredEnsureConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => importTarget.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
                EmptyImportedObjectUnits()));

        Assert.Equal("A live scene import run is already active on this live scene import target instance.", exception.Message);
        Assert.Equal(1, session.EnsureConnectedCallCount);

        releaseEnsureConnected.TrySetResult();
        _ = await firstRun;
    }

    [Fact]
    public async Task ExecuteAsync_ClearsRunLocalStateBetweenSequentialRunsOnTheSameTarget()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            CreateImportedObjectUnits(
                CreateCityObject("first-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));
        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedObjectUnits(
                CreateCityObject("second-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Slot assetsRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(routedClient, "PLATEAU tokyo23ku/Assets");

        Assert.Equal(
            2,
            routedClient.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "plateau_tokyo23ku_bldg_53394525", StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(
            2,
            routedClient.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "plateau_tokyo23ku_bldg_53394525", StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, assetsRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(0, session.ResetClientsCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsCodebaseReachableCommonMaterialWhenSetupDoesNotResolveIt()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning, new MissingCommonMaterialSetupInterpreter())));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials = CommonMaterialCatalog.Create();
        SceneImportExecutionPlan plan = ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
            metadata,
            workDirectory.Path,
            commonMaterials: commonMaterials);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(CreateBundledFacadeCityObject("setup-common-missing"))));

        Assert.Contains("Setup did not create common material family", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsCommonMaterialWhenSetupDoesNotMarkIt()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning, new MissingCommonMaterialSetupInterpreter())));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        SceneImportExecutionPlan plan = ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
            metadata,
            workDirectory.Path,
            commonMaterials: CommonMaterialCatalog.Create());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(CreateVertexColorTriangleCityObject("runtime-common-material"))));

        Assert.Contains("Setup did not create common material family", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PreparesCommonMaterialsBeforeSendWorkers()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        List<string> progressMessages = [];
        ResoniteMaterialDepthOffset terrainAlignedDepthOffset = new(-10.0, -10.0);
        string expectedMaterialName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(
            new ResoniteMaterialBinding(
                BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: terrainAlignedDepthOffset,
                SubmeshIndices: [0],
                AssetScope: ResoniteMaterialAssetScope.Common));
        bool terrainAlignedGenericSlotExistedWhenSendWorkersStarted = false;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: false,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: message =>
                {
                    if (message.Contains("Starting routed send workers", StringComparison.Ordinal))
                    {
                        terrainAlignedGenericSlotExistedWhenSendWorkersStarted = routedClient.SlotsById.Values.Any(
                            slot => string.Equals(slot.Name?.Value, expectedMaterialName, StringComparison.Ordinal));
                    }

                    progressMessages.Add(message);
                }),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning, progressReporter: progressMessages.Add)));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        ResoniteConstructionCityObject cityObject = CreateMixedSharedMaterialAndPayloadCityObject(
            "runtime-shared-texture",
            terrainAlignedDepthOffset);
        SceneImportExecutionPlan plan = ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
            metadata,
            workDirectory.Path,
            commonMaterials: ResoniteLiveSceneImportTargetTestSupport.CreateReferencedCommonMaterials([cityObject], enableMeshBake: false));

        _ = await importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(cityObject));

        int commonPrepIndex = progressMessages.FindIndex(static message =>
            message.Contains("Prepared ", StringComparison.Ordinal)
            && message.Contains("common material assets during scene setup", StringComparison.Ordinal));
        int sendWorkersIndex = progressMessages.FindIndex(static message =>
            message.Contains("Starting routed send workers", StringComparison.Ordinal));
        Assert.True(commonPrepIndex >= 0, "Expected common material preparation progress before streaming.");
        Assert.True(sendWorkersIndex >= 0, "Expected send worker startup progress.");
        Assert.InRange(commonPrepIndex, 0, sendWorkersIndex - 1);
        Assert.True(
            terrainAlignedGenericSlotExistedWhenSendWorkersStarted,
            "Expected terrain-aligned generic shared material slot to exist before send workers start.");
    }

    [Fact]
    public async Task ExecuteAsync_PreparesTerrainAlignedVertexColorCommonMaterialDuringSetup()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        List<string> progressMessages = [];
        string expectedMaterialName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(
            new ResoniteMaterialBinding(
                BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.VertexColor,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: new ResoniteMaterialDepthOffset(-10.0, -10.0),
                SubmeshIndices: [0],
                AssetScope: ResoniteMaterialAssetScope.Common));
        bool materialSlotExistedWhenSendWorkersStarted = false;
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            session: session,
            progressReporter: message =>
            {
                if (message.Contains("Starting routed send workers", StringComparison.Ordinal))
                {
                    materialSlotExistedWhenSendWorkersStarted = routedClient.SlotsById.Values.Any(
                        slot => string.Equals(slot.Name?.Value, expectedMaterialName, StringComparison.Ordinal));
                }

                progressMessages.Add(message);
            });
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        ResoniteConstructionCityObject cityObject = CreateVertexColorTriangleCityObject(
            "terrain-aligned-vertex-common",
            new ResoniteMaterialDepthOffset(-10.0, -10.0));
        SceneImportExecutionPlan plan = ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
            metadata,
            workDirectory.Path,
            commonMaterials: ResoniteLiveSceneImportTargetTestSupport.CreateReferencedCommonMaterials([cityObject], enableMeshBake: true));

        _ = await importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(cityObject));

        Assert.Contains(
            routedClient.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, expectedMaterialName, StringComparison.Ordinal));
        int materialPrepIndex = progressMessages.FindIndex(static message =>
            message.Contains("Setup batch prepared", StringComparison.Ordinal)
            && message.Contains("textureless common materials", StringComparison.Ordinal));
        int sendWorkersIndex = progressMessages.FindIndex(static message =>
            message.Contains("Starting routed send workers", StringComparison.Ordinal));
        Assert.True(materialPrepIndex >= 0, "Expected terrain-aligned vertex common material preparation in the setup batch.");
        Assert.True(sendWorkersIndex >= 0, "Expected send worker startup progress.");
        Assert.InRange(materialPrepIndex, 0, sendWorkersIndex - 1);
        Assert.True(
            materialSlotExistedWhenSendWorkersStarted,
            "Expected terrain-aligned vertex common material slot to exist before send workers start.");
    }

    [Fact]
    public void ReferencedCommonMaterialsIncludesTerrainAlignedVertexColorWhenMeshBakeIsEnabled()
    {
        ResoniteConstructionCityObject cityObject = CreateVertexColorTriangleCityObject(
            "terrain-aligned-vertex-common-default",
            new ResoniteMaterialDepthOffset(-10.0, -10.0));

        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials =
            ResoniteLiveSceneImportTargetTestSupport.CreateReferencedCommonMaterials([cityObject], enableMeshBake: true);

        Assert.Contains(
            commonMaterials.EnumerateItems(),
            static member => member.Definition == CommonMaterialCatalog.Create().VertexColor.TerrainAlignedUv.Definition);
    }

    [Fact]
    public async Task ExecuteAsync_SetsUpTerrainOverlayAsSharedGenericAlbedoOnlyMaterialWhenAssigned()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel),
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoOnly);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                overlay.PrimarySource));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);
        ResoniteConstructionCityObject demObject = CreateDemCityObject(
            "dem-setup-generic",
            "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
            overlay);
        demObject = demObject with
        {
            Materials =
            [
                demObject.Materials[0] with
                {
                    CommonMaterial = CommonMaterialCatalog.Create().Generic.Uv,
                },
            ],
        };
        SceneImportExecutionResult executionResult = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                workDirectory.Path,
                commonMaterials: ResoniteLiveSceneImportTargetTestSupport.CreateReferencedCommonMaterials(
                    [demObject],
                    enableMeshBake: true)),
            CreateImportedObjectUnits(demObject));

        Assert.Equal(1, executionResult.ProcessedCityObjectCount);
        Assert.Contains(
            routedClient.AddedComponents,
            request => request.Data.ComponentType == "[FrooxEngine]FrooxEngine.PBS_Metallic"
                && routedClient.SlotPaths[request.ContainerSlotId].Replace('\\', '/') ==
                    "PLATEAU Shared Assets/Common Materials/generic/uv");
        Assert.Contains(
            routedClient.AddedComponents,
            static request => request.Data.ComponentType == "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock");
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesTerrainOverlayGenerationFailure()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel),
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoOnly);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => throw new HttpRequestException("offline"));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => importTarget.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
                CreateImportedObjectUnits(
                    CreateDemCityObject("dem-overlay-failure", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay))));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsDatasetLicenseComponentsCreateOnlyAcrossRepeatedRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            CreateImportedObjectUnits(
                CreateCityObject("first-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));
        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedObjectUnits(
                CreateCityObject("second-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Assert.Single(
            routedClient.SlotsById[datasetRoot.ID!].Components!,
            component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal));
        Assert.Empty(routedClient.UpdatedComponents);
        Assert.DoesNotContain(
            routedClient.Batches.SelectMany(static operations => operations),
            static operation => operation is UpdateComponent);
    }

    [Fact]
    public async Task ExecuteAsync_AddsGsiFallbackLicenseOnlyWhenGsiTileIsActuallyUsed()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel),
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoOnly);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel)));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        SceneImportExecutionResult executionResult = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-run", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, licenses.Length);
        string[] creditStrings = licenses
            .Select(static component => ((Field_string)component.Members["CreditString"]).Value)
            .ToArray();
        Assert.Contains(creditStrings, static creditString => creditString.Contains("GSI Maps Terms", StringComparison.Ordinal));
        Assert.Contains(creditStrings, static creditString => !creditString.Contains("GSI Maps Terms", StringComparison.Ordinal));
        ImportDataSourceUsage demSourceUsage = Assert.Single(executionResult.DataSourceUsages);
        Assert.Equal(ImportDataSourceCategory.DemTextureSource, demSourceUsage.Category);
        Assert.Equal(
            new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel).IdentityKey,
            demSourceUsage.Identity);
        Assert.Equal(1, demSourceUsage.UsedCount);
        Assert.Empty(routedClient.UpdatedComponents);
        Assert.DoesNotContain(
            routedClient.Batches.SelectMany(static operations => operations),
            static operation => operation is UpdateComponent);
    }

    [Fact]
    public async Task ExecuteAsync_TracksEveryDemSourceUsedInComposedOverlay()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureGeoReferencedRasterSource rasterSource = new(
            Path.Combine(datasetDirectory.Path, "dem-partial.tif"),
            new GeoReferencedRasterMetadata(
                CreateThirdMeshBounds(),
                "EPSG:4326",
                1.0,
                1.0));
        TerrainTextureTileSource gsiFallbackSource = new(
            LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
            LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: rasterSource,
            FallbackSource: gsiFallbackSource,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                gsiFallbackSource,
                [rasterSource, gsiFallbackSource]));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        SceneImportExecutionResult executionResult = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-mixed", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        ImportDataSourceUsage[] usages = executionResult.DataSourceUsages
            .OrderBy(static usage => usage.Identity, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, usages.Length);
        Assert.Contains(
            usages,
            static usage => usage.Category == ImportDataSourceCategory.DemTextureSource
                && usage.Identity == new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel).IdentityKey
                && usage.UsedCount == 1);
        Assert.Contains(
            usages,
            usage => usage.Category == ImportDataSourceCategory.DemTextureSource
                && usage.Identity == rasterSource.IdentityKey
                && usage.UsedCount == 1);

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, licenses.Length);
        Assert.Contains(
            licenses.Select(static component => ((Field_string)component.Members["CreditString"]).Value),
            static creditString => creditString.Contains("GSI Maps Terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAddGsiFallbackLicenseWhenPrimaryTerrainSourceIsUsed()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel),
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel)));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-primary", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(licenses);
        string creditString = ((Field_string)licenses[0].Members["CreditString"]).Value;
        Assert.DoesNotContain("GSI Maps Terms", creditString, StringComparison.Ordinal);
        Assert.Empty(routedClient.UpdatedComponents);
        Assert.DoesNotContain(
            routedClient.Batches.SelectMany(static operations => operations),
            static operation => operation is UpdateComponent);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAddGsiFallbackLicenseWhenExplicitRasterSourceIsUsed()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureGeoReferencedRasterSource rasterSource = new(
            Path.Combine(datasetDirectory.Path, "dem-ortho.tif"),
            new GeoReferencedRasterMetadata(
                CreateThirdMeshBounds(),
                "EPSG:4326",
                1.0,
                1.0));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: CreateThirdMeshBounds(),
            MaxTextureSize: 512,
            PrimarySource: rasterSource,
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                CreateRawTextureSource(
                    2,
                    2,
                    ResoniteTextureColorProfiles.Srgb,
                    new byte[16]),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                rasterSource));
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-raster", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(licenses);
        string creditString = ((Field_string)licenses[0].Members["CreditString"]).Value;
        Assert.DoesNotContain("GSI Maps Terms", creditString, StringComparison.Ordinal);
        Assert.Empty(routedClient.UpdatedComponents);
        Assert.DoesNotContain(
            routedClient.Batches.SelectMany(static operations => operations),
            static operation => operation is UpdateComponent);
    }

    [Fact]
    public async Task ExecuteAsync_SetupHandlesDatasetAttributionWithoutUsingUpdates()
    {
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(routedClient, session: session);
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            cityGmlLocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages"),
            packageNames: ["dem"]);
        ImportedSceneSourceSnapshot readResult = await new LocalCityGmlDocumentReader(
            new DefaultPlateauDatasetContentSourceFactory(new RemoteArchiveDistributionPolicy(), new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector())
            .ReadAsync(
            request,
            cancellationToken: default);
        ImportedSceneMetadata metadata = new DefaultImportedSceneSourceComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(CommonMaterialCatalog.Create())),
                new DefaultDemTextureSourcePolicy(
                    new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                        new DefaultPlateauDatasetContentSourceFactory(
                            new RemoteArchiveDistributionPolicy(),
                            new ArchiveFileLayoutPolicy()))))
            .Compose(
                request,
                readResult,
                new PassthroughImportedObjectUnitOptimizer())
            .Metadata;

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            EmptyImportedObjectUnits());

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.True(licenses.Length >= 1);
        Assert.Contains(licenses, static component => ((Field_string)component.Members["CreditString"]).Value.Contains("tokyo23ku", StringComparison.Ordinal));
        Assert.Empty(routedClient.UpdatedComponents);
    }

    [Fact]
    public async Task ExecuteAsync_ResetsSessionAfterFailedRunBeforeRetry()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => importTarget.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
                ThrowingImportedObjectUnits()));

        _ = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedObjectUnits(
                CreateCityObject("retry-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));

        Assert.Equal(2, session.EnsureConnectedCallCount);
        Assert.Equal(1, session.ResetClientsCallCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInjectedSession()
    {
        DelegatingClientSession session = new();
        using SceneSinkRecordingClient routedClient = new();
        ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(routedClient, session: session);

        try
        {
            await importTarget.DisposeAsync();
            Assert.Equal(1, session.DisposeClientsCallCount);
        }
        finally
        {
            await importTarget.DisposeAsync();
        }
    }

    private static PlateauImportRequest CreateRequest(string datasetRoot)
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(datasetRoot),

            PackageNames: ["bldg"]);
    }

    private static ImportedSceneMetadata CreateMetadata(
        PlateauImportRequest request,
        IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.CityGmlLocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: sourceFiles ?? []);
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> EmptyImportedObjectUnits()
    {
        yield break;
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateImportedObjectUnits(
        params ResoniteConstructionCityObject[] cityObjects)
    {
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            ImportedCityObject importedCityObject = ImportedDynamicMaterialUvNormalizer.Normalize(
                ResoniteLiveSceneImportTargetTestSupport.ToImportedCityObject(cityObject));
            string sourceFileRelativePath = importedCityObject.SourceFileRelativePath ?? importedCityObject.ObjectKey;
            yield return new ImportedObjectUnit(
                sourceFileRelativePath,
                importedCityObject.PackageName,
                importedCityObject.LodLevel,
                [importedCityObject],
                importedCityObject.ActualMeshCode);
        }
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> ThrowingImportedObjectUnits()
    {
        await Task.Yield();
        throw new InvalidOperationException("city object stream failed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static ResoniteFloat2 CreateTilesPerMeter(string texturePath)
    {
        ScalarPair value = BundledDefaultMaterialProfiles.GetTilesPerMeterValue(texturePath);
        return new ResoniteFloat2(value.X, value.Y);
    }

    private static GeographicRectangle CreateThirdMeshBounds()
    {
        if (!PlateauMeshCode.TryGetBounds(
                "53394525",
                out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds))
        {
            throw new InvalidOperationException("Test mesh-code must be a valid third-level mesh-code.");
        }

        return new GeographicRectangle(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    private static ResoniteConstructionCityObject CreateCityObject(string objectKey, string sourceFileRelativePath)
    {
        return new ResoniteConstructionCityObject(
            objectKey,
            $"CityObject {objectKey}",
            "bldg",
            "53394525",
            1,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            [
                new ResoniteMaterialBinding(new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Dataset,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0]),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateDemCityObject(
        string objectKey,
        string sourceFileRelativePath,
        TerrainTextureOverlay overlay)
    {
        return new ResoniteConstructionCityObject(
            objectKey,
            $"DEM {objectKey}",
            "dem",
            "53394525",
            0,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            [
                new ResoniteMaterialBinding(new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Dataset,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0],
                    TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay)),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateBundledFacadeCityObject(string objectKey)
    {
        string family = BundledDefaultMaterialFamilies.FacadeHighriseGlass;
        int variantIndex = 0;
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        ResoniteFloat2 textureScale = CreateTilesPerMeter(texturePath);
        ResoniteFloat2 textureOffset = new(0.0, 0.5 / 6.0);
        return new ResoniteConstructionCityObject(
            objectKey,
            $"CityObject {objectKey}",
            "bldg",
            "53394525",
            0,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            [
                new ResoniteMaterialBinding(new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0],
                    TextureScale: textureScale,
                    Family: family,
                    TextureOffset: textureOffset,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: variantIndex,
                    CommonMaterial: CommonMaterialCatalog.Create().FacadeHighriseGlass.Facade001),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private static ResoniteConstructionCityObject CreateVertexColorTriangleCityObject(
        string objectIdentity,
        ResoniteMaterialDepthOffset? depthOffset = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: depthOffset,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    CommonMaterial: depthOffset is null
                        ? CommonMaterialCatalog.Create().VertexColor.Uv
                        : CommonMaterialCatalog.Create().VertexColor.TerrainAlignedUv),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private static ResoniteConstructionCityObject CreateMixedSharedMaterialAndPayloadCityObject(
        string objectIdentity,
        ResoniteMaterialDepthOffset? payloadDepthOffset = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoMaterialMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: payloadDepthOffset,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    CommonMaterial: payloadDepthOffset is null
                        ? CommonMaterialCatalog.Create().VertexColor.Uv
                        : CommonMaterialCatalog.Create().VertexColor.TerrainAlignedUv),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(
                        255,
                        0,
                        0,
                        "textures/runtime-shared-texture.png"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private static ResoniteImportedMesh CreateTwoMaterialMesh()
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
                new ResoniteMeshSubmesh(1, [3, 4, 5]),
            ]);
    }

    private sealed class ThrowingRunSetupPreparer : IResoniteLiveSendRunSetupPreparer
    {
        public Task<LiveSendPreparedRunSetup> PrepareAsync(
            LiveSendRunPlan runPlan,
            LiveSendRunStartRequest request,
            LiveSendRunStartContext context,
            CancellationToken cancellationToken)
        {
            _ = runPlan;
            _ = request;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<LiveSendPreparedRunSetup>(new InvalidOperationException("setup failed"));
        }
    }

    private sealed class RecordingWorkerLauncher : IResoniteLiveSendWorkerLauncher
    {
        public int LaunchCallCount { get; private set; }

        public void Launch(
            LiveSendWorkerLaunchRequest request,
            LiveSendRunStartContext context)
        {
            _ = request;
            _ = context;
            LaunchCallCount++;
        }
    }

    private sealed class MissingCommonMaterialSetupInterpreter : IResoniteSceneSetupInterpreter
    {
        public async Task<ResoniteSceneSetupState> SetupAsync(
            IResoniteLinkClient setupClient,
            ResoniteSceneSetupInfo setupInfo,
            CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
            CancellationToken cancellationToken)
        {
            _ = setupInfo;
            _ = commonMaterials;

            string datasetRootId = (await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = "Root" },
                        Name = new Field_string { Value = "PLATEAU tokyo23ku" },
                    },
                },
                cancellationToken)).Slot.Value;
            string assetsRootId = (await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = datasetRootId },
                        Name = new Field_string { Value = "Assets" },
                    },
                },
                cancellationToken)).Slot.Value;
            string commonRootId = (await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = "Root" },
                        Name = new Field_string { Value = "PLATEAU Shared Assets" },
                    },
                },
                cancellationToken)).Slot.Value;
            string commonMaterialsRootId = (await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = commonRootId },
                        Name = new Field_string { Value = "Common Materials" },
                    },
                },
                cancellationToken)).Slot.Value;

            return new ResoniteSceneSetupState(
                new CreatedSlot(new ResoniteSlotLocator(datasetRootId), "PLATEAU tokyo23ku"),
                new CreatedSlot(new ResoniteSlotLocator(assetsRootId), "Assets"),
                new CreatedSlot(new ResoniteSlotLocator(commonMaterialsRootId), "Common Materials"),
                DatasetRootExisted: false,
                new SceneAnchor(new ResoniteSlotLocator(datasetRootId), "53394525", new ResoniteFloat3(0.0, 0.0, 0.0), null),
                DatasetRootSnapshot: null,
                CommonMaterialAssets: CommonMaterialCatalog.Create().Map(static member => new ResoniteCommonMaterialAsset(member, SceneImportContractMapper.ToInternal(member.CreateBinding([0])), default)),
                CommonMaterialFamilies: []);
        }
    }
}
