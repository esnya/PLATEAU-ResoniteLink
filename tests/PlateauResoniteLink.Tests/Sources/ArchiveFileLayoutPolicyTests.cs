using System.Security.Cryptography;
using System.Text;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ArchiveFileLayoutPolicyTests
{
    [Theory]
    [InlineData("sample.zip")]
    [InlineData("sample.7z")]
    public void IsSupportedArchivePathAcceptsConfiguredExtensions(string path)
    {
        ArchiveFileLayoutPolicy policy = new();

        Assert.True(policy.IsSupportedArchivePath(path));
    }

    [Fact]
    public void GetLocalFileCacheKeyUsesFileStemAndFullPathDigest()
    {
        ArchiveFileLayoutPolicy policy = new();
        string archivePath = Path.Combine("C:\\datasets", "tokyo", "source-archive-a1b2c3.zip");

        string cacheKey = policy.GetLocalFileCacheKey(archivePath);

        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(archivePath))))
            .ToLowerInvariant();
        Assert.Equal($"source-archive-a1b2c3-{digest[..12]}", cacheKey);
    }

    [Fact]
    public void ResolveDatasetRootPrefixReturnsPrefixBeforeUdx()
    {
        ArchiveFileLayoutPolicy policy = new();

        string prefix = policy.ResolveDatasetRootPrefix(
        [
            "root-a/udx/bldg/533944/file.gml",
            "root-a/udx/dem/533944/file.gml",
            "other/path.txt",
        ]);

        Assert.Equal("root-a", prefix);
    }

    [Fact]
    public void StripDatasetRootPrefixRemovesDetectedPrefix()
    {
        ArchiveFileLayoutPolicy policy = new();

        string relativePath = policy.StripDatasetRootPrefix(
            "dataset-root/udx/bldg/533944/file.gml",
            "dataset-root");

        Assert.Equal("udx/bldg/533944/file.gml", relativePath);
    }

    [Fact]
    public void ResolveRelativePathRejectsTraversalOutsideArchiveRoot()
    {
        ArchiveFileLayoutPolicy policy = new();

        string? resolved = policy.ResolveRelativePath(
            "udx/bldg/533944/index.gml",
            "../../../../outside.txt");

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRelativePathAllowsTraversalWithinArchiveRoot()
    {
        ArchiveFileLayoutPolicy policy = new();

        string? resolved = policy.ResolveRelativePath(
            "udx/bldg/533944/index.gml",
            "../../textures/outside.txt");

        Assert.Equal("udx/textures/outside.txt", resolved);
    }
}
