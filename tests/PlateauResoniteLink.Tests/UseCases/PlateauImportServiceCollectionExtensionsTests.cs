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
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(readResult ?? throw new NotSupportedException());
        }
    }

    private sealed class CustomImportedSceneSourceCreation
    {
        public Task<IImportedSceneSource> CreateAsync(
            ResolvedLocalPlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            _ = request;
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
