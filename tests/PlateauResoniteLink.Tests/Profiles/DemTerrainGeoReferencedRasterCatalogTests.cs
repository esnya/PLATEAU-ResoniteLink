using System;
using System.Buffers.Binary;
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
    public void DemTerrainRasterCacheKeySeparatesSourceScopeFromMeshAndBounds()
    {
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);

        DemTerrainRasterCacheKey firstKey = new(
            "tokyo-a",
            new DemTerrainRasterSourceScope("C:\\ortho-a"),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds);
        DemTerrainRasterCacheKey secondKey = new(
            "tokyo-a",
            new DemTerrainRasterSourceScope("C:\\ortho-b"),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds);
        DemTerrainRasterCacheKey thirdKey = new(
            "tokyo-b",
            new DemTerrainRasterSourceScope("C:\\ortho-a"),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds);

        Assert.NotEqual(firstKey, secondKey);
        Assert.NotEqual(firstKey, thirdKey);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncReusesDatasetRasterMaterializationAcrossDistinctBoundsKeys()
    {
        using TemporaryDirectory datasetRoot = new();
        RecordingDatasetContentSource datasetSource = CreateDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle firstBounds = new(35.0, 35.1, 139.0, 139.1);
        GeographicRectangle secondBounds = new(35.1, 35.2, 139.1, 139.2);

        _ = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), firstBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            firstBounds,
            CancellationToken.None);
        _ = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), secondBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            secondBounds,
            CancellationToken.None);

        Assert.Equal(2, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncReusesFallbackMaterializationAcrossCanonicalizedBoundsKeys()
    {
        using TemporaryDirectory datasetRoot = new();
        RecordingDatasetContentSource datasetSource = CreateDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle firstBounds = new(35.0, 35.1, 139.0, 139.1);
        GeographicRectangle secondBounds = new(35.0000001, 35.1000001, 139.0000001, 139.1000001);

        _ = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), firstBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            firstBounds,
            CancellationToken.None);
        _ = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), secondBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            secondBounds,
            CancellationToken.None);

        Assert.Equal(2, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncMaterializesDatasetRasterOnceForMetadataAndImageRead()
    {
        using TemporaryDirectory datasetRoot = new();
        LocalFileReadableDatasetContentSource datasetSource = CreateLocalFileReadableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);

        TerrainTextureGeoReferencedRasterSource? result = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            CancellationToken.None);

        TerrainTextureGeoReferencedRasterSource resolvedResult = Assert.IsType<TerrainTextureGeoReferencedRasterSource>(result);
        Assert.Equal("EPSG:4326", resolvedResult.Metadata.CoordinateSystemIdentifier);
        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);

        await using Stream _ = await resolvedResult.OpenReadAsync(CancellationToken.None);

        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncSharesConcurrentDatasetRasterMaterializationAcrossBoundsKeys()
    {
        using TemporaryDirectory datasetRoot = new();
        SucceedingGateableDatasetContentSource datasetSource = CreateSucceedingGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle firstBounds = new(35.0, 35.01, 139.0, 139.01);
        GeographicRectangle secondBounds = new(35.02, 35.03, 139.02, 139.03);

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), firstBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            firstBounds,
            CancellationToken.None);
        await datasetSource.OpenReadStarted.Task.WaitAsync(CancellationToken.None);
        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), secondBounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            secondBounds,
            CancellationToken.None);

        datasetSource.ReleaseOpenRead.TrySetResult();

        TerrainTextureGeoReferencedRasterSource? firstResult = await firstCall;
        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
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
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.OpenReadStarted.Task.WaitAsync(CancellationToken.None);
        Task<TerrainTextureGeoReferencedRasterSource?> secondCall = catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        datasetSource.ReleaseOpenRead.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        TerrainTextureGeoReferencedRasterSource? secondResult = await secondCall;
        TerrainTextureGeoReferencedRasterSource resolvedSecondResult = Assert.IsType<TerrainTextureGeoReferencedRasterSource>(secondResult);
        Assert.Equal("EPSG:4326", resolvedSecondResult.Metadata.CoordinateSystemIdentifier);

        TerrainTextureGeoReferencedRasterSource? thirdResult = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            CancellationToken.None);
        Assert.Same(secondResult, thirdResult);
        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
    }

    [Fact]
    public async Task TryResolveRasterSourceAsyncTreatsFaultedCandidateMaterializationAsUnavailableAfterCanceledWaiter()
    {
        using TemporaryDirectory datasetRoot = new();
        FaultingGateableDatasetContentSource datasetSource = CreateFaultingGateableDatasetSource(datasetRoot.Path);
        DemTerrainGeoReferencedRasterCatalog catalog = await CreateCatalogAsync(datasetSource);
        GeographicRectangle bounds = new(35.0, 35.1, 139.0, 139.1);
        using CancellationTokenSource firstCallerCancellation = new();

        Task<TerrainTextureGeoReferencedRasterSource?> firstCall = catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            firstCallerCancellation.Token);
        await datasetSource.OpenReadStarted.Task.WaitAsync(CancellationToken.None);

        await firstCallerCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);
        datasetSource.ReleaseOpenRead.TrySetResult();
        await datasetSource.BackgroundCompletion.Task.WaitAsync(CancellationToken.None);

        TerrainTextureGeoReferencedRasterSource? retryResult = await catalog.TryResolveRasterSourceAsync(
            new DemTerrainRasterCacheKey("tokyo23ku", catalog.CacheScope, ThirdRegionalMeshCode.Parse("53394525"), bounds),
            ThirdRegionalMeshCode.Parse("53394525"),
            bounds,
            CancellationToken.None);

        Assert.Null(retryResult);
        Assert.Equal(1, datasetSource.EnsureLocalFileCallCount);
        Assert.Equal(0, datasetSource.OpenReadCallCount);
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
        File.WriteAllBytes(
            rasterPath,
            CreateClassicLittleEndianGeoTiffBytes(
                modelTiePoint: [0.0, 0.0, 0.0, 139.0, 35.1, 0.0],
                pixelScale: [0.01, 0.01, 0.0],
                geoKeyDirectory:
                [
                    1, 1, 0, 1,
                    2048, 0, 1, 4326,
                ]));
        return new SucceedingGateableDatasetContentSource(datasetRoot, [Path.GetFileName(rasterPath)]);
    }

    private static LocalFileReadableDatasetContentSource CreateLocalFileReadableDatasetSource(string datasetRoot)
    {
        string rasterPath = Path.Combine(datasetRoot, "local.tif");
        File.WriteAllBytes(
            rasterPath,
            CreateClassicLittleEndianGeoTiffBytes(
                modelTiePoint: [0.0, 0.0, 0.0, 139.0, 35.1, 0.0],
                pixelScale: [0.01, 0.01, 0.0],
                geoKeyDirectory:
                [
                    1, 1, 0, 1,
                    2048, 0, 1, 4326,
                ]));
        return new LocalFileReadableDatasetContentSource(datasetRoot, [Path.GetFileName(rasterPath)]);
    }

    private static byte[] CreateClassicLittleEndianGeoTiffBytes(
        double[] modelTiePoint,
        double[] pixelScale,
        ushort[] geoKeyDirectory)
    {
        const ushort imageWidthTag = 256;
        const ushort imageLengthTag = 257;
        const ushort bitsPerSampleTag = 258;
        const ushort compressionTag = 259;
        const ushort photometricInterpretationTag = 262;
        const ushort stripOffsetsTag = 273;
        const ushort samplesPerPixelTag = 277;
        const ushort rowsPerStripTag = 278;
        const ushort stripByteCountsTag = 279;
        const ushort modelTiePointTag = 33922;
        const ushort pixelScaleTag = 33550;
        const ushort geoKeyDirectoryTag = 34735;
        const ushort typeShort = 3;
        const ushort typeLong = 4;
        const ushort typeDouble = 12;
        const int headerSize = 8;
        const int pixelWidth = 10;
        const int pixelHeight = 10;
        const int entryCount = 12;
        const int entrySize = 12;
        const uint singlePixelDataLength = pixelWidth * pixelHeight;
        int ifdSize = 2 + (entryCount * entrySize) + 4;
        int pixelScaleOffset = headerSize + ifdSize;
        int tiePointOffset = pixelScaleOffset + (pixelScale.Length * sizeof(double));
        int geoKeyOffset = tiePointOffset + (modelTiePoint.Length * sizeof(double));
        int stripDataOffset = geoKeyOffset + (geoKeyDirectory.Length * sizeof(ushort));
        byte[] bytes = new byte[stripDataOffset + singlePixelDataLength];

        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), entryCount);

        WriteClassicEntry(bytes, 10, imageWidthTag, typeLong, 1, pixelWidth);
        WriteClassicEntry(bytes, 22, imageLengthTag, typeLong, 1, pixelHeight);
        WriteClassicEntry(bytes, 34, bitsPerSampleTag, typeShort, 1, 8);
        WriteClassicEntry(bytes, 46, compressionTag, typeShort, 1, 1);
        WriteClassicEntry(bytes, 58, photometricInterpretationTag, typeShort, 1, 1);
        WriteClassicEntry(bytes, 70, stripOffsetsTag, typeLong, 1, (uint)stripDataOffset);
        WriteClassicEntry(bytes, 82, samplesPerPixelTag, typeShort, 1, 1);
        WriteClassicEntry(bytes, 94, rowsPerStripTag, typeLong, 1, pixelHeight);
        WriteClassicEntry(bytes, 106, stripByteCountsTag, typeLong, 1, singlePixelDataLength);
        WriteClassicEntry(bytes, 118, pixelScaleTag, typeDouble, (uint)pixelScale.Length, (uint)pixelScaleOffset);
        WriteClassicEntry(bytes, 130, modelTiePointTag, typeDouble, (uint)modelTiePoint.Length, (uint)tiePointOffset);
        WriteClassicEntry(bytes, 142, geoKeyDirectoryTag, typeShort, (uint)geoKeyDirectory.Length, (uint)geoKeyOffset);

        for (int index = 0; index < pixelScale.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(pixelScaleOffset + (index * sizeof(double)), sizeof(double)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(pixelScale[index])));
        }

        for (int index = 0; index < modelTiePoint.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(tiePointOffset + (index * sizeof(double)), sizeof(double)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(modelTiePoint[index])));
        }

        for (int index = 0; index < geoKeyDirectory.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(geoKeyOffset + (index * sizeof(ushort)), sizeof(ushort)),
                geoKeyDirectory[index]);
        }

        Array.Fill(bytes, byte.MaxValue, stripDataOffset, (int)singlePixelDataLength);

        return bytes;
    }

    private static void WriteClassicEntry(byte[] bytes, int offset, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2, 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8, 4), valueOrOffset);
    }

    private static async Task<DemTerrainGeoReferencedRasterCatalog> CreateCatalogAsync(IPlateauDatasetContentSource datasetSource)
    {
        IDemTerrainGeoReferencedRasterCatalog? catalog = await DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            DatasetLocation.Local(datasetSource.SourcePath),
            CreateStubDatasetContentSourceAsync(datasetSource),
            CancellationToken.None);
        return Assert.IsType<DemTerrainGeoReferencedRasterCatalog>(catalog);
    }

    private static Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> CreateStubDatasetContentSourceAsync(
        IPlateauDatasetContentSource datasetSource)
    {
        return (sourcePath, _) =>
        {
            Assert.Equal(datasetSource.SourcePath, sourcePath);
            return Task.FromResult(datasetSource);
        };
    }

    private sealed class RecordingDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int OpenReadCallCount { get; private set; }

        public int EnsureLocalFileCallCount { get; private set; }

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The catalog owns disposal of the returned stream during metadata probing.")]
        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            OpenReadCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(File.OpenRead(Path.Combine(SourcePath, relativePath)));
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Path.GetDirectoryName(SourcePath), outputRoot);
            return Task.FromResult(Path.Combine(SourcePath, relativePath));
        }
    }

    private sealed class FaultingGateableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int OpenReadCallCount { get; private set; }

        public int EnsureLocalFileCallCount { get; private set; }

        public TaskCompletionSource OpenReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseOpenRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BackgroundCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The catalog owns disposal of the returned stream during metadata probing.")]
        public async ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            OpenReadCallCount++;
            OpenReadStarted.TrySetResult();
            try
            {
                await ReleaseOpenRead.Task.WaitAsync(cancellationToken);
                throw new IOException("Simulated raster open failure.");
            }
            finally
            {
                BackgroundCompletion.TrySetResult();
            }
        }

        public async Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            OpenReadStarted.TrySetResult();
            try
            {
                await ReleaseOpenRead.Task.WaitAsync(cancellationToken);
                throw new IOException("Simulated raster materialization failure.");
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

        public int OpenReadCallCount { get; private set; }

        public int EnsureLocalFileCallCount { get; private set; }

        public TaskCompletionSource OpenReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseOpenRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        public async ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            OpenReadCallCount++;
            OpenReadStarted.TrySetResult();
            await ReleaseOpenRead.Task.WaitAsync(cancellationToken);
            return File.OpenRead(Path.Combine(SourcePath, relativePath));
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            OpenReadStarted.TrySetResult();
            return WaitAndReturnLocalFileAsync(relativePath, cancellationToken);
        }

        private async Task<string> WaitAndReturnLocalFileAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            await ReleaseOpenRead.Task.WaitAsync(cancellationToken);
            return Path.Combine(SourcePath, relativePath);
        }
    }

    private sealed class LocalFileReadableDatasetContentSource(
        string sourcePath,
        IReadOnlyList<string> files) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public int OpenReadCallCount { get; private set; }

        public int EnsureLocalFileCallCount { get; private set; }

        public IReadOnlyList<string> EnumerateFiles() => files;

        public bool FileExists(string relativePath) => files.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            OpenReadCallCount++;
            throw new InvalidOperationException("Raster source should prefer the local file content source.");
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            EnsureLocalFileCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Path.GetDirectoryName(SourcePath), outputRoot);
            return Task.FromResult(Path.Combine(SourcePath, relativePath));
        }
    }
}
