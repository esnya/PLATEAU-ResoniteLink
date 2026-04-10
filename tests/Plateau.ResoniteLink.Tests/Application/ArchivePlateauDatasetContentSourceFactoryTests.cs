using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;

using Xunit.Sdk;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class ArchivePlateauDatasetContentSourceFactoryTests
{
    [Fact]
    public async Task LocalSourceRejectsTraversalPaths()
    {
        using TemporaryDirectory datasetRoot = new();
        Directory.CreateDirectory(Path.Combine(datasetRoot.Path, "udx", "bldg", "area"));
        await File.WriteAllTextAsync(
            Path.Combine(datasetRoot.Path, "udx", "bldg", "area", "plateau_tokyo23ku_bldg_533944.gml"),
            "<CityModel />");
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(datasetRoot.Path);

        Assert.Throws<ArgumentException>(() => datasetSource.FileExists("../../outside.txt"));
        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.OpenReadAsync("../../outside.txt").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.MaterializeFileAsync("../../outside.txt", datasetRoot.Path));
    }

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
                ".dataset-cache",
                GetMaterializedArchiveCacheKey(archivePath),
                traversalPath.Replace('/', Path.DirectorySeparatorChar)));

        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.MaterializeFileAsync(traversalPath, outputRoot));
        Assert.False(File.Exists(expectedUnsafePath));
    }

    [Fact]
    public async Task ArchiveSourceRejectsTraversalPathsForReadAndExists()
    {
        byte[] archiveBytes = CreateZipArchive(
            ("udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"),
            ("../../outside.txt", "escape"));

        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "malicious.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(archivePath);

        Assert.Throws<ArgumentException>(() => datasetSource.FileExists("../../outside.txt"));
        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.OpenReadAsync("../../outside.txt").AsTask());
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
    public async Task LocalSourceRejectsSymlinkEscapePaths()
    {
        using TemporaryDirectory workRoot = new();
        string datasetRoot = Path.Combine(workRoot.Path, "dataset");
        string outsideRoot = Path.Combine(workRoot.Path, "outside");
        string linkParent = Path.Combine(datasetRoot, "udx", "bldg");
        Directory.CreateDirectory(linkParent);
        Directory.CreateDirectory(outsideRoot);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "secret.gml"), "<CityModel />");
        CreateDirectorySymbolicLinkOrSkip(Path.Combine(linkParent, "linked"), outsideRoot);

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(datasetRoot);

        Assert.Throws<ArgumentException>(() => datasetSource.FileExists("udx/bldg/linked/secret.gml"));
        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.OpenReadAsync("udx/bldg/linked/secret.gml").AsTask());
    }

    [Fact]
    public async Task MaterializeFileAsyncRejectsSymlinkedCacheRoots()
    {
        byte[] archiveBytes = CreateZipArchive(("udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml", "<CityModel />"));

        using TemporaryDirectory workRoot = new();
        string archivePath = Path.Combine(workRoot.Path, "dataset.zip");
        await File.WriteAllBytesAsync(archivePath, archiveBytes);
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(archivePath);

        string outputRoot = Path.Combine(workRoot.Path, "output");
        string externalCacheRoot = Path.Combine(workRoot.Path, "external-cache");
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(externalCacheRoot);
        CreateDirectorySymbolicLinkOrSkip(Path.Combine(outputRoot, ".dataset-cache"), externalCacheRoot);

        await Assert.ThrowsAsync<ArgumentException>(() => datasetSource.MaterializeFileAsync(
            "udx/bldg/area/plateau_tokyo23ku_bldg_533944.gml",
            outputRoot));
    }

    [Fact]
    public async Task LocalSourceEnumerateFilesIgnoresSymlinkedDirectories()
    {
        using TemporaryDirectory workRoot = new();
        string datasetRoot = Path.Combine(workRoot.Path, "dataset");
        string safeDirectory = Path.Combine(datasetRoot, "udx", "bldg", "area");
        string outsideRoot = Path.Combine(workRoot.Path, "outside");
        string linkedDirectory = Path.Combine(datasetRoot, "linked");
        Directory.CreateDirectory(safeDirectory);
        Directory.CreateDirectory(outsideRoot);
        await File.WriteAllTextAsync(Path.Combine(safeDirectory, "safe.gml"), "<CityModel />");
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "outside.gml"), "<CityModel />");
        CreateDirectorySymbolicLinkOrSkip(linkedDirectory, outsideRoot);

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(datasetRoot);

        Assert.Equal(["udx/bldg/area/safe.gml"], datasetSource.EnumerateFiles());
    }

    [Fact]
    public async Task CreateAsyncDoesNotResolveDatasetRootThroughSymlinkedDirectories()
    {
        using TemporaryDirectory workRoot = new();
        string sourceRoot = Path.Combine(workRoot.Path, "source");
        string outsideDatasetRoot = Path.Combine(workRoot.Path, "outside-dataset");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(outsideDatasetRoot, "udx", "bldg", "area"));
        await File.WriteAllTextAsync(
            Path.Combine(outsideDatasetRoot, "udx", "bldg", "area", "outside.gml"),
            "<CityModel />");
        CreateDirectorySymbolicLinkOrSkip(Path.Combine(sourceRoot, "linked-dataset"), outsideDatasetRoot);

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(sourceRoot);

        Assert.Equal(Path.GetFullPath(sourceRoot), datasetSource.SourcePath);
        Assert.Empty(datasetSource.EnumerateFiles());
    }

    [Fact]
    public async Task CreateAsyncRejectsSymlinkedDatasetRoot()
    {
        using TemporaryDirectory workRoot = new();
        string targetDatasetRoot = Path.Combine(workRoot.Path, "target-dataset");
        string linkedDatasetRoot = Path.Combine(workRoot.Path, "linked-dataset");
        Directory.CreateDirectory(Path.Combine(targetDatasetRoot, "udx", "bldg", "area"));
        CreateDirectorySymbolicLinkOrSkip(linkedDatasetRoot, targetDatasetRoot);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => PlateauDatasetContentSourceFactory.CreateAsync(linkedDatasetRoot));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("symbolic link or junction", StringComparison.Ordinal));
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

    private static void CreateDirectorySymbolicLinkOrSkip(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw SkipException.ForSkip($"Symbolic links are unavailable in this environment: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            throw SkipException.ForSkip($"Symbolic links are unavailable in this environment: {exception.Message}");
        }
        catch (IOException exception) when (!Directory.Exists(linkPath))
        {
            throw SkipException.ForSkip($"Symbolic links are unavailable in this environment: {exception.Message}");
        }
    }
}
