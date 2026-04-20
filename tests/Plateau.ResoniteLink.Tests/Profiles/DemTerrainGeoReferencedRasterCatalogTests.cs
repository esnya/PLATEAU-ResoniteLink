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

    [Fact]
    public async Task TryResolveRasterSourceAsyncAllowsLaterWaiterToCompleteAfterFirstCallerCancels()
    {
        using TemporaryDirectory datasetRoot = new();
        GateableDatasetContentSource datasetSource = CreateGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);
        using CancellationTokenSource firstCallerCancellation = new();

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.MaterializeStarted.Task.WaitAsync(CancellationToken.None);
        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        datasetSource.ReleaseMaterialize.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;
        Assert.Null(secondResult);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncKeepsInFlightTaskCachedWhenFirstWaiterCancels()
    {
        using TemporaryDirectory datasetRoot = new();
        GateableDatasetContentSource datasetSource = CreateGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);
        using CancellationTokenSource firstCallerCancellation = new();

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.MaterializeStarted.Task.WaitAsync(CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);

        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);
        datasetSource.ReleaseMaterialize.TrySetResult();

        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;
        Assert.Null(secondResult);
        Assert.Equal(1, datasetSource.MaterializeCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncEvictsBackgroundFaultAfterCanceledWaiterAbandonsIt()
    {
        using TemporaryDirectory datasetRoot = new();
        FaultingGateableDatasetContentSource datasetSource = CreateFaultingGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);
        using CancellationTokenSource firstCallerCancellation = new();

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.MaterializeStarted.Task.WaitAsync(CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        datasetSource.ReleaseMaterialize.TrySetResult();
        await datasetSource.BackgroundCompletion.Task.WaitAsync(CancellationToken.None);
        await Task.Yield();
        await Task.Delay(10);

        await Assert.ThrowsAnyAsync<IOException>(
            async () => await catalog.TryResolveRasterSourceAsync(
                "dem-fallback",
                "dem-fallback",
                bounds,
                CancellationToken.None));
        Assert.Equal(2, datasetSource.MaterializeCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncKeepsSuccessfulCachedTaskAfterCanceledWaiter()
    {
        using TemporaryDirectory datasetRoot = new();
        SucceedingGateableDatasetContentSource datasetSource = CreateSucceedingGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);
        using CancellationTokenSource firstCallerCancellation = new();

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.MaterializeStarted.Task.WaitAsync(CancellationToken.None);
        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        datasetSource.ReleaseMaterialize.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;
        Assert.Null(secondResult);

        TerrainTextureGeoReferencedRasterSource? thirdResult = await catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);
        Assert.Null(thirdResult);
        Assert.Equal(1, datasetSource.MaterializeCallCount);
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

    private static GateableDatasetContentSource CreateGateableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "gateable.tif");
        File.WriteAllText(rasterPath, "dummy");
        return new GateableDatasetContentSource(
            datasetRoot,
            [Path.GetFileName(rasterPath)]);
    }

    private static FaultingGateableDatasetContentSource CreateFaultingGateableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "faulting.tif");
        File.WriteAllText(rasterPath, "dummy");
        return new FaultingGateableDatasetContentSource(
            datasetRoot,
            [Path.GetFileName(rasterPath)]);
    }

    private static SucceedingGateableDatasetContentSource CreateSucceedingGateableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "succeeding.tif");
        File.WriteAllText(rasterPath, "dummy");
        return new SucceedingGateableDatasetContentSource(
            datasetRoot,
            [Path.GetFileName(rasterPath)]);
    }

    private static async Task<DemTerrainGeoReferencedRasterCatalog> CreateCatalogAsync(IPlateauDatasetContentSource datasetSource)
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

    private sealed class GateableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int MaterializeCallCount { get; private set; }

        public TaskCompletionSource MaterializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMaterialize { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            MaterializeCallCount++;
            MaterializeStarted.TrySetResult();
            await ReleaseMaterialize.Task.WaitAsync(cancellationToken);
            return Path.Combine(SourcePath, relativePath);
        }
    }

    private sealed class FaultingGateableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int MaterializeCallCount { get; private set; }

        public TaskCompletionSource MaterializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMaterialize { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BackgroundCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            MaterializeCallCount++;
            MaterializeStarted.TrySetResult();
            try
            {
                await ReleaseMaterialize.Task.WaitAsync(cancellationToken);
                throw new IOException("Simulated materialization failure.");
            }
            finally
            {
                BackgroundCompletion.TrySetResult();
            }
        }
    }

    private sealed class SucceedingGateableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int MaterializeCallCount { get; private set; }

        public TaskCompletionSource MaterializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMaterialize { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            MaterializeCallCount++;
            MaterializeStarted.TrySetResult();
            await ReleaseMaterialize.Task.WaitAsync(cancellationToken);
            return Path.Combine(SourcePath, relativePath);
        }
    }
}
