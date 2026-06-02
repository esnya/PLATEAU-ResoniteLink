using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    public void CreateKeepsResolvedRemoteDemTextureSource()
    {
        string workRoot = "work";
        Uri cityGmlUri = new("https://example.test/tokyo.zip");
        Uri demTextureUri = new("https://example.test/ortho.tif");
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(cityGmlUri),
                DemTextureSource: DatasetLocation.Remote(demTextureUri)));

        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            request,
            workRoot,
            new ValidatedLocalDatasetLocation(
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, cityGmlUri, "source-archive")),
            new ValidatedLocalDatasetLocation(
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, demTextureUri, "source-ortho")));

        Assert.Equal(
            RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, demTextureUri, "source-ortho"),
            resolvedRequest.DemTextureLocalSourcePath);
    }

    [Fact]
    public void CreateRejectsLocalCityGmlSourceMismatch()
    {
        ValidatedPlateauImportRequest request = CreateValidatedRequest(
            new ValidatedLocalDatasetLocation("/tmp/plateau-a"));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                "work",
                new ValidatedLocalDatasetLocation("/tmp/plateau-b"),
                demTextureSource: null));
    }

    [Fact]
    public void CreateRejectsRemoteCityGmlSourceMismatch()
    {
        Uri cityGmlUri = new("https://example.test/tokyo.zip");
        ValidatedPlateauImportRequest request = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(cityGmlUri)));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                "work",
                new ValidatedLocalDatasetLocation("/tmp/unexpected.zip"),
                demTextureSource: null));
    }

    [Fact]
    public void CreateRejectsDifferentResolvedLocalDemTextureSource()
    {
        ValidatedPlateauImportRequest request = CreateValidatedRequest(
            new ValidatedLocalDatasetLocation("/tmp/plateau"),
            new ValidatedLocalDatasetLocation("/tmp/ortho-a.tif"));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                "work",
                new ValidatedLocalDatasetLocation("/tmp/plateau"),
                new ValidatedLocalDatasetLocation("/tmp/ortho-b.tif")));
    }

    [Fact]
    public void CreateRejectsResolvedDemTextureSourceWhenNoneWasRequested()
    {
        ValidatedPlateauImportRequest request = CreateValidatedRequest(
            new ValidatedLocalDatasetLocation("/tmp/plateau"));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                "work",
                new ValidatedLocalDatasetLocation("/tmp/plateau"),
                new ValidatedLocalDatasetLocation("/tmp/ortho.tif")));
    }

    [Fact]
    public void CreateRejectsUnresolvedDemTextureSourceWhenOneWasRequested()
    {
        ValidatedPlateauImportRequest request = CreateValidatedRequest(
            new ValidatedLocalDatasetLocation("/tmp/plateau"),
            new ValidatedLocalDatasetLocation("/tmp/ortho.tif"));

        Assert.Throws<ArgumentException>(
            () => ResolvedLocalPlateauImportRequest.Create(
                request,
                "work",
                new ValidatedLocalDatasetLocation("/tmp/plateau"),
                demTextureSource: null));
    }

    private static ValidatedPlateauImportRequest CreateValidatedRequest(
        ValidatedDatasetLocation cityGmlSource,
        ValidatedDatasetLocation? demTextureSource = null)
    {
        return new ValidatedPlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: cityGmlSource,
            DemTextureSource: demTextureSource);
    }
}
