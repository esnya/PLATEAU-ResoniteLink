using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteTextureImportResolverTests
{
    [Fact]
    public async Task ResolveAsyncMaterializesDatasetTextureAndCachesResult()
    {
        using TemporaryDirectory datasetRoot = new();
        using TemporaryDirectory workRoot = new();

        string relativeTexturePath = "textures/albedo.png";
        WriteDatasetImage(datasetRoot.Path, relativeTexturePath);
        FakeDatasetContentSource datasetContentSource = new(datasetRoot.Path);
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();

        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            workRoot.Path,
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
        Assert.Equal(1, datasetContentSource.MaterializeCount);
        Assert.Empty(terrainTextureAssetGenerator.RequestedOverlays);

        ResoniteFileTextureImport fileImport = Assert.IsType<ResoniteFileTextureImport>(firstResolution);
        Assert.StartsWith(workRoot.Path, fileImport.AbsolutePath, StringComparison.Ordinal);
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
            workRoot.Path,
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
        Assert.Equal(0, datasetContentSource.MaterializeCount);

        ResoniteRawTextureImport rawTextureImport = Assert.IsType<ResoniteRawTextureImport>(firstResolution);
        Assert.Equal(1, rawTextureImport.Width);
        Assert.Equal(1, rawTextureImport.Height);
        Assert.Equal("sRGB", rawTextureImport.ColorProfile);
        Assert.Equal(terrainTextureOverlay.TexturePath, rawTextureImport.Identity);
    }

    [Fact]
    public async Task ResolveAsyncDoesNotPoisonSharedResolutionWhenFirstWaiterIsCanceled()
    {
        using TemporaryDirectory workRoot = new();
        DelayedDatasetContentSource datasetContentSource = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();

        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            workRoot.Path,
            [],
            terrainTextureAssetGenerator);
        using CancellationTokenSource cancellationTokenSource = new();
        const string relativeTexturePath = "textures/albedo.png";

        Task<ResoniteTextureImport> canceledResolution = resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            cancellationTokenSource.Token);

        await datasetContentSource.MaterializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationTokenSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledResolution);

        datasetContentSource.AllowMaterializeCompletion.SetResult();

        ResoniteTextureImport successfulResolution = await resolver.ResolveAsync(
            relativeTexturePath,
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        Assert.Equal(1, datasetContentSource.MaterializeCount);
        Assert.Empty(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.IsType<ResoniteFileTextureImport>(successfulResolution);
    }

    [Fact]
    public async Task ResolveAsyncDoesNotLetCallerCancellationPoisonSharedResolution()
    {
        using TemporaryDirectory workRoot = new();
        DelayedDatasetContentSource datasetContentSource = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            workRoot.Path,
            [],
            terrainTextureAssetGenerator);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<ResoniteTextureImport> canceledRequest = resolver.ResolveAsync(
            "textures/albedo.png",
            ResoniteTextureSourceKind.Dataset,
            cancellationTokenSource.Token);

        await datasetContentSource.MaterializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledRequest);

        datasetContentSource.AllowMaterializeCompletion.SetResult();

        ResoniteTextureImport resolvedTexture = await resolver.ResolveAsync(
            "textures/albedo.png",
            ResoniteTextureSourceKind.Dataset,
            CancellationToken.None);

        ResoniteFileTextureImport fileImport = Assert.IsType<ResoniteFileTextureImport>(resolvedTexture);
        Assert.Equal("/generated/textures/albedo.png", fileImport.AbsolutePath);
        Assert.Equal(1, datasetContentSource.MaterializeCount);
    }

    [Fact]
    public async Task ResolveAsyncRemovesFaultedSharedResolutionAndRetries()
    {
        using TemporaryDirectory workRoot = new();
        FlakyDatasetContentSource datasetContentSource = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteTextureImportResolver resolver = new(
            datasetContentSource,
            workRoot.Path,
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

        ResoniteFileTextureImport fileImport = Assert.IsType<ResoniteFileTextureImport>(resolvedTexture);
        Assert.Equal("/generated/textures/albedo.png", fileImport.AbsolutePath);
        Assert.Equal(2, datasetContentSource.MaterializeCount);
    }

    private static void WriteDatasetImage(string datasetRoot, string relativePath)
    {
        string absolutePath = Path.Combine(datasetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        using Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255));
        image.SaveAsPng(absolutePath);
    }

    private sealed class FakeDatasetContentSource(string sourceRoot, TimeSpan? materializeDelay = null) : IPlateauDatasetContentSource
    {
        public int MaterializeCount { get; private set; }

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
#pragma warning disable CA2000
            return ValueTask.FromResult<Stream>(File.OpenRead(GetAbsolutePath(relativePath)));
#pragma warning restore CA2000
        }

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (materializeDelay is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            string sourcePath = GetAbsolutePath(relativePath);
            string outputPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using FileStream outputStream = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(outputStream, cancellationToken);
            MaterializeCount++;
            return outputPath;
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
        public TaskCompletionSource MaterializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowMaterializeCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaterializeCount { get; private set; }

        public string SourcePath => "/dataset";

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => true;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            MaterializeStarted.TrySetResult();
            await AllowMaterializeCompletion.Task.WaitAsync(cancellationToken);
            MaterializeCount++;
            return "/generated/" + relativePath.Replace('\\', '/');
        }
    }

    private sealed class FlakyDatasetContentSource : IPlateauDatasetContentSource
    {
        public int MaterializeCount { get; private set; }

        public string SourcePath => "/dataset";

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => true;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaterializeCount++;
            if (MaterializeCount == 1)
            {
                throw new InvalidOperationException("Simulated materialization failure.");
            }

            return Task.FromResult("/generated/" + relativePath.Replace('\\', '/'));
        }
    }
}
