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
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLiveSceneImportTargetLifecycleTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesNormalizedRequestsToInjectedSession()
    {
        using TemporaryDirectory resolvedDatasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));

        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Remote(new Uri("https://example.invalid/tokyo23ku/source-archive.zip")));
        ResoniteConstructionMetadata metadata = CreateMetadata(
            CreateRequest(resolvedDatasetDirectory.Path),
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                firstWorkDirectory.Path,
                normalizedRequest: normalizedRequest),
            EmptyImportedCityObjects());
        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                secondWorkDirectory.Path,
                normalizedRequest: normalizedRequest),
            EmptyImportedCityObjects());

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
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));

        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
                EmptyImportedCityObjects()));
        Assert.Equal(1, session.EnsureConnectedCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsConcurrentRunsBeforeBootstrapCompletes()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        TaskCompletionSource enteredEnsureConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseEnsureConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(
            routedClient,
            async (_, cancellationToken) =>
            {
                enteredEnsureConnected.TrySetResult();
                await releaseEnsureConnected.Task.WaitAsync(cancellationToken);
            });
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        Task<SceneImportExecutionResult> firstRun = builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            EmptyImportedCityObjects());

        await enteredEnsureConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
                EmptyImportedCityObjects()));

        Assert.Equal("A live scene build run is already active on this live scene import target instance.", exception.Message);
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
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            CreateImportedCityObjects(
                CreateCityObject("first-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));
        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedCityObjects(
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
    public async Task ExecuteAsync_FailsWhenBootstrapKnownCommonMaterialWasNotResolvedDuringSetup()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                new MissingCommonMaterialBootstrapInterpreter(),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);
        IReadOnlyList<MaterialBinding> commonMaterials = CommonMaterialCatalog.CreateForPackages(["bldg"]);
        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            request,
            request,
            SceneImportContractMapper.ToContract(metadata),
            request.LocalSourcePath!,
            workDirectory.Path,
            commonMaterials);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.ExecuteAsync(
                plan,
                CreateImportedCityObjects(CreateBundledFacadeCityObject("bootstrap-common-missing"))));

        Assert.Contains(
            "Bootstrap did not resolve shared/common material",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenSharedCommonMaterialIsNotMarkedForBootstrapSetup()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                new MissingCommonMaterialBootstrapInterpreter(),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            request,
            request,
            SceneImportContractMapper.ToContract(metadata),
            request.LocalSourcePath!,
            workDirectory.Path,
            commonMaterials: []);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.ExecuteAsync(
                plan,
                CreateImportedCityObjects(CreateBundledFacadeCityObject("bootstrap-common-missing"))));

        Assert.Contains(
            "Bootstrap did not resolve shared/common material",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            routedClient.AddedSlots,
            static slot => string.Equals(
                slot.Data.Name?.Value,
                BundledDefaultMaterialFamilies.Facade,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BootstrapsTerrainOverlaySharedCommonMaterialBeforeRuntimeEmission()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
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
                    new byte[16],
                    "terrain-overlay/dem/bootstrap-generic"),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                overlay.PrimarySource));
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);
        MaterialBinding bootstrapTerrainOverlayMaterial = SceneImportContractMapper.ToContract(
            new ResoniteMaterialBinding(
                "dem-overlay-bootstrap",
                new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                ResoniteMaterialType.Standard,
                null,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                null,
                [0],
                AssetScope: ResoniteMaterialAssetScope.Common,
                TerrainOverlay: overlay));

        SceneImportExecutionResult executionResult = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
                metadata,
                workDirectory.Path,
                commonMaterials: [bootstrapTerrainOverlayMaterial]),
            CreateImportedCityObjects(
                CreateDemCityObject("dem-bootstrap-generic", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay)));

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
        using SceneBuilderRecordingClient routedClient = new();
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
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => builder.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
                CreateImportedCityObjects(
                    CreateDemCityObject("dem-overlay-failure", "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml", overlay))));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsDatasetLicenseComponentsCreateOnlyAcrossRepeatedRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
            CreateImportedCityObjects(
                CreateCityObject("first-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));
        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedCityObjects(
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
        using SceneBuilderRecordingClient routedClient = new();
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
                    new byte[16],
                    "terrain-overlay/dem/gsi-used"),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel)));
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);

        SceneImportExecutionResult executionResult = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedCityObjects(
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
        using SceneBuilderRecordingClient routedClient = new();
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
                    new byte[16],
                    "terrain-overlay/dem/mixed-used"),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                gsiFallbackSource,
                [rasterSource, gsiFallbackSource]));
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);

        SceneImportExecutionResult executionResult = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedCityObjects(
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
        using SceneBuilderRecordingClient routedClient = new();
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
                    new byte[16],
                    "terrain-overlay/dem/ortho-used"),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel)));
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedCityObjects(
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
        using SceneBuilderRecordingClient routedClient = new();
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
                    new byte[16],
                    "terrain-overlay/dem/raster-used"),
                new ResoniteFloat2(1.0, 1.0),
                new ResoniteFloat2(0.0, 0.0),
                rasterSource));
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(
            routedClient,
            terrainTextureGenerator,
            session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            request.Dataset,
            request.MeshCode,
            request.LocalSourcePath!,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            terrainTextureOverlays: [overlay]);

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            CreateImportedCityObjects(
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
    public async Task ExecuteAsync_BootstrapHandlesAdditionalDatasetAttributionWithoutUsingUpdates()
    {
        using TemporaryDirectory workDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(routedClient, session: session);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages"),
            PackageNames: ["dem"],
            ServerUri: null);
        LocalCityGmlDocumentReadResult readResult = await new LocalCityGmlDocumentReader(
            new DefaultPlateauDatasetContentSourceFactory(new RemoteArchiveDistributionPolicy(), new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector())
            .ReadAsync(
            request,
            cancellationToken: default);
        ImportedSceneMetadata metadata = new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
                new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver()),
                new LocalCityGmlDemTextureSourcePolicy(
                    new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                        new DefaultPlateauDatasetContentSourceFactory(
                            new RemoteArchiveDistributionPolicy(),
                            new ArchiveFileLayoutPolicy()))))
            .Compose(request, readResult)
            .Metadata;

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            EmptyImportedCityObjects());

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
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(routedClient, session: session);
        PlateauImportRequest request = CreateRequest(datasetDirectory.Path);
        ResoniteConstructionMetadata metadata = CreateMetadata(
            request,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.ExecuteAsync(
                ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, firstWorkDirectory.Path),
                ThrowingImportedCityObjects()));

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, secondWorkDirectory.Path),
            CreateImportedCityObjects(
                CreateCityObject("retry-run", "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")));

        Assert.Equal(2, session.EnsureConnectedCallCount);
        Assert.Equal(1, session.ResetClientsCallCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInjectedSession()
    {
        DelegatingClientSession session = new();
        using SceneBuilderRecordingClient routedClient = new();
        ResoniteLiveSceneImportTarget builder = ResoniteLiveSceneImportTargetTestSupport.CreateBuilder(routedClient, session: session);

        try
        {
            await builder.DisposeAsync();
            Assert.Equal(1, session.DisposeClientsCallCount);
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }

    private static PlateauImportRequest CreateRequest(string datasetRoot)
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: datasetRoot,
            ServerUri: null);
    }

    private static ResoniteConstructionMetadata CreateMetadata(
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

    private static async IAsyncEnumerable<ImportedCityObject> EmptyImportedCityObjects()
    {
        yield break;
    }

    private static async IAsyncEnumerable<ImportedCityObject> CreateImportedCityObjects(
        params ResoniteConstructionCityObject[] cityObjects)
    {
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            yield return SceneImportContractMapper.ToContract(cityObject);
        }
    }

    private static async IAsyncEnumerable<ImportedCityObject> ThrowingImportedCityObjects()
    {
        await Task.Yield();
        throw new InvalidOperationException("city object stream failed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
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
            SourceObjectKey: objectKey,
            SourceUnitKey: objectKey,
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
            SourceObjectKey: objectKey,
            SourceUnitKey: objectKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateBundledFacadeCityObject(string objectKey)
    {
        string family = BundledDefaultMaterialFamilies.Facade;
        int variantIndex = 0;
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        return new ResoniteConstructionCityObject(
            objectKey,
            $"CityObject {objectKey}",
            "bldg",
            "53394525",
            0,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("common|facade|variant:0|Uv"),
            [
                new ResoniteMaterialBinding(
                    "common|facade|variant:0|Uv",
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0],
                    TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(texturePath),
                    Family: family,
                    TextureOffset: null,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: variantIndex),
            ],
            CollisionEnabled: true,
            SourceObjectKey: objectKey,
            SourceUnitKey: objectKey,
            SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml");
    }

    private sealed class MissingCommonMaterialBootstrapInterpreter : IResoniteSceneBootstrapInterpreter
    {
        public async Task<ResoniteSceneBootstrapState> BootstrapAsync(
            IResoniteLinkClient setupClient,
            SceneBootstrapInfo setupInfo,
            IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
            CancellationToken cancellationToken)
        {
            _ = setupInfo;
            _ = commonMaterials;

            string datasetRootId = await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = "Root" },
                        Name = new Field_string { Value = "PLATEAU tokyo23ku" },
                    },
                },
                cancellationToken);
            string assetsRootId = await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = datasetRootId },
                        Name = new Field_string { Value = "Assets" },
                    },
                },
                cancellationToken);
            string commonRootId = await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = "Root" },
                        Name = new Field_string { Value = "PLATEAU Shared Assets" },
                    },
                },
                cancellationToken);
            string commonMaterialsRootId = await setupClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Parent = new Reference { TargetID = commonRootId },
                        Name = new Field_string { Value = "Common Materials" },
                    },
                },
                cancellationToken);

            return new ResoniteSceneBootstrapState(
                new CreatedSlot(datasetRootId, "PLATEAU tokyo23ku"),
                new CreatedSlot(assetsRootId, "Assets"),
                new CreatedSlot(commonMaterialsRootId, "Common Materials"),
                DatasetRootExisted: false,
                new SceneAnchor(datasetRootId, "53394525", new ResoniteFloat3(0.0, 0.0, 0.0), null),
                DatasetRootSnapshot: null,
                CommonMaterialAssetsByKey: new Dictionary<string, CreatedMaterialAsset>(StringComparer.Ordinal),
                CommonMaterialFamilies: []);
        }
    }
}
