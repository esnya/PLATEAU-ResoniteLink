using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;
using Plateau.ResoniteLink.Profiles.PlateauCityGml;

namespace Plateau.ResoniteLink.Tests.Application;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class PlateauCityGmlConstructionSourcesTests
{
    [Fact]
    public async Task CreateAsync_DelegatesToInjectedFactoryOnce()
    {
        StubConstructionSource expected = new();
        RecordingConstructionSourceFactory factory = new(expected);
        PlateauCityGmlConstructionSources.FactoryProvider = () => factory;

        try
        {
            PlateauImportRequest request = CreateRequest();
            IResoniteConstructionSource actual = await PlateauCityGmlConstructionSources.CreateAsync(request);

            Assert.Same(expected, actual);
            Assert.Equal(1, factory.CreateAsyncCallCount);
            Assert.Same(request, factory.LastRequest);
        }
        finally
        {
            PlateauCityGmlConstructionSources.FactoryProvider = PlateauCityGmlComposition.CreateConstructionSourceFactory;
        }
    }

    [Fact]
    public void Create_PropagatesFactoryExceptionWithoutWrapping()
    {
        InvalidOperationException expected = new("boom");
        PlateauCityGmlConstructionSources.FactoryProvider = () => new ThrowingConstructionSourceFactory(expected);

        try
        {
            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => PlateauCityGmlConstructionSources.Create(CreateRequest()));
            Assert.Same(expected, actual);
        }
        finally
        {
            PlateauCityGmlConstructionSources.FactoryProvider = PlateauCityGmlComposition.CreateConstructionSourceFactory;
        }
    }

    [Fact]
    public void Create_PropagatesCancellationWithoutWrapping()
    {
        PlateauCityGmlConstructionSources.FactoryProvider = () => new CancellingConstructionSourceFactory();

        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => PlateauCityGmlConstructionSources.Create(CreateRequest()));
        }
        finally
        {
            PlateauCityGmlConstructionSources.FactoryProvider = PlateauCityGmlComposition.CreateConstructionSourceFactory;
        }
    }

    private static PlateauImportRequest CreateRequest()
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
            ServerUri: null);
    }

    private sealed class RecordingConstructionSourceFactory(IResoniteConstructionSource source) : IResoniteConstructionSourceFactory
    {
        private readonly IResoniteConstructionSource source = source;

        public int CreateAsyncCallCount { get; private set; }

        public PlateauImportRequest? LastRequest { get; private set; }

        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            CreateAsyncCallCount++;
            LastRequest = request;
            return Task.FromResult(source);
        }
    }

    private sealed class ThrowingConstructionSourceFactory(Exception exception) : IResoniteConstructionSourceFactory
    {
        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IResoniteConstructionSource>(exception);
        }
    }

    private sealed class CancellingConstructionSourceFactory : IResoniteConstructionSourceFactory
    {
        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromCanceled<IResoniteConstructionSource>(new CancellationToken(canceled: true));
        }
    }

    private sealed class StubConstructionSource : IResoniteConstructionSource
    {
        public ResoniteConstructionMetadata Metadata { get; } = new(
            "3.0",
            "stub",
            new PlateauImportRequest("stub", "53394525", DatasetSourceKind.Local, "/tmp", null),
            new PlateauSourceDataset([], [], [], []),
            new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(true, "credit", "license", "https://example.invalid"),
                []),
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects() => [];

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
