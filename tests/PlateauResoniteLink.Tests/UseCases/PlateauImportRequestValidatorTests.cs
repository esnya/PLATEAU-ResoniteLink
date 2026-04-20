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
    public void ValidateRejectsRemoteOrthoSourceThatIsNotDirectArchiveOrRaster()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("C:/dataset"),
            DemTextureSource: PlateauImportSource.Remote(new Uri("https://example.invalid/ortho.png")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --ortho-source value must point directly to a .zip/.7z archive or .tif/.tiff raster over http or https.",
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
            Source: PlateauImportSource.Local(rasterPath));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --citygml-source value must point to a .zip/.7z archive or directory containing extracted CityGML dataset contents.",
            errors);
    }

    [Fact]
    public void TryNormalizeAndValidateTrimsAndNormalizesRequestData()
    {
        using TemporaryDirectory sourceRoot = new();
        using TemporaryDirectory orthoRoot = new();

        PlateauImportRequest request = new(
            Dataset: " tokyo23ku ",
            MeshCode: " 53394525 ",
            Source: PlateauImportSource.Local($"  {sourceRoot.Path}  "),
            DemTextureSource: PlateauImportSource.Local($"  {orthoRoot.Path}  "),
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
        Assert.IsType<ValidatedPlateauLocalImportSource>(validatedRequest.Source);
        Assert.Equal(sourceRoot.Path, validatedRequest.LocalSourcePath);
        Assert.Equal(orthoRoot.Path, validatedRequest.DemTextureLocalSourcePath);
        Assert.Equal(["wtr", "tran"], validatedRequest.PackageNames);
    }

    [Fact]
    public void ValidateRequiresExistingLocalCityGmlSourcePath()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("/path/that/does/not/exist"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The local source path '/path/that/does/not/exist' does not exist.", errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void ValidateRejectsNonPositiveDemHeightmapMetersPerVertex(double metersPerVertex)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("C:/dataset"),
            DemHeightmapMetersPerVertex: metersPerVertex);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The DEM heightmap meters-per-vertex value must be greater than zero.", errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-4)]
    public void ValidateRejectsDemHeightmapMaxResolutionBelowTwo(int maxResolution)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("C:/dataset"),
            DemHeightmapMaxResolution: maxResolution);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The DEM heightmap max resolution value must be at least 2.", errors);
    }
}
