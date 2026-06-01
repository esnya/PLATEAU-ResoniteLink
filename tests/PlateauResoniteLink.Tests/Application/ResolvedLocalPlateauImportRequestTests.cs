using System;
using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ResolvedLocalPlateauImportRequestTests
{
    [Fact]
    public void ConstructorTrimsResolvedBoundaryStrings()
    {
        ResolvedLocalPlateauImportRequest request = new(
            Dataset: " plateau-13213 ",
            MeshCode: " 53395325 ",
            CityGmlLocalSourcePath: " C:/data/source.zip ",
            DemTextureSource: new LocalDatasetLocation(" C:/data/ortho.7z "));

        Assert.Equal("plateau-13213", request.Dataset);
        Assert.Equal("53395325", request.MeshCode);
        Assert.Equal("C:/data/source.zip", request.CityGmlLocalSourcePath);
        Assert.Equal("C:/data/ortho.7z", request.DemTextureLocalSourcePath);
    }

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
    public void ConstructorNormalizesPackageOptionMapKeysAtResolvedBoundary()
    {
        ResolvedLocalPlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlLocalSourcePath: "/tmp/plateau",
            ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
            {
                [" BLDG "] = new HashSet<int> { 1 },
                ["waterbody"] = new HashSet<int> { 2 },
            },
            PackagePatterns: new Dictionary<string, string>
            {
                [" BLDG "] = "_bldg_",
                ["waterbody"] = "_wtr_",
            });

        Assert.Equal([1], request.ExcludeLodLevelsByPackage!["bldg"]);
        Assert.Equal([2], request.ExcludeLodLevelsByPackage!["wtr"]);
        Assert.Equal("_bldg_", request.PackagePatterns!["bldg"]);
        Assert.Equal("_wtr_", request.PackagePatterns!["wtr"]);
    }

    [Fact]
    public void ConstructorRejectsDuplicatePackageOptionMapKeysAfterNormalization()
    {
        Assert.Throws<ArgumentException>(
            () => new ResolvedLocalPlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlLocalSourcePath: "/tmp/plateau",
                ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
                {
                    [" BLDG "] = new HashSet<int> { 1 },
                    ["bldg"] = new HashSet<int> { 2 },
                }));

        Assert.Throws<ArgumentException>(
            () => new ResolvedLocalPlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlLocalSourcePath: "/tmp/plateau",
                PackagePatterns: new Dictionary<string, string>
                {
                    ["waterbody"] = "_wtr_",
                    ["wtr"] = "_wtr_",
                }));
    }

    [Fact]
    public void ConstructorKeepsLocalDemTextureSource()
    {
        ResolvedLocalPlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlLocalSourcePath: "/tmp/plateau",
            DemTextureSource: new LocalDatasetLocation("/tmp/ortho.tif"));

        LocalDatasetLocation demTextureSource = Assert.IsType<LocalDatasetLocation>(request.DemTextureSource);
        Assert.Equal("/tmp/ortho.tif", demTextureSource.LocalSourcePath);
    }

    [Fact]
    public void CreateKeepsLocalDemTextureSource()
    {
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(new Uri("https://example.test/tokyo.zip")),
                DemTextureSource: DatasetLocation.Remote(new Uri("https://example.test/ortho.tif"))));

        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            request,
            new ValidatedLocalDatasetLocation("/tmp/plateau"),
            new ValidatedLocalDatasetLocation("/tmp/ortho.tif"));

        Assert.Equal("/tmp/ortho.tif", resolvedRequest.DemTextureLocalSourcePath);
    }
}
