using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class PlateauImportRequestValidatorTests
{
    [Fact]
    public void ValidateRequiresServerUrlForRemoteSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The --server-url value is required when --source remote is used.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsRemoteServerUrlThatIsNotDirectArchive()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: new Uri("https://example.invalid/dataset"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The --server-url value must point directly to a .zip or .7z CityGML archive over http or https.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void TryNormalizeAndValidateTrimsAndNormalizesRequestData()
    {
        using TemporaryDirectory sourceRoot = new();

        PlateauImportRequest request = new(
            Dataset: " tokyo23ku ",
            MeshCode: " 53394525 ",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: $"  {sourceRoot.Path}  ",
            ServerUri: null,
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
        Assert.DoesNotMatch(validatedRequest.MeshCodePattern, "53394526");
        Assert.IsType<ValidatedPlateauLocalImportSource>(validatedRequest.Source);
        Assert.Equal(sourceRoot.Path, validatedRequest.LocalSourcePath);
        Assert.Equal(["wtr", "tran"], validatedRequest.PackageNames);

        PlateauImportRequest roundTrippedRequest = validatedRequest.ToImportRequest();
        Assert.Equal("tokyo23ku", roundTrippedRequest.Dataset);
        Assert.Equal("53394525", roundTrippedRequest.MeshCode);
        Assert.Equal(sourceRoot.Path, roundTrippedRequest.LocalSourcePath);
        Assert.Equal(["wtr", "tran"], roundTrippedRequest.PackageNames);
    }

    [Fact]
    public void ValidateRejectsUnsupportedPackageNamesInPackageList()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null,
            PackageNames: ["bldg", "unknown", "waterbody"]);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => error.Contains(
                "Unsupported package name(s): unknown.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsUnsupportedPackageKeysInPackageMaps()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null,
            ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
            {
                ["unknown"] = new HashSet<int> { 1 },
            },
            PackagePatterns: new Dictionary<string, string>
            {
                ["another-unknown"] = "*Road*",
            });

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => error.Contains("Unsupported package name(s): unknown.", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains("Unsupported package name(s): another-unknown.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsDuplicateNormalizedPackageKeysInPackageMaps()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null,
            ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
            {
                ["tran"] = new HashSet<int> { 1 },
                [" TRAN "] = new HashSet<int> { 2 },
            },
            PackagePatterns: new Dictionary<string, string>
            {
                ["waterbody"] = "*Water*",
                ["wtr"] = "*River*",
            });

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => error.Contains(
                "The ExcludeLodLevelsByPackage value contains duplicate package keys after normalization: tran.",
                StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains(
                "The PackagePatterns value contains duplicate package keys after normalization: wtr.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRequiresLocalSourcePathForLocalSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: null,
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The --local-source-path value is required when --source local is used.",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateAcceptsExistingLocalSourcePath(bool createFile)
    {
        using TemporaryDirectory sourceRoot = new();
        string localSourcePath = createFile
            ? Path.Combine(sourceRoot.Path, "source.gml")
            : sourceRoot.Path;

        if (createFile)
        {
            File.WriteAllText(localSourcePath, "<CityModel />");
        }

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: localSourcePath,
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRejectsMissingLocalSourcePath()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/path/that/does/not/exist",
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The local source path '/path/that/does/not/exist' does not exist.",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void ValidateRejectsNonPositiveDemHeightmapMetersPerVertex(double metersPerVertex)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null,
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
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null,
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
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "C:/dataset",
            ServerUri: null);

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
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: new Uri("https://example.invalid/dataset.zip"));

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The mesh code value '53394825' is not a supported literal mesh code.",
                StringComparison.Ordinal));
    }
}
