using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class PlateauImportServiceTests
{
    [Fact]
    public async Task ExecuteAsyncBuildsNormalizedPlan()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: " tokyo23ku ",
                MeshCode: " 53394525 ",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null),
            outputRoot: "artifacts/resonite");

        Assert.Equal("PLATEAU tokyo23ku 53394525", result.Plan.WorldName);
        Assert.Equal("tokyo23ku", result.Plan.Request.Dataset);
        Assert.Equal("53394525", result.Plan.Request.MeshCode);
        Assert.Equal("bldg", result.Plan.SourceDataset.PackageName);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Plan.SourceDataset.SourceFiles);
        Assert.Equal(2, result.Plan.Buildings.Count);
        ResoniteConstructionBuilding buildingOne = Assert.Single(
            result.Plan.Buildings,
            static building => building.DisplayName == "Building One");
        Assert.Equal(2, buildingOne.Materials.Count);
        Assert.Contains(
            buildingOne.Materials,
            static material => material.TexturePath == "udx/bldg/53394525/appearance/roof.png");
        Assert.Equal("stub://artifact", Assert.Single(result.Destinations));
    }

    [Fact]
    public async Task ExecuteAsyncRejectsLocalSourceWithoutInput()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    InputPath: null,
                    ServerUri: null),
                outputRoot: "artifacts/resonite"));

        Assert.Contains(
            "The --input value is required when --source local is used.",
            exception.Errors);
    }

    [Fact]
    public async Task ExecuteAsyncFallsBackToColorWhenTextureFileIsMissing()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMissingTexture");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null),
            outputRoot: "artifacts/resonite");

        ResoniteConstructionBuilding building = Assert.Single(result.Plan.Buildings);
        ResoniteMaterialBinding material = Assert.Single(building.Materials);
        Assert.Null(material.TexturePath);
        Assert.Equal(0.52, material.BaseColor.R, 6);
        Assert.Equal(0.62, material.BaseColor.G, 6);
        Assert.Equal(0.72, material.BaseColor.B, 6);
    }

    [Fact]
    public async Task ExecuteAsyncProducesDeterministicJsonArtifact()
    {
        using TemporaryDirectory outputRootA = new();
        using TemporaryDirectory outputRootB = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        PlateauImportService service = new(new JsonArtifactResoniteSceneBuilder());

        ImportExecutionResult firstResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null),
            outputRootA.Path);
        ImportExecutionResult secondResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null),
            outputRootB.Path);

        string firstArtifact = Assert.Single(firstResult.Destinations);
        string secondArtifact = Assert.Single(secondResult.Destinations);

        Assert.Equal(
            await File.ReadAllTextAsync(firstArtifact),
            await File.ReadAllTextAsync(secondArtifact));
    }

    [Fact]
    public async Task ExecuteAsyncResolvesServerSourceBeforeBuildingPlan()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        RecordingDatasetSourceResolver resolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));
        PlateauImportService service = new(sceneBuilder, resolver);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Server,
                InputPath: null,
                ServerUri: new Uri("https://search.ckan.jp/backend/api/", UriKind.Absolute)),
            outputRoot: "artifacts/resonite");

        Assert.Equal(DatasetSourceKind.Server, Assert.Single(resolver.Requests).SourceKind);
        Assert.Equal(DatasetSourceKind.Local, result.Plan.Request.SourceKind);
        Assert.Equal(fixturePath, result.Plan.Request.InputPath);
    }

    private sealed class StubResoniteSceneBuilder : IResoniteSceneBuilder
    {
        public Task<IReadOnlyList<string>> BuildAsync(
            ResoniteConstructionPlan plan,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
            return Task.FromResult<IReadOnlyList<string>>(["stub://artifact"]);
        }
    }

    private sealed class RecordingDatasetSourceResolver(PlateauImportRequest resolvedRequest) : IPlateauDatasetSourceResolver
    {
        public List<PlateauImportRequest> Requests { get; } = [];

        public Task<PlateauImportRequest> ResolveAsync(
            PlateauImportRequest request,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(resolvedRequest);
        }
    }
}
