using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class RemoteArchiveDistributionPolicyTests
{
    [Fact]
    public void GetSourceArchivePathUsesStableSourceArchiveStem()
    {
        RemoteArchiveDistributionPolicy policy = new();
        Uri archiveUri = new("https://example.test/plateau/tokyo23ku.zip", UriKind.Absolute);
        string datasetRoot = Path.Combine("work", "tokyo23ku");

        string archivePath = policy.GetSourceArchivePath(datasetRoot, archiveUri, "tokyo23ku.zip");

        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(archiveUri.AbsoluteUri)))
            .ToLowerInvariant();
        Assert.Equal(
            Path.Combine(Path.GetFullPath(datasetRoot), $"source-archive-{digest[..12]}.zip"),
            archivePath);
    }

    [Fact]
    public void GetSourceArchiveMetadataPathPlacesMetadataNextToArchive()
    {
        RemoteArchiveDistributionPolicy policy = new();
        string archivePath = Path.Combine("work", "tokyo23ku", "source-archive-abcd1234.zip");

        string metadataPath = policy.GetSourceArchiveMetadataPath(archivePath);

        Assert.Equal(
            $"{archivePath}.meta.json",
            metadataPath);
    }

    [Theory]
    [InlineData("sample.zip")]
    [InlineData("sample.7z")]
    public void IsSupportedArchivePathAcceptsConfiguredExtensions(string path)
    {
        RemoteArchiveDistributionPolicy policy = new();

        Assert.True(policy.IsSupportedArchivePath(path));
    }

    [Fact]
    public void GetSourceArchivePathRejectsUnsupportedArchiveExtension()
    {
        RemoteArchiveDistributionPolicy policy = new();

        Assert.Throws<PlateauImportValidationException>(
            () => policy.GetSourceArchivePath(
                "C:\\work\\tokyo23ku",
                new Uri("https://example.test/tokyo23ku.rar", UriKind.Absolute),
                "tokyo23ku.rar"));
    }
}
