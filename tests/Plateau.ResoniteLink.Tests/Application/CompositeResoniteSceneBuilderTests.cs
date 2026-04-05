using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class CompositeResoniteSceneBuilderTests
{
    [Fact]
    public async Task ProcessCityObjectAsyncRunsIndependentBuildersConcurrently()
    {
        TaskCompletionSource secondBuilderStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseBuilders = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using BlockingSceneBuilder firstBuilder = new(
            onProcessStarted: null,
            waitForStartSignal: secondBuilderStarted.Task,
            releaseSignal: releaseBuilders.Task);
        await using BlockingSceneBuilder secondBuilder = new(
            onProcessStarted: () => secondBuilderStarted.TrySetResult(),
            waitForStartSignal: Task.CompletedTask,
            releaseSignal: releaseBuilders.Task);
        await using CompositeResoniteSceneBuilder composite = new([firstBuilder, secondBuilder]);

        ResoniteConstructionMetadata metadata = CreateMetadata();
        ResoniteConstructionCityObject cityObject = CreateCityObject();

        await composite.BeginAsync(metadata, "artifacts/resonite");

        Task processTask = composite.ProcessCityObjectAsync(cityObject);
        await secondBuilder.ProcessStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(processTask.IsCompleted);

        releaseBuilders.TrySetResult();
        await processTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static ResoniteConstructionMetadata CreateMetadata()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            InputPath: "/tmp/dataset",
            ServerUri: null);
        PlateauSourceDataset sourceDataset = new(
            PackageNames: ["bldg"],
            SourceFiles: ["udx/bldg/53394525/example.gml"],
            TerrainTextureOverlays: []);
        ResoniteAttribution attribution = new(
            new ResoniteLicenseComponentMetadata(
                RequireCredit: true,
                CreditText: "Example Credit",
                LicenseName: "Test License",
                LicenseUrl: "https://example.test/license"),
            []);

        return new ResoniteConstructionMetadata(
            "1.0",
            "Test World",
            request,
            sourceDataset,
            attribution,
            new ResoniteLocalOrigin(0.0, 0.0, 0.0));
    }

    private static ResoniteConstructionCityObject CreateCityObject()
    {
        return new ResoniteConstructionCityObject(
            SlotKey: "test-cityobject",
            DisplayName: "Test CityObject",
            PackageName: "bldg",
            Transform: new ResoniteTransform(
                new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                Vertices: [],
                Submeshes: []),
            Materials: []);
    }

    private sealed class BlockingSceneBuilder(
        Action? onProcessStarted,
        Task waitForStartSignal,
        Task releaseSignal) : IResoniteSceneBuilder
    {
        public TaskCompletionSource ProcessStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task BeginAsync(
            ResoniteConstructionMetadata metadata,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task ProcessCityObjectAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            onProcessStarted?.Invoke();
            ProcessStarted.TrySetResult();
            await waitForStartSignal.WaitAsync(cancellationToken);
            await releaseSignal.WaitAsync(cancellationToken);
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
