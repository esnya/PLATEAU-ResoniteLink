using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests;

public sealed class PublicApiGuardClauseTests
{
    [Fact]
    public void PlateauImportServiceConstructorRejectsNullSceneBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => new PlateauImportService(null!));
    }

    [Fact]
    public void PlateauImportServiceOwnedFactoryRejectsNullSceneBuilder()
    {
        Assert.Throws<ArgumentNullException>(() => PlateauImportService.CreateOwned(null!));
    }

    [Fact]
    public async Task CliApplicationImportServiceConstructorRejectsNullDependencies()
    {
        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService importService = new(sceneBuilder);

        Assert.Throws<ArgumentNullException>(() => new CliApplication(null!, TextWriter.Null, importService));
        Assert.Throws<ArgumentNullException>(() => new CliApplication(TextWriter.Null, null!, importService));
        Assert.Throws<ArgumentNullException>(() => new CliApplication(TextWriter.Null, TextWriter.Null, (PlateauImportService)null!));
    }

    [Fact]
    public async Task CliApplicationFactoryConstructorRejectsNullDependencies()
    {
        await using StubSceneBuilder sceneBuilder = new();
        Func<BuildCommandOptions, PlateauImportService> factory = _ => new PlateauImportService(sceneBuilder);

        Assert.Throws<ArgumentNullException>(() => new CliApplication(null!, TextWriter.Null, factory));
        Assert.Throws<ArgumentNullException>(() => new CliApplication(TextWriter.Null, null!, factory));
        Assert.Throws<ArgumentNullException>(() => new CliApplication(TextWriter.Null, TextWriter.Null, (Func<BuildCommandOptions, PlateauImportService>)null!));
    }

    [Fact]
    public void ResoniteLinkSceneBuilderConstructorRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ResoniteLinkSceneBuilder(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), 0));
    }

    [Fact]
    public void ImportExecutionResultCopiesDestinations()
    {
        List<string> destinations = ["stub://resonite"];
        ResoniteConstructionMetadata metadata = new(
            "1.0",
            "world",
            new PlateauImportRequest("dataset", "53394525", new PlateauLocalImportSource("/tmp/source")),
            new PlateauSourceDataset(["bldg"], ["/tmp/source"], []),
            new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(false, "credit", "license", "https://example.invalid/license"),
                []),
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));

        ImportExecutionResult result = new(metadata, destinations);
        destinations.Add("mutated");

        Assert.Single(result.Destinations);
    }

    [Fact]
    public void ImportExecutionResultSupportsDeconstruction()
    {
        ResoniteConstructionMetadata metadata = new(
            "1.0",
            "world",
            new PlateauImportRequest("dataset", "53394525", new PlateauLocalImportSource("/tmp/source")),
            new PlateauSourceDataset(["bldg"], ["/tmp/source"], []),
            new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(false, "credit", "license", "https://example.invalid/license"),
                []),
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));
        ImportExecutionResult result = new(metadata, ["stub://resonite"]);

        (ResoniteConstructionMetadata deconstructedMetadata, IReadOnlyList<string> destinations) = result;

        Assert.Same(metadata, deconstructedMetadata);
        Assert.Equal(["stub://resonite"], destinations);
    }

    [Fact]
    public void PlateauImportValidationExceptionSupportsStandardConstructors()
    {
        PlateauImportValidationException defaultException = new();
        PlateauImportValidationException messageException = new("message");
        InvalidOperationException inner = new("inner");
        PlateauImportValidationException innerException = new("message", inner);

        Assert.Empty(defaultException.Errors);
        Assert.Equal("message", messageException.Message);
        Assert.Same(inner, innerException.InnerException);
    }

    [Fact]
    public void PlateauImportRequestDefensivelyCopiesCollectionsAndValidatesTerrainOptions()
    {
        List<string> packageNames = ["bldg"];
        HashSet<int> excludedLods = [1];
        Dictionary<string, IReadOnlySet<int>> excludedLodsByPackage = new()
        {
            ["dem"] = new HashSet<int> { 2 },
        };
        Dictionary<string, string> packagePatterns = new()
        {
            ["dem"] = "*Terrain*",
        };

        PlateauImportRequest request = new(
            "dataset",
            "53394525",
            new PlateauLocalImportSource("/tmp/source"),
            packageNames,
            excludedLods,
            excludedLodsByPackage,
            packagePatterns);

        packageNames.Add("tran");
        excludedLods.Add(3);
        ((HashSet<int>)excludedLodsByPackage["dem"]).Add(4);
        packagePatterns["dem"] = "*Changed*";

        Assert.Equal(["bldg"], request.PackageNames);
        Assert.Equal([1], request.GlobalExcludeLodLevels);
        Assert.Equal([2], request.ExcludeLodLevelsByPackage!["dem"]);
        Assert.Equal("*Terrain*", request.PackagePatterns!["dem"]);

        List<string> replacementPackageNames = ["tran"];
        HashSet<int> replacementGlobalExclude = [4];
        Dictionary<string, IReadOnlySet<int>> replacementExcludedByPackage = new()
        {
            ["tran"] = new HashSet<int> { 5 },
        };
        Dictionary<string, string> replacementPatterns = new()
        {
            ["tran"] = "*Road*",
        };
        PlateauImportRequest replaced = request with
        {
            PackageNames = replacementPackageNames,
            GlobalExcludeLodLevels = replacementGlobalExclude,
            ExcludeLodLevelsByPackage = replacementExcludedByPackage,
            PackagePatterns = replacementPatterns,
        };
        replacementPackageNames.Add("dem");
        replacementGlobalExclude.Add(6);
        ((HashSet<int>)replacementExcludedByPackage["tran"]).Add(7);
        replacementPatterns["tran"] = "*ChangedRoad*";

        Assert.Equal(["tran"], replaced.PackageNames);
        Assert.Equal([4], replaced.GlobalExcludeLodLevels);
        Assert.Equal([5], replaced.ExcludeLodLevelsByPackage!["tran"]);
        Assert.Equal("*Road*", replaced.PackagePatterns!["tran"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => request with
        {
            DemTerrainMode = (DemTerrainMode)int.MaxValue,
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => request with
        {
            DemHeightmapMetersPerVertex = 0.0,
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => request with
        {
            DemHeightmapMaxResolution = 1,
        });
    }

    [Fact]
    public void PublicDomainModelsDefensivelyCopyCollections()
    {
        List<int> triangleIndices = [0, 1, 2];
        ResoniteMeshSubmesh submesh = new(0, "material", triangleIndices);
        triangleIndices.Add(3);

        List<ResoniteMeshVertex> vertices =
        [
            new(
                new ResoniteFloat3(0.0, 0.0, 0.0),
                new ResoniteFloat3(0.0, 1.0, 0.0),
                new ResoniteFloat2(0.0, 0.0),
                new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
            new(
                new ResoniteFloat3(1.0, 0.0, 0.0),
                new ResoniteFloat3(0.0, 1.0, 0.0),
                new ResoniteFloat2(1.0, 0.0),
                new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
            new(
                new ResoniteFloat3(0.0, 0.0, 1.0),
                new ResoniteFloat3(0.0, 1.0, 0.0),
                new ResoniteFloat2(0.0, 1.0),
                new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
        ];
        List<ResoniteMeshSubmesh> submeshes = [submesh];
        ResoniteImportedMesh mesh = new(vertices, submeshes);
        vertices.Clear();
        submeshes.Clear();

        List<ResoniteMaterialBinding> materials =
        [
            new(
                "material",
                new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                ResoniteMaterialType.Standard,
                null,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                null,
                [0]),
        ];
        ResoniteConstructionCityObject cityObject = new(
            "slot",
            "display",
            "bldg",
            "53394525",
            2,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            mesh,
            materials);
        materials.Clear();

        Assert.Equal(3, submesh.TriangleVertexIndices.Count);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Single(mesh.Submeshes);
        Assert.Single(cityObject.Materials);
    }

    [Fact]
    public void PublicDomainModelsRejectInvalidGeometryAndTriangleIndices()
    {
        Assert.Throws<ArgumentException>(() => new ResoniteMeshSubmesh(0, "material", [0, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResoniteMeshSubmesh(0, "material", [0, -1, 2]));
        Assert.Throws<ArgumentException>(() => new ResoniteMaterialBinding(
            "material",
            new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            ResoniteMaterialType.Standard,
            null,
            ResoniteTextureSourceKind.Dataset,
            ResoniteMaterialProjection.Uv,
            null,
            []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResoniteMaterialBinding(
            "material",
            new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            ResoniteMaterialType.Standard,
            null,
            ResoniteTextureSourceKind.Dataset,
            ResoniteMaterialProjection.Uv,
            null,
            [-1]));
        Assert.Throws<ArgumentException>(() => new ResoniteMaterialBinding(
            "material",
            new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            ResoniteMaterialType.Standard,
            null,
            ResoniteTextureSourceKind.Dataset,
            ResoniteMaterialProjection.Uv,
            null,
            [0, 0]));

        ResoniteImportedMesh validMesh = new(
            [
                new(
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0),
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
                new(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0),
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
                new(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0),
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0)),
            ],
            [
                new ResoniteMeshSubmesh(0, "material", [0, 1, 2]),
            ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => validMesh with
        {
            Submeshes = [new ResoniteMeshSubmesh(0, "material", [0, 1, 3])],
        });
        Assert.Throws<ArgumentException>(() => validMesh with
        {
            Submeshes =
            [
                new ResoniteMeshSubmesh(0, "material", [0, 1, 2]),
                new ResoniteMeshSubmesh(2, "material", [0, 1, 2]),
            ],
        });

        ResoniteHeightMapGridGeometry heightMap = new(
            2,
            2,
            new ResoniteFloat2(1.0, 1.0),
            0.0,
            1.0,
            [0.0, 0.1, 0.2, 0.3]);

        Assert.Throws<ArgumentException>(() => heightMap with
        {
            HeightSamples = [],
        });
    }

    private sealed class StubSceneBuilder : IResoniteSceneBuilder
    {
        public Task EnsureConnectedAsync(PlateauImportRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BeginAsync(
            ResoniteConstructionMetadata metadata,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ProcessCityObjectAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
