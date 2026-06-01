using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ResolvedLocalPlateauImportRequestTests
{
    [Fact]
    public void ConstructorNormalizesPackageNamesAtResolvedBoundary()
    {
        ResolvedLocalPlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlLocalSourcePath: "/tmp/plateau",
            PackageNames: [" BLDG ", "waterbody", "bldg"]);

        Assert.Equal(["bldg", "wtr"], request.PackageNames);
    }

    [Fact]
    public void ConstructorRejectsEmptyPackageNames()
    {
        Assert.Throws<ArgumentException>(
            () => new ResolvedLocalPlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlLocalSourcePath: "/tmp/plateau",
                PackageNames: []));
    }

    [Fact]
    public void ConstructorRejectsUnsupportedPackageNames()
    {
        Assert.Throws<ArgumentException>(
            () => new ResolvedLocalPlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlLocalSourcePath: "/tmp/plateau",
                PackageNames: ["unknown"]));
    }

    [Fact]
    public void ConstructorRejectsRemoteDemTextureSource()
    {
        Assert.Throws<ArgumentException>(
            () => new ResolvedLocalPlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlLocalSourcePath: "/tmp/plateau",
                DemTextureSource: DatasetLocation.Remote(new Uri("https://example.test/ortho.tif"))));
    }

    [Fact]
    public void CreateRejectsRemoteDemTextureSource()
    {
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(new Uri("https://example.test/tokyo.zip"))));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                new ValidatedLocalDatasetLocation("/tmp/plateau"),
                new ValidatedRemoteDatasetLocation(new Uri("https://example.test/ortho.tif"))));
    }
}
