using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

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

        ImportedSceneSourceSnapshot readResult = await reader.ReadAsync(
            CreateResolvedRequest(fixturePath, ["bldg"]));
        ImportedSceneSourceDataset documentSet = readResult.DocumentSet;

        Assert.Equal(fixturePath, documentSet.DatasetSource.SourcePath);
        Assert.Equal(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.SelectedMeshCodes);
    }

    [Fact]
    public async Task ReadAsyncDoesNotOpenDemFilesDuringSetupDiscovery()
    {
        CountingDatasetContentSource datasetSource = new(
            "C:\\fixtures\\plateau",
            ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"]);
        LocalCityGmlDocumentReader reader = new(
            new StubDatasetContentSourceFactory(datasetSource),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());

        ImportedSceneSourceSnapshot readResult = await reader.ReadAsync(
            CreateResolvedRequest(datasetSource.SourcePath, ["dem"]));
        ImportedSceneSourceDataset documentSet = readResult.DocumentSet;

        Assert.Equal(["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["dem"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    [Fact]
    public async Task ReadAsyncUsesSelectedMeshCodesForDiscoveryOriginWhenExactRequestMatchesParentSourceFiles()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");
        LocalCityGmlDocumentReader reader = new(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());

        ImportedSceneSourceSnapshot readResult = await reader.ReadAsync(
            CreateResolvedRequest(fixturePath, ["dem", "tran"]));

        GeodeticCoordinate expectedOrigin = MeshCodeBounds.TryParse("53394525")!.GetGeodeticCenter();
        Assert.Equal(["53394525"], readResult.DocumentSet.SelectedMeshCodes);
        Assert.Equal(expectedOrigin.Latitude, readResult.DiscoveryContext.GlobalOriginPoint.Latitude, 12);
        Assert.Equal(expectedOrigin.Longitude, readResult.DiscoveryContext.GlobalOriginPoint.Longitude, 12);
        Assert.Equal(expectedOrigin.Altitude, readResult.DiscoveryContext.GlobalOriginPoint.Altitude, 12);
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

    private static ResolvedLocalPlateauImportRequest CreateResolvedRequest(
        string cityGmlLocalSourcePath,
        IReadOnlyList<string> packageNames)
    {
        return ResolvedLocalPlateauImportRequestTestFactory.Create(
            cityGmlLocalSourcePath: cityGmlLocalSourcePath,
            packageNames: packageNames);
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
            throw new InvalidOperationException($"Discovery should not open '{relativePath}'.");
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Discovery should not materialize files.");
        }
    }
}
