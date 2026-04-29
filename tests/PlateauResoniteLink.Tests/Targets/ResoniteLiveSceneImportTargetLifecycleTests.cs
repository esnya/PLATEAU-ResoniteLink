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
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneSetupInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));

        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Remote(new Uri("https://example.invalid/tokyo23ku/source-archive.zip")));
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
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneSetupInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));

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
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneSetupInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
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
    public async Task ExecuteAsync_ClearsRunLocalStateBetweenSequentialRunsOnTheSameBuilder()
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
    public async Task ExecuteAsync_FailsWhenSetupKnownCommonMaterialWasNotResolvedDuringSetup()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
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
            new ResoniteLiveSceneImportDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                new MissingCommonMaterialSetupInterpreter(),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        IReadOnlyList<MaterialBinding> commonMaterials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);
        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            request,
            request,
            metadata,
            request.LocalSourcePath!,
            workDirectory.Path,
            commonMaterials);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => importTarget.ExecuteAsync(
                plan,
                CreateImportedObjectUnits(CreateBundledFacadeCityObject("setup-common-missing"))));

        Assert.Contains(
            "Setup did not resolve shared/common material",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("family=Facade", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("projection=Uv", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PreparesSharedCommonMaterialDuringRuntimeWhenSetupDoesNotMarkIt()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
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
            new ResoniteLiveSceneImportDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                new MissingCommonMaterialSetupInterpreter(),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            request,
            request,
            metadata,
            request.LocalSourcePath!,
            workDirectory.Path,
            commonMaterials: []);

        _ = await importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(CreateVertexColorTriangleCityObject("runtime-common-material")));

        Assert.Contains(
            routedClient.SlotPaths.Values,
            static path => path.EndsWith("/vertex-color/shared_uv_vertex-color", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_StartsRuntimeSharedMaterialPreparationBeforePreparedTextureImport()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: false,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                new MissingCommonMaterialSetupInterpreter(),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ImportedSceneMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            request,
            request,
            metadata,
            request.LocalSourcePath!,
            workDirectory.Path,
            commonMaterials: []);

        _ = await importTarget.ExecuteAsync(
            plan,
            CreateImportedObjectUnits(CreateMixedSharedMaterialAndPayloadCityObject("runtime-shared-texture")));

        int firstSharedMaterialReadIndex = routedClient.OperationNames.FindIndex(static operation =>
            operation.StartsWith("GetSlot:", StringComparison.Ordinal)
            && operation.Contains("Common Materials", StringComparison.Ordinal));
        int firstTextureImportIndex = routedClient.OperationNames.FindIndex(static operation =>
            string.Equals(operation, "ImportTexture", StringComparison.Ordinal));
        Assert.True(firstSharedMaterialReadIndex >= 0, "Expected runtime shared material preparation to read the shared Common Materials slot.");
        Assert.True(firstTextureImportIndex >= 0, "Expected the textured material to import its prepared texture payload.");
        Assert.InRange(firstSharedMaterialReadIndex, 0, firstTextureImportIndex - 1);
    }

    [Fact]
    public async Task ExecuteAsync_SetsUpTerrainOverlaySharedCommonMaterialBeforeRuntimeEmission()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneSinkRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
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
                new ResoniteRawTextureImport(
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
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);
        MaterialBinding setupTerrainOverlayMaterial = ResoniteLiveSceneImportTargetTestSupport.ToContractMaterial(
            new ResoniteMaterialBinding(
                "dem-overlay-setup",
                new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                ResoniteMaterialType.Standard,
                null,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                null,
                [0],
                AssetScope: ResoniteMaterialAssetScope.Common,
                TerrainOverlay: overlay));

        SceneImportExecutionResult executionResult = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                workDirectory.Path,
                commonMaterials: [setupTerrainOverlayMaterial]),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-setup-generic", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        Assert.Equal(1, executionResult.ProcessedCityObjectCount);
        Slot commonRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            routedClient,
            "PLATEAU Shared Assets/Common Materials");
        Assert.Contains(
            routedClient.SlotPaths.Values,
            path => string.Equals(
                path,
                $"{routedClient.SlotPaths[commonRoot.ID!]}/generic/shared_uv_generic",
                StringComparison.Ordinal));
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
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
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
            request.LocalSourcePath!,
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
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
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
                new ResoniteRawTextureImport(
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
            request.LocalSourcePath!,
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
        ImportDataSourceUsage demSourceUsage = Assert.Single(executionResult.DataSourceUsages ?? []);
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
                new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                "EPSG:4326",
                1.0,
                1.0));
        TerrainTextureTileSource gsiFallbackSource = new(
            LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
            LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512,
            PrimarySource: rasterSource,
            FallbackSource: gsiFallbackSource,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(
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
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);

        SceneImportExecutionResult executionResult = await importTarget.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedObjectUnits(
                CreateDemCityObject("dem-mixed", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

        ImportDataSourceUsage[] usages = executionResult.DataSourceUsages?
            .OrderBy(static usage => usage.Identity, StringComparer.Ordinal)
            .ToArray()
            ?? [];
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
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
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
                new ResoniteRawTextureImport(
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
            request.LocalSourcePath!,
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
                new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                "EPSG:4326",
                1.0,
                1.0));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512,
            PrimarySource: rasterSource,
            FallbackSource: new TerrainTextureTileSource(
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        RecordingTerrainTextureAssetGenerator terrainTextureGenerator = new(
            _ => new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(
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
            request.LocalSourcePath!,
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
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages"),
            PackageNames: ["dem"],
            ServerUri: null);
        ImportedSceneSourceSnapshot readResult = await new LocalCityGmlDocumentReader(
            new DefaultPlateauDatasetContentSourceFactory(new RemoteArchiveDistributionPolicy(), new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector())
            .ReadAsync(
            request,
            cancellationToken: default);
        ImportedSceneMetadata metadata = new DefaultImportedSceneSourceComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
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
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: datasetRoot,
            ServerUri: null,
            PackageNames: ["bldg"]);
    }

    private static ImportedSceneMetadata CreateMetadata(
        PlateauImportRequest request,
        IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
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

    private static ResoniteConstructionCityObject CreateCityObject(string objectKey, string sourceFileRelativePath)
    {
        return new ResoniteConstructionCityObject(
            objectKey,
            $"CityObject {objectKey}",
            "bldg",
            "53394525",
            1,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("material-1"),
            [
                new ResoniteMaterialBinding(
                    "material-1",
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0),
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
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("dem-material"),
            [
                new ResoniteMaterialBinding(
                    "dem-material",
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Dataset,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0],
                    TerrainOverlay: overlay),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateBundledFacadeCityObject(string objectKey)
    {
        string family = BundledDefaultMaterialFamilies.Facade;
        int variantIndex = 0;
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        ResoniteFloat2 textureScale = CreateTilesPerMeter(texturePath);
        ResoniteFloat2 textureOffset = new(0.0, 0.5 / 6.0);
        string materialKey = $"common|facade|variant:0|Uv|scale:{textureScale.X:0.######}x{textureScale.Y:0.######}|offset:{textureOffset.X:0.######}x{textureOffset.Y:0.######}";
        return new ResoniteConstructionCityObject(
            objectKey,
            $"CityObject {objectKey}",
            "bldg",
            "53394525",
            0,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(materialKey),
            [
                new ResoniteMaterialBinding(
                    materialKey,
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0),
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
                    BundledVariantIndex: variantIndex),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private static ResoniteConstructionCityObject CreateVertexColorTriangleCityObject(string objectIdentity)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("vertex-color-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "vertex-color-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private static ResoniteConstructionCityObject CreateMixedSharedMaterialAndPayloadCityObject(string objectIdentity)
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
                    MaterialKey: "first-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
                new ResoniteMaterialBinding(
                    MaterialKey: "second-material",
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
                    SubmeshIndices: [1]),
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
                new ResoniteMeshSubmesh(0, "first-material", [0, 1, 2]),
                new ResoniteMeshSubmesh(1, "second-material", [3, 4, 5]),
            ]);
    }

    private sealed class MissingCommonMaterialSetupInterpreter : IResoniteSceneSetupInterpreter
    {
        public async Task<ResoniteSceneSetupState> SetupAsync(
            IResoniteLinkClient setupClient,
            ResoniteSceneSetupInfo setupInfo,
            IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
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
                CommonMaterialAssetsByKey: new Dictionary<string, CreatedMaterialAsset>(StringComparer.Ordinal),
                CommonMaterialFamilies: []);
        }
    }
}
