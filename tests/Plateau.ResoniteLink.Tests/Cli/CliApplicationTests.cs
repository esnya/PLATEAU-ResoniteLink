using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The CLI test hands builder ownership to PlateauImportService.")]
public sealed class CliApplicationTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateImportService(ISceneImportTarget sceneBuilder)
    {
        return new PlateauImportService(
            sceneBuilder,
            new CkanPlateauDatasetSourceResolver(SharedDatasetSourceResolverHttpClient),
            new LocalCityGmlDocumentReader(),
            new LocalCityGmlConstructionSourceFactory(
                new LocalCityGmlDocumentReader(),
                new LocalCityGmlConstructionComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()))));
    }

    [Fact]
    public async Task RunAsyncWritesLiveCompletionForValidBuildCommand()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubSceneBuilder sceneBuilder = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(sceneBuilder));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "local",
                "--local-source-path",
                fixturePath,
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, sceneBuilder.CityObjects.Count);
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
        Assert.Contains("World: PLATEAU tokyo23ku 53394525", standardOutput.ToString());
        Assert.Contains("Resonite location: stub://resonite/location", standardOutput.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task RunAsyncReturnsFailureForOperationalException()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("Unexpected transport failure."));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        int exitCode = await application.RunAsync(
            BuildLiveArgs(fixturePath));

        Assert.Equal(1, exitCode);
        Assert.Contains("Import failed: Unexpected transport failure.", standardError.ToString());
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncPassesDocumentedDefaultPackagesWhenPackagesOptionIsOmitted()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubSceneBuilder()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        int exitCode = await application.RunAsync(
            BuildLiveArgs(fixturePath));

        Assert.Equal(0, exitCode);
        BuildCommandOptions capturedOptions = Assert.Single(importServiceFactory.CapturedOptions);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, capturedOptions.Request.PackageNames);
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncPassesMeshBakeDisableOptionToFactory()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubSceneBuilder()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                ..BuildLiveArgs(fixturePath),
                "--no-mesh-bake",
            ]);

        Assert.Equal(0, exitCode);
        BuildCommandOptions capturedOptions = Assert.Single(importServiceFactory.CapturedOptions);
        Assert.False(capturedOptions.EnableMeshBake);
    }

    [Fact]
    public async Task RunAsyncDoesNotWarnWhenMultipleConnectionsAreConfigured()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubSceneBuilder()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                ..BuildLiveArgs(fixturePath),
                "--resonitelink-connections",
                "4",
            ]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("is experimental", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellation()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new OperationCanceledException());

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory);

        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => application.RunAsync(
                BuildLiveArgs(fixturePath),
                cancellationTokenSource.Token));
    }

    private static string[] BuildLiveArgs(string fixturePath)
    {
        return CliTestData.BuildLocalBuildArgs(fixturePath);
    }

    private sealed class StubSceneBuilder : ISceneImportTarget
    {
        public List<ImportedCityObject> CityObjects { get; } = [];

        public Task EnsureConnectedAsync(
            PlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BeginAsync(
            SceneBuildRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            return Task.CompletedTask;
        }

        public Task ProcessCityObjectAsync(
            ImportedCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            CityObjects.Add(cityObject);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(["stub://resonite/location"]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubImportServiceFactory(Func<BuildCommandOptions, PlateauImportService> createImportService)
        : IImportServiceFactory
    {
        public List<BuildCommandOptions> CapturedOptions { get; } = [];

        public PlateauImportService Create(BuildCommandOptions options, Action<string>? progressReporter)
        {
            CapturedOptions.Add(options);
            return createImportService(options);
        }
    }
}
