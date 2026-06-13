using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

public sealed class PlateauImportServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddImportedSceneSourceServicesUsesCustomComposerWhenFactoryCreatesSourceFromReader()
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
        RecordingConstructionComposer composer = new(expectedSource);
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddSingleton<IImportedSceneSourceComposer>(composer)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();
        IImportedSceneSourceFactory factory = provider.GetRequiredService<IImportedSceneSourceFactory>();

        IImportedSceneSource source = await factory.CreateAsync(request);

        Assert.Same(expectedSource, source);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(expectedReadResult, composer.LastReadResult);
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDatasetContentSourceFactory()
    {
        CustomPlateauDatasetContentSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IPlateauDatasetContentSourceFactory>(factory)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IPlateauDatasetContentSourceFactory>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDocumentReader()
    {
        CustomCityGmlDocumentReader reader = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(reader, provider.GetRequiredService<ICityGmlDocumentReader>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomImportedSceneSourceFactory()
    {
        CustomImportedSceneSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IImportedSceneSourceFactory>(factory)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IImportedSceneSourceFactory>());
    }

    [Fact]
    public void AddImportedSceneSourceServicesPreservesCustomDemTextureSourcePolicy()
    {
        CustomDemTextureSourcePolicy policy = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IDemTextureSourcePolicy>(policy)
            .AddImportedSceneSourceServices()
            .BuildServiceProvider();

        Assert.Same(policy, provider.GetRequiredService<IDemTextureSourcePolicy>());
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

    private sealed class CustomCityGmlDocumentReader(ImportedSceneSourceSnapshot? readResult = null) : ICityGmlDocumentReader
    {
        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public Task<ImportedSceneSourceSnapshot> ReadAsync(
            ResolvedLocalPlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(readResult ?? throw new NotSupportedException());
        }
    }

    private sealed class CustomImportedSceneSourceFactory : IImportedSceneSourceFactory
    {
        public Task<IImportedSceneSource> CreateAsync(
            ResolvedLocalPlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }

    private sealed class CustomDemTextureSourcePolicy : IDemTextureSourcePolicy
    {
        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingConstructionComposer(IImportedSceneSource source) : IImportedSceneSourceComposer
    {
        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public ImportedSceneSourceSnapshot? LastReadResult { get; private set; }

        public IImportedSceneSource Compose(
            ResolvedLocalPlateauImportRequest request,
            ImportedSceneSourceSnapshot readResult,
            IImportedObjectUnitOptimizer objectUnitOptimizer)
        {
            LastRequest = request;
            LastReadResult = readResult;
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
