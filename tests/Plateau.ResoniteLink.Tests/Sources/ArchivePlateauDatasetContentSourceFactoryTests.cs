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
}
