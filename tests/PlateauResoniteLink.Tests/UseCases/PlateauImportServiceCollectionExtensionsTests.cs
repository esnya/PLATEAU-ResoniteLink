using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

public sealed class PlateauImportServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddImportedSceneSourceServicesUsesCustomComposerWhenCreatingSourceFromReader()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            cityGmlLocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"));
        ImportedSceneSourceSnapshot expectedReadResult = new(
            new ImportedSceneSourceDataset(
                new StubDatasetContentSource(request.CityGmlLocalSourcePath),
                [],
                ["bldg"],
                [],
                ["53394525"]),
            new ImportedSceneSourceContext(
                [],
                new GeodeticPoint(35.0, 139.0, 0.0)));
        StubImportedSceneSource expectedSource = new();
        CustomCityGmlDocumentReader reader = new(expectedReadResult);
        ReadCityGmlDocument readCityGmlDocument = reader.ReadAsync;
        RecordingConstructionComposer composer = new(expectedSource);
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(readCityGmlDocument)
            .AddSingleton<ImportedSceneSourceComposer>(composer.Compose)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();
        CreateImportedSceneSource createImportedSceneSource =
            provider.GetRequiredService<CreateImportedSceneSource>();

        IImportedSceneSource source = await createImportedSceneSource(request);

        Assert.Same(expectedSource, source);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(expectedReadResult, composer.LastReadResult);
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDatasetContentSourceCreation()
    {
        Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource =
            static (_, _) => throw new NotSupportedException();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(createDatasetContentSource)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(
            createDatasetContentSource,
            provider.GetRequiredService<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDocumentReader()
    {
        CustomCityGmlDocumentReader reader = new();
        ReadCityGmlDocument readCityGmlDocument = reader.ReadAsync;
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(readCityGmlDocument)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(readCityGmlDocument, provider.GetRequiredService<ReadCityGmlDocument>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomImportedSceneSourceCreation()
    {
        CustomImportedSceneSourceCreation sourceCreation = new();
        CreateImportedSceneSource createImportedSceneSource = sourceCreation.CreateAsync;
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(createImportedSceneSource)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(createImportedSceneSource, provider.GetRequiredService<CreateImportedSceneSource>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDemTextureSourceResolver()
    {
        CustomDemTextureSourcePolicy policy = new();
        ResolveDemTextureSources resolveDemTextureSources = policy.ResolveAsync;
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(resolveDemTextureSources)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(resolveDemTextureSources, provider.GetRequiredService<ResolveDemTextureSources>());
    }

    private sealed class CustomCityGmlDocumentReader(ImportedSceneSourceSnapshot? readResult = null)
    {
        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public Task<ImportedSceneSourceSnapshot> ReadAsync(
            ResolvedLocalPlateauImportRequest request,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            _ = logger;
            LastRequest = request;
            return Task.FromResult(readResult ?? throw new NotSupportedException());
        }
    }

    private sealed class CustomImportedSceneSourceCreation
    {
        public Task<IImportedSceneSource> CreateAsync(
            ResolvedLocalPlateauImportRequest request,
            ILoggerFactory? loggerFactory = null,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = loggerFactory;
            throw new NotSupportedException();
        }
    }

    private sealed class CustomDemTextureSourcePolicy
    {
        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingConstructionComposer(IImportedSceneSource source)
    {
        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public ImportedSceneSourceSnapshot? LastReadResult { get; private set; }

        public IImportedSceneSource Compose(
            ResolvedLocalPlateauImportRequest request,
            ImportedSceneSourceSnapshot readResult,
            ImportedObjectUnitOptimizer objectUnitOptimizer,
            ILoggerFactory? loggerFactory = null)
        {
            LastRequest = request;
            LastReadResult = readResult;
            _ = loggerFactory;
            _ = objectUnitOptimizer;
            return source;
        }
    }

    private sealed class StubDatasetContentSource(string sourcePath) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubImportedSceneSource : IImportedSceneSource
    {
        public ImportedSceneMetadata Metadata { get; } = new(
            "3.0",
            "stub",
            new PlateauImportRequest("stub", "53394525", DatasetLocation.Local("/tmp")),
            new PlateauSourceDataset([], [], []),
            new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid")),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));

        public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ImportedObjectUnit("stub.gml", "bldg", null, []);
            await Task.CompletedTask;
        }
    }
}
