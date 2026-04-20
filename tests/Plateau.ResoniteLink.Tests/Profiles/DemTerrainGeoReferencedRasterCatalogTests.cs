using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DemTerrainGeoReferencedRasterCatalogTests
{
    [Fact]
    public async Task TryResolveRasterSourceAsyncReusesMaterializedCandidatesForSameCacheKey()
    {
        using TemporaryDirectory datasetRoot = new();
        RecordingDatasetContentSource datasetSource = CreateDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);

        _ = await catalog.TryResolveRasterSourceAsync("dem-fallback", "dem-fallback", bounds, CancellationToken.None);
        _ = await catalog.TryResolveRasterSourceAsync("dem-fallback", "dem-fallback", bounds, CancellationToken.None);

        Assert.Equal(2, datasetSource.MaterializeCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncDoesNotReuseFallbackMaterializationAcrossDistinctBoundsKeys()
    {
        using TemporaryDirectory datasetRoot = new();
        RecordingDatasetContentSource datasetSource = CreateDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);

        _ = await catalog.TryResolveRasterSourceAsync(
            "dem-fallback|35.000000|35.100000|139.000000|139.100000",
            "dem-fallback",
            new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
            CancellationToken.None);
        _ = await catalog.TryResolveRasterSourceAsync(
            "dem-fallback|35.100000|35.200000|139.100000|139.200000",
            "dem-fallback",
            new GeographicRectangle(35.1, 35.2, 139.1, 139.2),
            CancellationToken.None);

        Assert.Equal(4, datasetSource.MaterializeCallCount);
    }

    private static RecordingDatasetContentSource CreateDatasetSource(string datasetRoot)
    {
        string westRasterPath = Path.Combine(datasetRoot, "west.tif");
        string eastRasterPath = Path.Combine(datasetRoot, "east.tif");
        File.WriteAllText(westRasterPath, "dummy");
        File.WriteAllText(eastRasterPath, "dummy");
        return new RecordingDatasetContentSource(
            datasetRoot,
            [Path.GetFileName(westRasterPath), Path.GetFileName(eastRasterPath)]);
    }

    private static async Task<DemTerrainGeoReferencedRasterCatalog> CreateCatalogAsync(RecordingDatasetContentSource datasetSource)
    {
        DemTerrainGeoReferencedRasterCatalog? catalog = await DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            PlateauImportSource.Local(datasetSource.SourcePath),
            new StubDatasetContentSourceFactory(datasetSource),
            CancellationToken.None);

        return Assert.IsType<DemTerrainGeoReferencedRasterCatalog>(catalog);
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

    private sealed class RecordingDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int MaterializeCallCount { get; private set; }

        public IReadOnlyList<string> EnumerateFiles()
        {
            return files;
        }

        public bool FileExists(string relativePath)
        {
            return files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);
        }

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            MaterializeCallCount++;
            return Task.FromResult(Path.Combine(SourcePath, relativePath));
        }
    }
}
