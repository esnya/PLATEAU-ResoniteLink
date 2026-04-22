using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentReaderTests
{
    [Fact]
    public async Task ReadAsyncCreatesDocumentSetBoundaryFromStableLocalFixture()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        LocalCityGmlDocumentReader reader = new(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());

        LocalCityGmlDocumentReadResult readResult = await reader.ReadAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                PackageNames: ["bldg"],
                ServerUri: null));
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;

        Assert.Equal(fixturePath, documentSet.DatasetSource.SourcePath);
        Assert.Equal(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.SelectedMeshCodes);
    }

    [Fact]
    public async Task ReadAsyncDoesNotOpenDemFilesDuringBootstrapDiscovery()
    {
        CountingDatasetContentSource datasetSource = new(
            "C:\\fixtures\\plateau",
            ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);
        LocalCityGmlDocumentReader reader = new(
            new StubDatasetContentSourceFactory(datasetSource),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());

        LocalCityGmlDocumentReadResult readResult = await reader.ReadAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetSource.SourcePath,
                PackageNames: ["dem"],
                ServerUri: null));
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;

        Assert.Equal(["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["dem"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    private sealed class StubDatasetContentSourceFactory(IPlateauDatasetContentSource datasetSource) : IPlateauDatasetContentSourceFactory
    {
        public Task<IPlateauDatasetContentSource> CreateAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(datasetSource.SourcePath, sourcePath);
            return Task.FromResult(datasetSource);
        }
    }

    private sealed class CountingDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public int OpenReadCallCount { get; private set; }

        public string SourcePath => sourcePath;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return files;
        }

        public bool FileExists(string relativePath)
        {
            return files.Contains(relativePath, StringComparer.Ordinal);
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
        }

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            OpenReadCallCount++;
            throw new InvalidOperationException($"Bootstrap discovery should not open '{relativePath}'.");
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Bootstrap discovery should not materialize files.");
        }
    }
}
