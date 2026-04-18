using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Targets;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLiveSceneImportTargetLifecycleTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesNormalizedRequestsToInjectedSession()
    {
        using TemporaryDirectory rawDatasetDirectory = new();
        using TemporaryDirectory resolvedDatasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));

        PlateauImportRequest normalizedRequest = CreateRequest(rawDatasetDirectory.Path);
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
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));

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
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));
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
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));
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
    }

    [Fact]
    public async Task ExecuteAsync_KeepsDatasetLicenseComponentsCreateOnlyAcrossRepeatedRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));
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
    public async Task ExecuteAsync_BootstrapAddsOptionalGsiFallbackLicenseWithoutUsingUpdates()
    {
        using TemporaryDirectory workDirectory = new();
        using SceneBuilderRecordingClient routedClient = new();
        DelegatingClientSession session = new(routedClient);
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages"),
            PackageNames: ["dem"],
            ServerUri: null);
        LocalCityGmlDocumentSet documentSet = await LocalCityGmlBootstrapPipeline.ReadAsync(request);
        ResoniteConstructionMetadata metadata = new LocalCityGmlConstructionComposer(new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()))
            .Compose(request, documentSet)
            .Metadata;

        _ = await builder.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path),
            EmptyImportedCityObjects());

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client: routedClient, name: "PLATEAU tokyo23ku");
        Component[] licenses = datasetRoot.Components!
            .Where(static component => string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, licenses.Length);
        Assert.Contains(licenses, static component => ((Field_string)component.Members["CreditString"]).Value.Contains("tokyo23ku", StringComparison.Ordinal));
        Assert.Contains(licenses, static component => ((Field_string)component.Members["CreditString"]).Value.Contains("GSI Maps Terms", StringComparison.Ordinal));
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
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));
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
        ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLiveSceneImportDependencies(
                session,
                new TerrainTextureAssetGenerator()));

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
}
