using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

public sealed class PlateauImportServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddLocalCityGmlImportServicesUsesCustomComposerWhenFactoryCreatesSourceFromReader()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(TestData.GetFixturePath("LocalPlateauDataset")));
        LocalCityGmlDocumentReadResult expectedReadResult = new(
            new LocalCityGmlDocumentSet(
                new StubDatasetContentSource(request.LocalSourcePath!),
                [],
                ["bldg"],
                [],
                ["53394525"]),
            new LocalCityGmlBootstrapContext(
                [],
                new GeodeticPoint(35.0, 139.0, 0.0)));
        StubConstructionSource expectedSource = new();
        CustomCityGmlDocumentReader reader = new(expectedReadResult);
        RecordingConstructionComposer composer = new(expectedSource);
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddSingleton<IImportedSceneSourceComposer>(composer)
            .AddLocalCityGmlImportServices()
            .BuildServiceProvider();
        IImportedSceneSourceFactory factory = provider.GetRequiredService<IImportedSceneSourceFactory>();

        IImportedSceneSource source = await factory.CreateAsync(request);

        Assert.Same(expectedSource, source);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(expectedReadResult, composer.LastReadResult);
    }

    [Fact]
    public void AddLocalCityGmlImportServicesPreservesCustomDatasetContentSourceFactory()
    {
        CustomPlateauDatasetContentSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IPlateauDatasetContentSourceFactory>(factory)
            .AddLocalCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IPlateauDatasetContentSourceFactory>());
    }

    [Fact]
    public void AddLocalCityGmlImportServicesPreservesCustomDocumentReader()
    {
        CustomCityGmlDocumentReader reader = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICityGmlDocumentReader>(reader)
            .AddLocalCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(reader, provider.GetRequiredService<ICityGmlDocumentReader>());
    }

    [Fact]
    public void AddLocalCityGmlImportServicesPreservesCustomConstructionSourceFactory()
    {
        CustomConstructionSourceFactory factory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IImportedSceneSourceFactory>(factory)
            .AddLocalCityGmlImportServices()
            .BuildServiceProvider();

        Assert.Same(factory, provider.GetRequiredService<IImportedSceneSourceFactory>());
    }

    [Fact]
    public void AddLocalCityGmlImportServicesPreservesCustomDemTextureSourcePolicy()
    {
        CustomDemTextureSourcePolicy policy = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IDemTextureSourcePolicy>(policy)
            .AddLocalCityGmlImportServices()
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

    private sealed class CustomCityGmlDocumentReader(LocalCityGmlDocumentReadResult? readResult = null) : ICityGmlDocumentReader
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public Task<LocalCityGmlDocumentReadResult> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(readResult ?? throw new NotSupportedException());
        }
    }

    private sealed class CustomConstructionSourceFactory : IImportedSceneSourceFactory
    {
        public Task<IImportedSceneSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            _ = request;
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
        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentReadResult? LastReadResult { get; private set; }

        public IImportedSceneSource Compose(
            PlateauImportRequest request,
            LocalCityGmlDocumentReadResult readResult,
            Action<string>? progressReporter = null)
        {
            LastRequest = request;
            LastReadResult = readResult;
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

    private sealed class StubConstructionSource : IImportedSceneSource
    {
        public ImportedSceneMetadata Metadata { get; } = new(
            "3.0",
            "stub",
            new PlateauImportRequest("stub", "53394525", DatasetLocation.Local("/tmp")),
            new PlateauSourceDataset([], [], []),
            new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid"),
                []),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));

        public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
