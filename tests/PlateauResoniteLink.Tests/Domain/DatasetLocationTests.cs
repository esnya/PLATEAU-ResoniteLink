
using System;

using PlateauResoniteLink.Core.Domain.Importing;

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
    public void RequestStoresLocalSourceAsTypedSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/data/plateau"));

        LocalDatasetLocation localSource = Assert.IsType<LocalDatasetLocation>(request.CityGmlSource);
        Assert.Equal("/data/plateau", localSource.LocalSourcePath);
        Assert.Equal(DatasetSourceKind.Local, request.CityGmlSource.SourceKind);
        Assert.Equal("/data/plateau", request.CityGmlLocalSourcePath);
        Assert.Null(request.CityGmlServerUri);
    }

    [Fact]
    public void TypedConstructorKeepsRemoteSourceExplicit()
    {
        Uri serverUri = new("https://example.invalid/plateau.zip");

        PlateauImportRequest request = new(
            "tokyo23ku",
            "53394525",
            new RemoteDatasetLocation(serverUri));

        RemoteDatasetLocation remoteSource = Assert.IsType<RemoteDatasetLocation>(request.CityGmlSource);
        Assert.Equal(serverUri, remoteSource.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, request.CityGmlSource.SourceKind);
        Assert.Equal(serverUri, request.CityGmlServerUri);
        Assert.Null(request.CityGmlLocalSourcePath);
    }
}
