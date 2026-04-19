using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class ArchivePlateauDatasetContentSourceFactoryTests
{
    [Fact]
    public async Task MaterializeFileAsyncMaterializesSafeRelativePaths()
    {
        byte[] archiveBytes = CreateZipArchive(
            ("udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"),
            ("../../outside.txt", "escape"));

        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "malicious.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(archivePath);

        string outputRoot = Path.Combine(workRoot.Path, "output");
        Directory.CreateDirectory(outputRoot);
        string safePath = "udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml";
        string expectedSafePath = Path.GetFullPath(
            Path.Combine(
                outputRoot,
                "materialized",
                GetMaterializedArchiveCacheKey(archivePath),
                safePath.Replace('/', Path.DirectorySeparatorChar)));

        string actualSafePath = await datasetSource.MaterializeFileAsync(safePath, outputRoot);
        Assert.Equal(expectedSafePath, actualSafePath);
        Assert.True(File.Exists(actualSafePath));
    }

    [Fact]
    public async Task MaterializeFileAsyncDoesNotWriteOutsideDatasetCacheForTraversalPaths()
    {
        byte[] archiveBytes = CreateZipArchive(
            ("udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"),
            ("../../outside.txt", "escape"));

        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "malicious.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(archivePath);

        string outputRoot = Path.Combine(workRoot.Path, "output");
        Directory.CreateDirectory(outputRoot);

        string traversalPath = "../../outside.txt";
        string expectedUnsafePath = Path.GetFullPath(
            Path.Combine(
                outputRoot,
                "materialized",
                GetMaterializedArchiveCacheKey(archivePath),
                traversalPath.Replace('/', Path.DirectorySeparatorChar)));

        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.MaterializeFileAsync(traversalPath, outputRoot));
        Assert.False(File.Exists(expectedUnsafePath));
    }

    [Fact]
    public async Task MaterializeFileAsyncUsesDistinctCacheDirectoriesForSameNamedArchivesInDifferentPaths()
    {
        byte[] archiveBytes = CreateZipArchive(("udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));
        using TemporaryDirectory workRoot = new();
        string firstArchivePath = Path.Combine(workRoot.Path, "a", "dataset.zip");
        string secondArchivePath = Path.Combine(workRoot.Path, "b", "dataset.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(firstArchivePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondArchivePath)!);
        await File.WriteAllBytesAsync(firstArchivePath, archiveBytes);
        await File.WriteAllBytesAsync(secondArchivePath, archiveBytes);

        IPlateauDatasetContentSource firstDatasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(firstArchivePath);
        IPlateauDatasetContentSource secondDatasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(secondArchivePath);

        string outputRoot = Path.Combine(workRoot.Path, "output");
        string relativePath = "udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml";

        string firstMaterializedPath = await firstDatasetSource.MaterializeFileAsync(relativePath, outputRoot);
        string secondMaterializedPath = await secondDatasetSource.MaterializeFileAsync(relativePath, outputRoot);

        Assert.NotEqual(Path.GetDirectoryName(firstMaterializedPath), Path.GetDirectoryName(secondMaterializedPath));
    }

    [Fact]
    public async Task ResolveRelativePathUsesConfiguredArchiveFileLayoutPolicy()
    {
        using TemporaryDirectory datasetRoot = new();
        string modelPath = Path.Combine(datasetRoot.Path, "sub", "model.gml");
        string texturePath = Path.Combine(datasetRoot.Path, "textures", "override.png");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        await File.WriteAllTextAsync(modelPath, "<CityModel />");
        await File.WriteAllTextAsync(texturePath, "png");

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new OverrideRelativePathPolicy("textures/override.png"));

        string? resolved = datasetSource.ResolveRelativePath("sub/model.gml", "ignored.png");

        Assert.Equal("textures/override.png", resolved);
    }

    private static string GetMaterializedArchiveCacheKey(string archivePath)
    {
        string fullArchivePath = Path.GetFullPath(archivePath);
        string fileStem = Path.GetFileNameWithoutExtension(fullArchivePath);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullArchivePath))).ToLowerInvariant();
        return $"{fileStem}-{digest[..12]}";
    }

    private static byte[] CreateZipArchive(params (string Path, string Content)[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class OverrideRelativePathPolicy(string resolvedPath) : IArchiveFileLayoutPolicy
    {
        private readonly ArchiveFileLayoutPolicy inner = new();

        public bool IsSupportedArchivePath(string path) => inner.IsSupportedArchivePath(path);
        public string CreateSafePathSegment(string value) => inner.CreateSafePathSegment(value);
        public string ResolveDatasetRoot(string workRoot, string dataset) => inner.ResolveDatasetRoot(workRoot, dataset);
        public string GetMaterializedArchiveRoot(string outputRoot, string archivePath) => inner.GetMaterializedArchiveRoot(outputRoot, archivePath);
        public string GetMaterializedArchiveCacheKey(string archivePath) => inner.GetMaterializedArchiveCacheKey(archivePath);
        public string NormalizeRelativePath(string path) => inner.NormalizeRelativePath(path);
        public string CombineRelativePaths(params string?[] segments) => inner.CombineRelativePaths(segments);
        public string GetDirectoryPath(string relativePath) => inner.GetDirectoryPath(relativePath);
        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => resolvedPath;
        public string ResolveDatasetRootPrefix(IEnumerable<string> relativePaths) => inner.ResolveDatasetRootPrefix(relativePaths);
        public string StripDatasetRootPrefix(string relativePath, string datasetRootPrefix) => inner.StripDatasetRootPrefix(relativePath, datasetRootPrefix);
        public string GetNestedArchivePrefix(string prefix, string entryKey) => inner.GetNestedArchivePrefix(prefix, entryKey);
    }
}
