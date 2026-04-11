using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteTextureImportResolverTests
{
    [Fact]
    public async Task ResolveAsyncReadsDatasetTextureAsRawAndCachesResult()
    {
        using TemporaryDirectory datasetRoot = new();

        string relativeTexturePath = "textures/albedo.png";
        WriteDatasetImage(datasetRoot.Path, relativeTexturePath);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();

        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            [],
            terrainTextureAssetGenerator);

        ResoniteTextureImport firstResolution = await resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);
        ResoniteTextureImport secondResolution = await resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        Assert.Same(firstResolution, secondResolution);
        Assert.Equal(1, datasetContentSource.OpenReadCount);
        Assert.Empty(terrainTextureAssetGenerator.RequestedOverlays);

        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(firstResolution);
        Assert.Equal(1, rawImport.Width);
        Assert.Equal(1, rawImport.Height);
        Assert.Equal("sRGB", rawImport.ColorProfile);
        Assert.Equal(relativeTexturePath, rawImport.Identity);
    }

    [Fact]
    public async Task ResolveAsyncUsesTerrainOverlayBeforeDatasetMaterialization()
    {
        using TemporaryDirectory workRoot = new();
        FakeDatasetContentSource datasetContentSource = new(workRoot.Path);
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();

        TerrainTextureOverlay terrainTextureOverlay = new(
            TexturePath: "textures/overlay.png",
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: 4);

        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            [terrainTextureOverlay],
            terrainTextureAssetGenerator);

        ResoniteTextureImport firstResolution = await resolver.ResolveAsync(
            terrainTextureOverlay.TexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);
        ResoniteTextureImport secondResolution = await resolver.ResolveAsync(
            terrainTextureOverlay.TexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        Assert.Same(firstResolution, secondResolution);
        Assert.Single(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.Equal(0, datasetContentSource.OpenReadCount);

        ResoniteRawTextureImport rawTextureImport = Assert.IsType<ResoniteRawTextureImport>(firstResolution);
        Assert.Equal(1, rawTextureImport.Width);
        Assert.Equal(1, rawTextureImport.Height);
        Assert.Equal("sRGB", rawTextureImport.ColorProfile);
        Assert.Equal(terrainTextureOverlay.TexturePath, rawTextureImport.Identity);
    }

    [Fact]
    public async Task ResolveAsyncDoesNotPoisonSharedResolutionWhenFirstWaiterIsCanceled()
    {
        using TemporaryDirectory datasetRoot = new();

        string relativeTexturePath = "textures/albedo.png";
        WriteDatasetImage(datasetRoot.Path, relativeTexturePath);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path, openReadDelay: TimeSpan.FromMilliseconds(100));
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();

        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            [],
            terrainTextureAssetGenerator);

        using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMilliseconds(20));
        Task<ResoniteTextureImport> canceledResolution = resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            cancellationTokenSource.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledResolution);

        ResoniteTextureImport successfulResolution = await resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        Assert.Equal(1, datasetContentSource.OpenReadCount);
        Assert.Empty(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.IsType<ResoniteRawTextureImport>(successfulResolution);
    }

    [Fact]
    public async Task ResolveAsyncDoesNotLetCallerCancellationPoisonSharedResolution()
    {
        DelayedDatasetContentSource datasetContentSource = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            [],
            terrainTextureAssetGenerator);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<ResoniteTextureImport> canceledRequest = resolver.ResolveAsync(
            "textures/albedo.png",
            ResoniteTextureSourceKind.Dataset,
            cancellationTokenSource.Token);

        await datasetContentSource.OpenReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledRequest);

        datasetContentSource.AllowOpenReadCompletion.SetResult();

        ResoniteTextureImport resolvedTexture = await resolver.ResolveAsync(
            "textures/albedo.png",
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(resolvedTexture);
        Assert.Equal("textures/albedo.png", rawImport.Identity);
        Assert.Equal(1, datasetContentSource.OpenReadCount);
    }

    [Fact]
    public async Task ResolveAsyncRemovesFaultedSharedResolutionAndRetries()
    {
        FlakyDatasetContentSource datasetContentSource = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            [],
            terrainTextureAssetGenerator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await resolver.ResolveAsync(
                "textures/albedo.png",
                ResoniteTextureSourceKind.Dataset,
                CancellationToken.None));

        ResoniteTextureImport resolvedTexture = await resolver.ResolveAsync(
            "textures/albedo.png",
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        ResoniteRawTextureImport rawImport = Assert.IsType<ResoniteRawTextureImport>(resolvedTexture);
        Assert.Equal("textures/albedo.png", rawImport.Identity);
        Assert.Equal(2, datasetContentSource.OpenReadCount);
    }

    private static void WriteDatasetImage(string datasetRoot, string relativePath)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255));
        image.SaveAsPng(absolutePath);
    }

    private sealed class FakeDatasetContentSource(string sourceRoot, TimeSpan? openReadDelay = null) : IPlateauDatasetContentSource
    {
        public int OpenReadCount { get; private set; }

        public string SourcePath => sourceRoot;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
                .ToArray();
        }

        public bool FileExists(string relativePath)
        {
            return File.Exists(GetAbsolutePath(relativePath));
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OpenReadCoreAsync(relativePath, cancellationToken);
        }

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private async ValueTask<Stream> OpenReadCoreAsync(string relativePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (openReadDelay is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            OpenReadCount++;
#pragma warning disable CA2000
            return File.OpenRead(GetAbsolutePath(relativePath));
#pragma warning restore CA2000
        }

        private string GetAbsolutePath(string relativePath)
        {
            return Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private sealed class StubTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

        public Task<ResoniteRawTextureImport> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedOverlays.Add(terrainTextureOverlay);
            return Task.FromResult(
                new ResoniteRawTextureImport(
                    1,
                    1,
                    "sRGB",
                    [0, 0, 0, byte.MaxValue],
                    terrainTextureOverlay.TexturePath));
        }
    }

    private sealed class DelayedDatasetContentSource : IPlateauDatasetContentSource
    {
        public TaskCompletionSource OpenReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowOpenReadCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenReadCount { get; private set; }

        public string SourcePath => "/dataset";

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => true;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => OpenReadCoreAsync(cancellationToken);

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private async ValueTask<Stream> OpenReadCoreAsync(CancellationToken cancellationToken)
        {
            OpenReadStarted.TrySetResult();
            await AllowOpenReadCompletion.Task.WaitAsync(cancellationToken);
            OpenReadCount++;
            return new MemoryStream(CreateSinglePixelPng(), writable: false);
        }
    }

    private sealed class FlakyDatasetContentSource : IPlateauDatasetContentSource
    {
        public int OpenReadCount { get; private set; }

        public string SourcePath => "/dataset";

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => true;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => OpenReadCoreAsync(cancellationToken);

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private ValueTask<Stream> OpenReadCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenReadCount++;
            if (OpenReadCount == 1)
            {
                throw new InvalidOperationException("Simulated open-read failure.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(CreateSinglePixelPng(), writable: false));
        }
    }

    private static byte[] CreateSinglePixelPng()
    {
        using MemoryStream stream = new();
        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255)))
        {
            image.SaveAsPng(stream);
        }

        return stream.ToArray();
    }
}
