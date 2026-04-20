using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

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
    public void LegacyConstructorMapsLocalSourceIntoTypedSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/data/plateau",
            ServerUri: null);

        PlateauLocalImportSource localSource = Assert.IsType<PlateauLocalImportSource>(request.Source);
        Assert.Equal("/data/plateau", localSource.LocalSourcePath);
        Assert.Equal(DatasetSourceKind.Local, request.Source.SourceKind);
        Assert.Equal("/data/plateau", request.LocalSourcePath);
        Assert.Null(request.ServerUri);
    }

    [Fact]
    public void TypedConstructorKeepsRemoteSourceExplicit()
    {
        Uri serverUri = new("https://example.invalid/plateau.zip");

        PlateauImportRequest request = new(
            "tokyo23ku",
            "53394525",
            new PlateauRemoteImportSource(serverUri));

        PlateauRemoteImportSource remoteSource = Assert.IsType<PlateauRemoteImportSource>(request.Source);
        Assert.Equal(serverUri, remoteSource.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, request.Source.SourceKind);
        Assert.Equal(serverUri, request.ServerUri);
        Assert.Null(request.LocalSourcePath);
    }
}
