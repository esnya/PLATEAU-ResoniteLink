using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Cli;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The CLI test hands builder ownership to PlateauImportService.")]
public sealed class CliApplicationTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateImportService(ISceneSink sceneImportSink)
    {
        LocalCityGmlDocumentReader documentReader = CreateDocumentReader();
        return new PlateauImportService(
            sceneImportSink,
            new CkanPlateauDatasetSourceResolver(
                SharedDatasetSourceResolverHttpClient,
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new LocalCityGmlConstructionSourceFactory(
                documentReader,
                new LocalCityGmlConstructionComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
                    new LocalCityGmlDemTextureSourcePolicy(
                        new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                            new DefaultPlateauDatasetContentSourceFactory(
                                new RemoteArchiveDistributionPolicy(),
                                new ArchiveFileLayoutPolicy())))),
                new LocalCityGmlDemTextureSourcePolicy(
                    new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                        new DefaultPlateauDatasetContentSourceFactory(
                            new RemoteArchiveDistributionPolicy(),
                            new ArchiveFileLayoutPolicy()))),
                new PassthroughImportedCityObjectOptimizer()),
            new CommonMaterialCatalog(),
            new ArchiveFileLayoutPolicy());
    }

    private static LocalCityGmlDocumentReader CreateDocumentReader()
    {
        return new LocalCityGmlDocumentReader(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());
    }

    private static DatasetInspectionService CreateDatasetInspectionService()
    {
        return new DatasetInspectionService(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()));
    }

    [Fact]
    public async Task RunAsyncWritesLiveCompletionForValidImportCommand()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportSink importSink = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(importSink));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, importSink.CityObjects.Count);
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
        Assert.Contains("World: PLATEAU tokyo23ku 53394525", standardOutput.ToString());
        Assert.Contains("Resonite location: stub://resonite/location", standardOutput.ToString());
        Assert.Contains("Data sources:", standardOutput.ToString());
        Assert.Contains("CityGML source files:", standardOutput.ToString());
        Assert.Contains("DEM texture sources:", standardOutput.ToString());
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
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            BuildImportArgs(fixturePath));

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
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubImportSink()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            BuildImportArgs(fixturePath));

        Assert.Equal(0, exitCode);
        ImportCommandOptions capturedOptions = Assert.Single(importServiceFactory.CapturedOptions);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, capturedOptions.Request.PackageNames);
        Assert.Equal(PlateauImportMemoryProfile.Large, capturedOptions.MemoryProfile);
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncPassesMeshBakeDisableOptionToFactory()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubImportSink()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            [
                ..BuildImportArgs(fixturePath),
                "--no-mesh-bake",
            ]);

        Assert.Equal(0, exitCode);
        ImportCommandOptions capturedOptions = Assert.Single(importServiceFactory.CapturedOptions);
        Assert.False(capturedOptions.EnableMeshBake);
    }

    [Fact]
    public async Task RunAsyncDoesNotWarnWhenMultipleConnectionsAreConfigured()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubImportSink()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            [
                ..BuildImportArgs(fixturePath),
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
            importServiceFactory,
            CreateDatasetInspectionService());

        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => application.RunAsync(
                BuildImportArgs(fixturePath),
                cancellationTokenSource.Token));
    }

    private static string[] BuildImportArgs(string fixturePath)
    {
        return CliTestData.BuildLocalImportArgs(fixturePath);
    }

    private sealed class StubImportSink : ISceneSink
    {
        public List<ImportedCityObject> CityObjects { get; } = [];

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedCityObject> cityObjects,
            CancellationToken cancellationToken = default)
        {
            _ = plan;
            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                CityObjects.Add(cityObject);
            }

            return new SceneImportExecutionResult(
                ["stub://resonite/location"],
                CityObjects.Count,
                DataSourceUsages:
                [
                    new ImportDataSourceUsage(
                        ImportDataSourceCategory.DemTextureSource,
                        "terrain://ortho-primary",
                        1),
                ]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubImportServiceFactory(Func<ImportCommandOptions, PlateauImportService> createImportService)
        : IImportServiceFactory
    {
        public List<ImportCommandOptions> CapturedOptions { get; } = [];

        public PlateauImportService Create(ImportCommandOptions options, Action<string>? progressReporter)
        {
            CapturedOptions.Add(options);
            return createImportService(options);
        }
    }
}
