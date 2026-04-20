using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class PlateauImportRequestValidatorTests
{
    [Fact]
    public void ValidateRequiresCityGmlSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(null));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains("The --citygml-source value is required.", errors);
    }

    [Fact]
    public void ValidateRejectsRemoteCityGmlSourceThatIsNotDirectArchive()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Remote(new Uri("https://example.invalid/dataset")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --citygml-source value must point directly to a .zip or .7z CityGML archive over http or https.",
            errors);
    }

    [Fact]
    public void TryNormalizeAndValidateTrimsAndNormalizesRequestData()
    {
        using TemporaryDirectory sourceRoot = new();

        PlateauImportRequest request = new(
            Dataset: " tokyo23ku ",
            MeshCode: " 53394525 ",
            Source: PlateauImportSource.Local($"  {sourceRoot.Path}  "),
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
        Assert.IsType<ValidatedPlateauLocalImportSource>(validatedRequest.Source);
        Assert.Equal(sourceRoot.Path, validatedRequest.LocalSourcePath);
        Assert.Equal(["wtr", "tran"], validatedRequest.PackageNames);
    }

    [Fact]
    public void ValidateRejectsUnsupportedPackageNamesInPackageList()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("C:/dataset"),
            PackageNames: ["bldg", "unknown", "waterbody"]);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => error.Contains("Unsupported package name(s): unknown.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRequiresExistingLocalCityGmlSourcePath()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("/path/that/does/not/exist"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The CityGML source path '/path/that/does/not/exist' does not exist.",
            errors);
    }

    [Fact]
    public void ValidateRequiresSupportedLocalCityGmlSourceExtension()
    {
        using TemporaryDirectory sourceRoot = new();
        string unsupportedPath = Path.Combine(sourceRoot.Path, "source.tif");
        File.WriteAllText(unsupportedPath, "dummy");

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(unsupportedPath));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            $"The CityGML source path '{unsupportedPath}' must be a dataset directory or a .zip/.7z archive.",
            errors);
    }

    [Fact]
    public void ValidateAcceptsRemoteGeoTiffUrl()
    {
        using TemporaryDirectory sourceRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(sourceRoot.Path),
            DemTextureSource: PlateauImportSource.Remote(new Uri("https://example.invalid/53394525.tif")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRejectsUnsupportedRemoteGeoTiffSource()
    {
        using TemporaryDirectory sourceRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(sourceRoot.Path),
            DemTextureSource: PlateauImportSource.Remote(new Uri("https://example.invalid/53394525.png")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            "The --geotiff-source value must point directly to a .tif, .tiff, .zip, or .7z resource over http or https.",
            errors);
    }

    [Fact]
    public void ValidateAcceptsExistingLocalGeoTiffFile()
    {
        using TemporaryDirectory sourceRoot = new();
        string geoTiffPath = Path.Combine(sourceRoot.Path, "53394525.tif");
        File.WriteAllText(geoTiffPath, "dummy");

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(sourceRoot.Path),
            DemTextureSource: PlateauImportSource.Local(geoTiffPath));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRejectsGeoTiffSourceDirectory()
    {
        using TemporaryDirectory sourceRoot = new();
        using TemporaryDirectory geoTiffRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(sourceRoot.Path),
            DemTextureSource: PlateauImportSource.Local(geoTiffRoot.Path));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            $"The GeoTIFF source path '{geoTiffRoot.Path}' must point to an existing file.",
            errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateAcceptsExistingLocalSourcePath(bool createFile)
    {
        using TemporaryDirectory sourceRoot = new();
        string localSourcePath = createFile
            ? Path.Combine(sourceRoot.Path, "source.zip")
            : sourceRoot.Path;

        if (createFile)
        {
            File.WriteAllText(localSourcePath, "<CityModel />");
        }

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(localSourcePath));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Empty(errors);
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

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The DEM heightmap meters-per-vertex value must be greater than zero.",
                StringComparison.Ordinal));
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

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The DEM heightmap max resolution value must be at least 2.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsInvalidMeshCodeRegex()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "[53394525",
            Source: PlateauImportSource.Local("C:/dataset"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => error.StartsWith(
                "The mesh code value '[53394525' is not a valid regular expression:",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsUnsupportedNumericMeshCode()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394825",
            Source: PlateauImportSource.Remote(new Uri("https://example.invalid/dataset.zip")));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The mesh code value '53394825' is not a supported literal mesh code.",
                StringComparison.Ordinal));
    }
}
