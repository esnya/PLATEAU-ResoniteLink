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
using PlateauResoniteLink.Diagnostics;
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
        return new PlateauImportService(
            sceneImportSink,
            new CkanPlateauDatasetSourceResolver(
                SharedDatasetSourceResolverHttpClient,
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new DefaultImportedSceneSourceFactory(
                documentReader,
                new DefaultImportedSceneSourceComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(CommonMaterialCatalog.Create())),
                    new DefaultDemTextureSourcePolicy(
                        new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                            new DefaultPlateauDatasetContentSourceFactory(
                                new RemoteArchiveDistributionPolicy(),
                                new ArchiveFileLayoutPolicy())))),
                new PassthroughImportedObjectUnitOptimizer()),
            CommonMaterialCatalog.Create(),
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
        string output = standardOutput.ToString();
        string error = standardError.ToString();
        Assert.Contains("Resonite import completed.", output, StringComparison.Ordinal);
        Assert.Contains("World: PLATEAU tokyo23ku 53394525", output, StringComparison.Ordinal);
        Assert.Contains("Resonite location: stub://resonite/location", output, StringComparison.Ordinal);
        Assert.Contains("Data sources:", output, StringComparison.Ordinal);
        Assert.Contains("CityGML source files:", output, StringComparison.Ordinal);
        Assert.Contains("DEM texture sources:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Import source prepared", output, StringComparison.Ordinal);
        Assert.Contains("Import source prepared for live send", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolved CityGML source", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncWritesCanonicalDumpCompletionWithoutLiveDestination()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        string dumpPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
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
                "--canonical-scene-dump",
                dumpPath,
            ]);

        string output = standardOutput.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Canonical scene dump completed.", output, StringComparison.Ordinal);
        Assert.Contains($"Dump: {Path.GetFullPath(dumpPath)}", output, StringComparison.Ordinal);
        Assert.Contains("World: PLATEAU tokyo23ku 53394525", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Resonite location:", output, StringComparison.Ordinal);
        Assert.Contains("Import source prepared for live send", standardError.ToString(), StringComparison.Ordinal);
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
        StubImportServiceFactory importServiceFactory = new(_ => throw new AggregateException(
            new InvalidOperationException("first terrain overlay failure"),
            new InvalidOperationException("second terrain overlay failure")));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
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
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(new StubImportSink()));

        CliApplication application = new(
            standardOutput,
            standardError,
            importServiceFactory,
            CreateDatasetInspectionService());

        int exitCode = await application.RunAsync(
            CreateImportArgs(fixturePath));

        Assert.Equal(0, exitCode);
        ImportCommandOptions capturedOptions = Assert.Single(importServiceFactory.CapturedOptions);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, capturedOptions.Request.PackageNames);
        Assert.Equal(PlateauImportMemoryProfile.Large, capturedOptions.MemoryProfile);
        Assert.Contains("Import source prepared for live send", standardError.ToString(), StringComparison.Ordinal);
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
                ..CreateImportArgs(fixturePath),
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
                ..CreateImportArgs(fixturePath),
                "--resonitelink-connections",
                "4",
            ]);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("is experimental", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Import source prepared for live send", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncWritesVerboseProgressToStandardErrorWhenRequested()
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
                ..CreateImportArgs(fixturePath),
                "--verbose",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Resonite import completed.", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Resolved CityGML source", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncKeepsConcurrentImportProgressSeparatedByStandardErrorWriter()
    {
        using StringWriter firstOutput = new();
        using StringWriter firstError = new();
        using StringWriter secondOutput = new();
        using StringWriter secondError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        TaskCompletionSource bothImportsEnteredSink = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int enteredSinkCount = 0;

        CliApplication firstApplication = new(
            firstOutput,
            firstError,
            new StubImportServiceFactory(_ => CreateImportService(new CoordinatedProgressImportSink(
                "first-import-marker",
                bothImportsEnteredSink,
                () => Interlocked.Increment(ref enteredSinkCount)))),
            CreateDatasetInspectionService());
        CliApplication secondApplication = new(
            secondOutput,
            secondError,
            new StubImportServiceFactory(_ => CreateImportService(new CoordinatedProgressImportSink(
                "second-import-marker",
                bothImportsEnteredSink,
                () => Interlocked.Increment(ref enteredSinkCount)))),
            CreateDatasetInspectionService());

        Task<int> firstRun = firstApplication.RunAsync(CreateImportArgs(fixturePath));
        Task<int> secondRun = secondApplication.RunAsync(CreateImportArgs(fixturePath));

        int firstExitCode = await firstRun;
        int secondExitCode = await secondRun;
        Assert.True(firstExitCode == 0, firstError.ToString());
        Assert.True(secondExitCode == 0, secondError.ToString());
        string firstDiagnostics = firstError.ToString();
        string secondDiagnostics = secondError.ToString();
        Assert.Contains("first-import-marker", firstDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("second-import-marker", firstDiagnostics, StringComparison.Ordinal);
        Assert.Contains("second-import-marker", secondDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("first-import-marker", secondDiagnostics, StringComparison.Ordinal);
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
                CreateImportArgs(fixturePath),
                cancellationTokenSource.Token));
    }

    private static string[] CreateImportArgs(string fixturePath)
    {
        return CliTestData.CreateLocalImportArgs(fixturePath);
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

    private sealed class CoordinatedProgressImportSink(
        string marker,
        TaskCompletionSource bothImportsEnteredSink,
        Func<int> incrementEnteredSinkCount)
        : ISceneSink
    {
        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            _ = plan;
            _ = objectUnits;
            if (incrementEnteredSinkCount() == 2)
            {
                bothImportsEnteredSink.TrySetResult();
            }

            await bothImportsEnteredSink.Task.WaitAsync(cancellationToken);
            PlateauDiagnostics.Progress("coordinated progress {Marker}", marker);
            int cityObjectCount = 0;
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                cityObjectCount += objectUnit.CityObjects.Count;
            }

            return new SceneImportExecutionResult(
                ["stub://resonite/location"],
                ProcessedCityObjectCount: cityObjectCount,
                DataSourceUsages: []);
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

        public PlateauImportService Create(ImportCommandOptions options)
        {
            CapturedOptions.Add(options);
            return createImportService(options);
        }
    }
}
