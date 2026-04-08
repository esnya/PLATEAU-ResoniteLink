using System.IO.Compression;
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
                ".dataset-cache",
                Path.GetFileNameWithoutExtension(archivePath),
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
                ".dataset-cache",
                Path.GetFileNameWithoutExtension(archivePath),
                traversalPath.Replace('/', Path.DirectorySeparatorChar)));

        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.MaterializeFileAsync(traversalPath, outputRoot));
        Assert.False(File.Exists(expectedUnsafePath));
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
