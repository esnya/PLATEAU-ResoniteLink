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

    [Theory]
    [InlineData("/data/plateau", DatasetSourceKind.Local)]
    [InlineData("https://example.invalid/plateau.zip", DatasetSourceKind.Remote)]
    public void FromInputParsesSourceKind(string sourceInput, DatasetSourceKind expectedKind)
    {
        PlateauImportSource source = PlateauImportSource.FromInput(sourceInput);

        Assert.Equal(expectedKind, source.SourceKind);
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
}
