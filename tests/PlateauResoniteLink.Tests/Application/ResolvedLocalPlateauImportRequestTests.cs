using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ResolvedLocalPlateauImportRequestTests
{
    [Fact]
    public void CreateCarriesPackageNamesFromValidatedRequest()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            packageNames: ["bldg", "wtr"]);

        Assert.Equal(["bldg", "wtr"], request.PackageNames);
    }

    [Fact]
    public void CreateCarriesLocalDemTextureSource()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            demTextureLocalSourcePath: "C:\\tmp\\ortho.tif");

        Assert.Equal("C:\\tmp\\ortho.tif", request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void CreateRejectsDifferentResolvedLocalCityGmlSource()
    {
        ValidatedPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.CreateValidatedRequest(
            cityGmlLocalSourcePath: "C:\\tmp\\plateau-a");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("C:\\tmp\\plateau-b"),
                null));

        Assert.Equal("cityGmlSource", exception.ParamName);
    }

    [Fact]
    public void CreateRejectsDifferentResolvedLocalDemTextureSource()
    {
        ValidatedPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.CreateValidatedRequest(
            demTextureLocalSourcePath: "C:\\tmp\\ortho-a.tif");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("C:\\tmp\\plateau"),
                new ValidatedLocalDatasetLocation("C:\\tmp\\ortho-b.tif")));

        Assert.Equal("demTextureSource", exception.ParamName);
    }

    [Fact]
    public void CreateRejectsResolvedDemTextureSourceWhenNoneWasRequested()
    {
        ValidatedPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.CreateValidatedRequest();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("C:\\tmp\\plateau"),
                new ValidatedLocalDatasetLocation("C:\\tmp\\ortho.tif")));

        Assert.Equal("demTextureSource", exception.ParamName);
    }

    [Fact]
    public void CreateRejectsRemoteCityGmlSourceResolvedOutsideExpectedCachePath()
    {
        using TemporaryDirectory workRoot = new();
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(new Uri("https://example.test/source.zip"))));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("C:\\tmp\\other.zip"),
                null,
                workRoot.Path));

        Assert.Equal("cityGmlSource", exception.ParamName);
    }

    [Fact]
    public void CreateAcceptsRemoteCityGmlSourceResolvedToExpectedCachePath()
    {
        using TemporaryDirectory workRoot = new();
        Uri sourceUri = new("https://example.test/source.zip");
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(sourceUri)));
        string expectedSourcePath = RemoteDatasetResourceLayout.GetRemoteResourcePath(
            workRoot.Path,
            sourceUri,
            ResolvedLocalPlateauImportRequest.RemoteCityGmlResourcePrefix);

        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            request,
            new ValidatedLocalDatasetLocation(expectedSourcePath),
            null,
            workRoot.Path);

        Assert.Equal(expectedSourcePath, resolvedRequest.CityGmlLocalSourcePath);
    }

    [Fact]
    public void CreateRejectsRemoteDemTextureSourceResolvedOutsideExpectedCachePath()
    {
        using TemporaryDirectory workRoot = new();
        ValidatedPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.CreateValidatedRequest() with
        {
            DemTextureSource = new ValidatedRemoteDatasetLocation(new Uri("https://example.test/ortho.tif")),
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("C:\\tmp\\plateau"),
                new ValidatedLocalDatasetLocation("C:\\tmp\\other.tif"),
                workRoot.Path));

        Assert.Equal("demTextureSource", exception.ParamName);
    }
}
