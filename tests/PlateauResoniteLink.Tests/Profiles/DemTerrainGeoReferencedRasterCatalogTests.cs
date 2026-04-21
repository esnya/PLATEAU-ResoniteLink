using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainGeoReferencedRasterCatalogTests
{
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

        Assert.Equal(4, datasetSource.EnsureLocalFileCallCount);
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
        await datasetSource.EnsureLocalFileStarted.Task.WaitAsync(CancellationToken.None);
        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        datasetSource.ReleaseEnsureLocalFile.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;
        Assert.Null(secondResult);

        TerrainTextureGeoReferencedRasterSource? thirdResult = await catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None);
        Assert.Null(thirdResult);
        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncEvictsFaultedBackgroundTaskAfterCanceledWaiter()
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
        await datasetSource.EnsureLocalFileStarted.Task.WaitAsync(CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        datasetSource.ReleaseEnsureLocalFile.TrySetResult();
        await datasetSource.BackgroundCompletion.Task.WaitAsync(CancellationToken.None);
        await Task.Delay(10);

        await Assert.ThrowsAnyAsync<IOException>(async () => await catalog.TryResolveRasterSourceAsync(
            "dem-fallback",
            "dem-fallback",
            bounds,
            CancellationToken.None));
        Assert.Equal(2, datasetSource.EnsureLocalFileCallCount);
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

    private static FaultingGateableDatasetContentSource CreateFaultingGateableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "faulting.tif");
        File.WriteAllText(rasterPath, "dummy");
        return new FaultingGateableDatasetContentSource(datasetRoot, [Path.GetFileName(rasterPath)]);
    }

    private static SucceedingGateableDatasetContentSource CreateSucceedingGateableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "succeeding.tif");
        File.WriteAllText(rasterPath, "dummy");
        return new SucceedingGateableDatasetContentSource(datasetRoot, [Path.GetFileName(rasterPath)]);
    }

    private static async Task<DemTerrainGeoReferencedRasterCatalog> CreateCatalogAsync(IPlateauDatasetContentSource datasetSource)
    {
        IDemTerrainGeoReferencedRasterCatalog? catalog = await DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            DatasetLocation.Local(datasetSource.SourcePath),
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

        public int EnsureLocalFileCallCount { get; private set; }

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            _ = outputRoot;
            return Task.FromResult(Path.Combine(SourcePath, relativePath));
        }
    }

    private sealed class FaultingGateableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int EnsureLocalFileCallCount { get; private set; }

        public TaskCompletionSource EnsureLocalFileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseEnsureLocalFile { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BackgroundCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            _ = outputRoot;
            EnsureLocalFileStarted.TrySetResult();
            try
            {
                await ReleaseEnsureLocalFile.Task.WaitAsync(cancellationToken);
                throw new IOException("Simulated ensure-local-file failure.");
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

        public int EnsureLocalFileCallCount { get; private set; }

        public TaskCompletionSource EnsureLocalFileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseEnsureLocalFile { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            _ = outputRoot;
            EnsureLocalFileStarted.TrySetResult();
            await ReleaseEnsureLocalFile.Task.WaitAsync(cancellationToken);
            return Path.Combine(SourcePath, relativePath);
        }
    }
}
