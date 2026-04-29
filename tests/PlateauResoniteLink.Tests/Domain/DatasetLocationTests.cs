using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

public sealed class DatasetLocationTests
{
    [Fact]
    public void SourceFactoriesCreateTypedSources()
    {
        DatasetLocation localSource = DatasetLocation.Local("/data/plateau");
        DatasetLocation remoteSource = DatasetLocation.Remote(new Uri("https://example.invalid/plateau.zip"));

        LocalDatasetLocation typedLocalSource = Assert.IsType<LocalDatasetLocation>(localSource);
        RemoteDatasetLocation typedRemoteSource = Assert.IsType<RemoteDatasetLocation>(remoteSource);

        Assert.Equal("/data/plateau", typedLocalSource.LocalSourcePath);
        Assert.Equal(DatasetSourceKind.Local, typedLocalSource.SourceKind);
        Assert.Equal(new Uri("https://example.invalid/plateau.zip"), typedRemoteSource.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, typedRemoteSource.SourceKind);
    }

    [Fact]
    public void CompatibilityConstructorMapsLocalSourceIntoTypedSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/data/plateau",
            ServerUri: null);

        LocalDatasetLocation localSource = Assert.IsType<LocalDatasetLocation>(request.Source);
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
            new RemoteDatasetLocation(serverUri));

        RemoteDatasetLocation remoteSource = Assert.IsType<RemoteDatasetLocation>(request.Source);
        Assert.Equal(serverUri, remoteSource.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, request.Source.SourceKind);
        Assert.Equal(serverUri, request.ServerUri);
        Assert.Null(request.LocalSourcePath);
    }
}
