using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.UseCases;

public sealed class PlateauImportServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddPlateauCityGmlImportServicesUsesCustomReaderAndComposerWhenFactoryCreatesSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
            ServerUri: null);
        LocalCityGmlDocumentSet expectedDocumentSet = new(
            new StubDatasetContentSource(request.LocalSourcePath!),
            [],
            ["bldg"],
            [],
            ["53394525"],
            [],
            [],
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697"),
            new GeodeticPoint(35.0, 139.0, 0.0),
            terrainHeightSampler: null);
        StubConstructionSource expectedSource = new();
        CustomCityGmlDocumentReader reader = new(expectedDocumentSet);
        RecordingConstructionComposer composer = new(expectedSource);
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddSingleton<IResoniteConstructionComposer>(composer)
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();
        IResoniteConstructionSourceFactory factory = provider.GetRequiredService<IResoniteConstructionSourceFactory>();

        IResoniteConstructionSource source = await factory.CreateAsync(request);

        Assert.Same(expectedSource, source);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(expectedDocumentSet, composer.LastDocumentSet);
    }

    [Fact]
    public void AddPlateauCityGmlImportServicesPreservesCustomDatasetContentSourceFactory()
    {
        CustomPlateauDatasetContentSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IPlateauDatasetContentSourceFactory>(factory)
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IPlateauDatasetContentSourceFactory>());
    }

    [Fact]
    public void AddPlateauCityGmlImportServicesPreservesCustomDocumentReader()
    {
        CustomCityGmlDocumentReader reader = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(reader, provider.GetRequiredService<ICityGmlDocumentReader>());
    }

    [Fact]
    public void AddPlateauCityGmlImportServicesPreservesCustomConstructionSourceFactory()
    {
        CustomConstructionSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IResoniteConstructionSourceFactory>(factory)
            .AddPlateauCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IResoniteConstructionSourceFactory>());
    }

    private sealed class CustomPlateauDatasetContentSourceFactory : IPlateauDatasetContentSourceFactory
    {
        public Task<IPlateauDatasetContentSource> CreateAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CustomCityGmlDocumentReader(LocalCityGmlDocumentSet? documentSet = null) : ICityGmlDocumentReader
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public Task<LocalCityGmlDocumentSet> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(documentSet ?? throw new NotSupportedException());
        }
    }

    private sealed class CustomConstructionSourceFactory : IResoniteConstructionSourceFactory
    {
        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingConstructionComposer(IResoniteConstructionSource source) : IResoniteConstructionComposer
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentSet? LastDocumentSet { get; private set; }

        public IResoniteConstructionSource Compose(
            PlateauImportRequest request,
            LocalCityGmlDocumentSet documentSet,
            Action<string>? progressReporter = null)
        {
            LastRequest = request;
            LastDocumentSet = documentSet;
            return source;
        }
    }

    private sealed class StubDatasetContentSource(string sourcePath) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

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

    private sealed class StubConstructionSource : IResoniteConstructionSource
    {
        public ResoniteConstructionMetadata Metadata { get; } = new(
            "3.0",
            "stub",
            new PlateauImportRequest("stub", "53394525", DatasetSourceKind.Local, "/tmp", null),
            new PlateauSourceDataset([], [], [], []),
            new ResoniteAttribution(
                new LicenseAttributionMetadata(true, "credit", "license", "https://example.invalid"),
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
