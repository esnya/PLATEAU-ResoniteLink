using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Cli;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The CLI test hands importTarget ownership to PlateauImportService.")]
public sealed class CliApplicationTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateImportService(ISceneSink sceneImportSink)
    {
        LocalCityGmlDocumentReader documentReader = CreateDocumentReader();
        CkanPlateauDatasetSourceResolver datasetSourceResolver = new(
            SharedDatasetSourceResolverHttpClient,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        return new PlateauImportService(
            sceneImportSink,
            datasetSourceResolver.ResolveAsync,
            CreateImportedSceneSource(documentReader),
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());
    }

    private static DefaultDemTextureSourcePolicy CreateDemTextureSourcePolicy()
    {
        return new DefaultDemTextureSourcePolicy(
            (source, cancellationToken) => DemTerrainGeoReferencedRasterCatalog.CreateAsync(
                source,
                CreateDatasetContentSourceAsync,
                cancellationToken));
    }

    private static LocalCityGmlDocumentReader CreateDocumentReader()
    {
        return new LocalCityGmlDocumentReader(
            CreateDatasetContentSourceAsync,
            CityGmlAppearanceStore.Create);
    }

    private static CreateImportedSceneSource CreateImportedSceneSource(LocalCityGmlDocumentReader documentReader)
    {
        return async (request, loggerFactory, cancellationToken) =>
        {
            ImportedSceneSourceSnapshot readResult = await documentReader.ReadAsync(
                request,
                loggerFactory?.CreateLogger("PlateauResoniteLink.Import"),
                cancellationToken);
            return StreamingImportedSceneSource.Compose(
                request,
                readResult,
                TestCityGmlGeometryProjector.Create(),
                CreateDemTextureSourcePolicy().ResolveAsync,
                PassthroughImportedObjectUnitOptimizer.OptimizeAsync,
                loggerFactory);
        };
    }

    private static DatasetInspectionService CreateDatasetInspectionService()
    {
        return new DatasetInspectionService(
            CreateDatasetContentSourceAsync);
    }

    private static Task<IPlateauDatasetContentSource> CreateDatasetContentSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        return PlateauDatasetContentSourceFactory.CreateAsync(
            sourcePath,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy(),
            cancellationToken);
    }

    [Fact]
    public async Task RunAsyncWritesLiveCompletionForValidImportCommand()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportSink importSink = new();
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => CreateImportService(importSink));

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
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
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => throw new InvalidOperationException("Unexpected transport failure."));

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            CreateImportArgs(fixturePath));

        Assert.Equal(1, exitCode);
        Assert.Contains("Import failed: Unexpected transport failure.", standardError.ToString());
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncListsAggregateExceptionInnerFailures()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => throw new AggregateException(
                new InvalidOperationException("first terrain overlay failure"),
                new InvalidOperationException("second terrain overlay failure")));

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            CreateImportArgs(fixturePath));

        string error = standardError.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Import failed: 2 errors occurred.", error, StringComparison.Ordinal);
        Assert.Contains("[1] first terrain overlay failure", error, StringComparison.Ordinal);
        Assert.Contains("[2] second terrain overlay failure", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncPassesDocumentedDefaultPackagesWhenPackagesOptionIsOmitted()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        List<ImportCommandOptions> capturedOptions = [];
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => CreateImportService(new StubImportSink()), capturedOptions);

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            CreateImportArgs(fixturePath));

        Assert.Equal(0, exitCode);
        ImportCommandOptions capturedOption = Assert.Single(capturedOptions);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, capturedOption.Request.PackageNames);
        Assert.Equal(PlateauImportMemoryProfile.Large, capturedOption.MemoryProfile);
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
    }

    [Fact]
    public async Task RunAsyncPassesMeshBakeDisableOptionToFactory()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        List<ImportCommandOptions> capturedOptions = [];
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => CreateImportService(new StubImportSink()), capturedOptions);

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            [
                ..CreateImportArgs(fixturePath),
                "--no-mesh-bake",
            ]);

        Assert.Equal(0, exitCode);
        ImportCommandOptions capturedOption = Assert.Single(capturedOptions);
        Assert.False(capturedOption.EnableMeshBake);
    }

    [Fact]
    public async Task RunAsyncDoesNotWarnWhenMultipleConnectionsAreConfigured()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => CreateImportService(new StubImportSink()));

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            [
                ..CreateImportArgs(fixturePath),
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
        Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> createImportService =
            CreateImportServiceFactory(_ => throw new OperationCanceledException());

        CliApplication application = new(
            standardOutput,
            standardError,
            createImportService,
            CreateDatasetInspectionService());

        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => application.RunAsync(
                CreateImportArgs(fixturePath),
                cancellationTokenSource.Token));
    }

    private static string[] CreateImportArgs(string fixturePath)
    {
        return CliTestData.CreateLocalImportArgs(fixturePath);
    }

    private static Func<ImportCommandOptions, ILoggerFactory, PlateauImportService> CreateImportServiceFactory(
        Func<ImportCommandOptions, PlateauImportService> createImportService,
        List<ImportCommandOptions>? capturedOptions = null)
    {
        return (options, _) =>
        {
            capturedOptions?.Add(options);
            return createImportService(options);
        };
    }

    private sealed class StubImportSink : ISceneSink
    {
        public List<ImportedCityObject> CityObjects { get; } = [];

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            _ = plan;
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                CityObjects.AddRange(objectUnit.CityObjects);
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

}
