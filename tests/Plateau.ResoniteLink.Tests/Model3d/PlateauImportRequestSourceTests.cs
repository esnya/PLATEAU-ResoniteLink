using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Domain;

public sealed class PlateauImportRequestSourceTests
{
    [Fact]
    public void SourceFactoriesCreateTypedSources()
    {
        PlateauImportSource localSource = PlateauImportSource.Local("/data/plateau");
        PlateauImportSource remoteSource = PlateauImportSource.Remote(new Uri("https://example.invalid/plateau.zip"));

        PlateauLocalImportSource typedLocalSource = Assert.IsType<PlateauLocalImportSource>(localSource);
        PlateauRemoteImportSource typedRemoteSource = Assert.IsType<PlateauRemoteImportSource>(remoteSource);

        Assert.Equal("/data/plateau", typedLocalSource.LocalSourcePath);
        Assert.Equal(DatasetSourceKind.Local, typedLocalSource.SourceKind);
        Assert.Equal(new Uri("https://example.invalid/plateau.zip"), typedRemoteSource.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, typedRemoteSource.SourceKind);
    }

    [Fact]
    public void RequestExposesOptionalDemTextureSource()
    {
        Uri orthoUri = new("https://example.invalid/53394525.tif");
        PlateauImportRequest request = new(
            "tokyo23ku",
            "53394525",
            PlateauImportSource.Remote(new Uri("https://example.invalid/plateau.zip")),
            DemTextureSource: PlateauImportSource.Remote(orthoUri));

        Assert.Equal(DatasetSourceKind.Remote, request.Source.SourceKind);
        Assert.Equal(DatasetSourceKind.Remote, request.DemTextureSourceKind);
        Assert.Equal(orthoUri, request.DemTextureServerUri);
    }

    [Fact]
    public void FromLegacyPreservesWindowsLocalPath()
    {
        const string localPath = @"C:\data\plateau\13213_higashimurayama-shi_2020_citygml_3_op.zip";

        PlateauImportSource source = PlateauImportSource.FromLegacy(
            DatasetSourceKind.Local,
            localPath,
            serverUri: null);

        PlateauLocalImportSource typedLocalSource = Assert.IsType<PlateauLocalImportSource>(source);
        Assert.Equal(localPath, typedLocalSource.LocalSourcePath);
    }

    [Fact]
    public void FromLegacyReconstructsRemoteSource()
    {
        Uri remoteUri = new("https://example.invalid/plateau.zip");

        PlateauImportSource source = PlateauImportSource.FromLegacy(
            DatasetSourceKind.Remote,
            localSourcePath: null,
            remoteUri);

        PlateauRemoteImportSource typedRemoteSource = Assert.IsType<PlateauRemoteImportSource>(source);
        Assert.Equal(remoteUri, typedRemoteSource.ServerUri);
    }
}
