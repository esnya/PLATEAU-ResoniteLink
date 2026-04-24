using System;
using System.Collections.Generic;
using System.IO;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class PlateauImportRequestValidatorTests
{
    [Fact]
    public void ValidateRequiresCityGmlSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The --citygml-source value is required.", errors);
    }

    [Fact]
    public void ValidateRejectsRemoteCityGmlSourceThatIsNotDirectArchive()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: new Uri("https://example.invalid/dataset"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --citygml-source value must point directly to a .zip or .7z CityGML archive over http or https.",
            errors);
    }

    [Fact]
    public void ValidateRejectsRemoteGeoTiffSourceThatIsNotDirectArchiveOrRaster()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("C:/dataset"),
            DemTextureSource: DatasetLocation.Remote(new Uri("https://example.invalid/ortho.png")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --geotiff-source value must point directly to a .tif, .tiff, .zip, or .7z resource over http or https.",
            errors);
    }

    [Fact]
    public void ValidateRejectsLocalCityGmlSourceThatIsOnlyTerrainRaster()
    {
        using TemporaryDirectory sourceRoot = new();
        string rasterPath = Path.Combine(sourceRoot.Path, "ortho.tif");
        File.WriteAllBytes(rasterPath, [0x00]);

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(rasterPath));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            $"The CityGML source path '{rasterPath}' must be a dataset directory or a .zip/.7z archive.",
            errors);
    }

    [Fact]
    public void TryNormalizeAndValidateTrimsAndNormalizesRequestData()
    {
        using TemporaryDirectory sourceRoot = new();
        string geoTiffPath = Path.Combine(sourceRoot.Path, "53394525.tif");
        File.WriteAllText(geoTiffPath, "dummy");

        PlateauImportRequest request = new(
            Dataset: " tokyo23ku ",
            MeshCode: " 53394525 ",
            Source: DatasetLocation.Local($"  {sourceRoot.Path}  "),
            DemTextureSource: DatasetLocation.Local($"  {geoTiffPath}  "),
            PackageNames: [" waterbody ", " tran "]);

        bool success = PlateauImportRequestValidator.TryNormalizeAndValidate(
            request,
            out ValidatedPlateauImportRequest? validatedRequest,
            out IReadOnlyList<string> errors);

        Assert.True(success);
        Assert.Empty(errors);
        Assert.NotNull(validatedRequest);
        Assert.Equal("tokyo23ku", validatedRequest!.Dataset);
        Assert.Equal("53394525", validatedRequest.MeshCode);
        Assert.Matches(validatedRequest.MeshCodePattern, "53394525");
        Assert.IsType<ValidatedLocalDatasetLocation>(validatedRequest.Source);
        Assert.Equal(sourceRoot.Path, validatedRequest.LocalSourcePath);
        Assert.Equal(geoTiffPath, validatedRequest.DemTextureLocalSourcePath);
        Assert.Equal(["wtr", "tran"], validatedRequest.PackageNames);
    }

    [Fact]
    public void TryNormalizeAndValidatePreservesDynamicTerrainMeshMode()
    {
        using TemporaryDirectory sourceRoot = new();
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(sourceRoot.Path),
            TerrainMeshMode: TerrainMeshMode.Dynamic);

        bool success = PlateauImportRequestValidator.TryNormalizeAndValidate(
            request,
            out ValidatedPlateauImportRequest? validatedRequest,
            out IReadOnlyList<string> errors);

        Assert.True(success);
        Assert.Empty(errors);
        Assert.NotNull(validatedRequest);
        Assert.Equal(TerrainMeshMode.Dynamic, validatedRequest!.TerrainMeshMode);
    }

    [Fact]
    public void ValidateRejectsGeoTiffSourceDirectory()
    {
        using TemporaryDirectory sourceRoot = new();
        using TemporaryDirectory geoTiffRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(sourceRoot.Path),
            DemTextureSource: DatasetLocation.Local(geoTiffRoot.Path));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            $"The GeoTIFF source path '{geoTiffRoot.Path}' must point to an existing file.",
            errors);
    }

    [Fact]
    public void ValidateRequiresExistingLocalCityGmlSourcePath()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("/path/that/does/not/exist"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The CityGML source path '/path/that/does/not/exist' does not exist.", errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void ValidateRejectsNonPositiveTerrainGridMetersPerVertex(double metersPerVertex)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("C:/dataset"),
            TerrainGridMetersPerVertex: metersPerVertex);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The terrain grid meters-per-vertex value must be greater than zero.", errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-4)]
    public void ValidateRejectsTerrainGridMaxResolutionBelowTwo(int maxResolution)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("C:/dataset"),
            TerrainGridMaxResolution: maxResolution);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The terrain grid max resolution value must be at least 2.", errors);
    }
}
