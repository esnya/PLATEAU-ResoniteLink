using System.Diagnostics.CodeAnalysis;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

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
            Source: PlateauImportSource.Remote(new Uri("https://example.invalid/tokyo23ku/source-archive.zip")));
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
        Assert.Equal([normalizedRequest, normalizedRequest], session.EnsureConnectedRequests);
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

        _ = await builder.ExecuteAsync(
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
        Assert.Empty(routedClient.UpdatedComponents);
        Assert.DoesNotContain(
            routedClient.Batches.SelectMany(static operations => operations),
            static operation => operation is UpdateComponent);
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
        LocalCityGmlDocumentReadResult readResult = await LocalCityGmlBootstrapPipeline.ReadAsync(
            request,
            new DefaultPlateauDatasetContentSourceFactory(new RemoteArchiveDistributionPolicy(), new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());
        ImportedSceneMetadata metadata = new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
                new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver()))
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
}
