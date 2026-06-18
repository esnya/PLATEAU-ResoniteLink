using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.CityGml;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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

    private static PlateauImportService CreateImportService(
        ISceneSink sceneImportSink,
        IPlateauDatasetSourceResolver? datasetSourceResolver = null,
        IImportedSceneSourceFactory? importedSceneSourceFactory = null)
    {
        LocalCityGmlDocumentReader documentReader = CreateDocumentReader();
        return new PlateauImportService(
            sceneImportSink,
            datasetSourceResolver ?? new CkanPlateauDatasetSourceResolver(
                SharedDatasetSourceResolverHttpClient,
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            importedSceneSourceFactory ?? new DefaultImportedSceneSourceFactory(
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
    public async Task RunAsyncWritesHelpForEmptyInvocation()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        CliApplication application = CreateApplication(
            standardOutput,
            standardError,
            new StubImportServiceFactory(_ => throw new InvalidOperationException("should not import")));

        int exitCode = await application.RunAsync([]);

        Assert.Equal(0, exitCode);
        string output = standardOutput.ToString();
        Assert.Contains("import", output, StringComparison.Ordinal);
        Assert.Contains("search", output, StringComparison.Ordinal);
        Assert.Contains("stats", output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task RunAsyncWritesLiveCompletionForValidImportCommand()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportSink importSink = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(importSink));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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
                "--distance-culling",
            ]);

        Assert.Equal(0, exitCode);
        (ImportSinkCliOptions _, ResoniteSceneBuildCliOptions sceneBuildOptions) =
            Assert.Single(importServiceFactory.CapturedOptions);
        Assert.True(sceneBuildOptions.EnableDistanceCulling);
        Assert.Equal(2, importSink.CityObjects.Count);
        Assert.Contains("Resonite import completed.", standardOutput.ToString());
        Assert.Contains("World: PLATEAU tokyo23ku 53394525", standardOutput.ToString());
        Assert.Contains("Resonite location: stub://resonite/location", standardOutput.ToString());
        Assert.Contains("Data sources:", standardOutput.ToString());
        Assert.Contains("CityGML source files:", standardOutput.ToString());
        Assert.Contains("DEM texture sources:", standardOutput.ToString());
        Assert.Contains("Import source prepared for live send", standardError.ToString(), StringComparison.Ordinal);
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

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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
    public async Task RunAsyncRejectsConflictingImportTargets()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("should not import"));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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
                "out/scene.json",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(importServiceFactory.CapturedOptions);
        Assert.Contains(
            "Do not specify --resonitelink-port or --resonitelink-url when --canonical-scene-dump is used.",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncRejectsImportWithoutTarget()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("should not import"));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(importServiceFactory.CapturedOptions);
        Assert.Contains(
            "Specify either --resonitelink-port, --resonitelink-url, or --canonical-scene-dump.",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncRejectsInvalidResoniteLinkConnectionCountWithoutExecutingImport()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("should not import"));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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
                "--resonitelink-connections",
                "0",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(importServiceFactory.CapturedOptions);
        Assert.Contains(
            "The value '0' is not a valid ResoniteLink connection count.",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RepeatedScalarResoniteLinkOptionCases))]
    public async Task RunAsyncRejectsRepeatedScalarResoniteLinkOptionsWithoutExecutingImport(
        string[] targetArgs,
        string optionName)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("should not import"));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        List<string> args =
        [
            "import",
            "--dataset",
            "tokyo23ku",
            "--mesh-code",
            "53394525",
            "--citygml-source",
            fixturePath,
        ];
        args.AddRange(targetArgs);

        int exitCode = await application.RunAsync([.. args]);

        Assert.Equal(1, exitCode);
        Assert.Empty(importServiceFactory.CapturedOptions);
        Assert.Contains(optionName, standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncRejectsPackageSpecificLodExclusionWithoutValue()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new InvalidOperationException("should not import"));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
                "--exclude-lod-for-package",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(importServiceFactory.CapturedOptions);
        Assert.Contains("Invalid package:lod format", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncResolvesImportSourceInputsBeforeExecutingImport()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        Uri demTextureServerUri = new("https://example.invalid/53394525.tif");
        CapturingDatasetSourceResolver datasetSourceResolver = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(
            new StubImportSink(),
            datasetSourceResolver,
            new ThrowingImportedSceneSourceFactory()));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
                "--geotiff-source",
                demTextureServerUri.ToString(),
                "--packages",
                "bldg",
                "--packages",
                "tran",
                "--exclude-lod",
                "1",
                "--exclude-lod",
                "2",
                "--exclude-lod-for-package",
                "tran:1",
                "--exclude-lod-for-package",
                "bldg:none",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(1, exitCode);
        ValidatedPlateauImportRequest? capturedRequest = datasetSourceResolver.CapturedRequest;
        Assert.NotNull(capturedRequest);
        Assert.Equal(DatasetSourceKind.Local, capturedRequest.CityGmlSourceKind);
        Assert.Equal(fixturePath, capturedRequest.CityGmlLocalSourcePath);
        Assert.Equal(DatasetSourceKind.Remote, capturedRequest.DemTextureSourceKind);
        Assert.Equal(demTextureServerUri, capturedRequest.DemTextureServerUri);
        Assert.Equal(["bldg", "tran"], capturedRequest.PackageNames);
        Assert.NotNull(capturedRequest.GlobalExcludeLodLevels);
        Assert.Contains(1, capturedRequest.GlobalExcludeLodLevels);
        Assert.Contains(2, capturedRequest.GlobalExcludeLodLevels);
        Assert.NotNull(capturedRequest.ExcludeLodLevelsByPackage);
        Assert.Contains(1, capturedRequest.ExcludeLodLevelsByPackage["tran"]);
        Assert.Empty(capturedRequest.ExcludeLodLevelsByPackage["bldg"]);
        Assert.Contains("Import failed: stop after binding", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncResolvesCommaDelimitedImportListInputsBeforeExecutingImport()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturingDatasetSourceResolver datasetSourceResolver = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(
            new StubImportSink(),
            datasetSourceResolver,
            new ThrowingImportedSceneSourceFactory()));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
                "--packages",
                "bldg,tran",
                "--exclude-lod",
                "1,2",
                "--exclude-lod-for-package",
                "tran:1,bldg:none",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(1, exitCode);
        ValidatedPlateauImportRequest? capturedRequest = datasetSourceResolver.CapturedRequest;
        Assert.NotNull(capturedRequest);
        Assert.Equal(["bldg", "tran"], capturedRequest.PackageNames);
        Assert.NotNull(capturedRequest.GlobalExcludeLodLevels);
        Assert.Contains(1, capturedRequest.GlobalExcludeLodLevels);
        Assert.Contains(2, capturedRequest.GlobalExcludeLodLevels);
        Assert.NotNull(capturedRequest.ExcludeLodLevelsByPackage);
        Assert.Contains(1, capturedRequest.ExcludeLodLevelsByPackage["tran"]);
        Assert.Empty(capturedRequest.ExcludeLodLevelsByPackage["bldg"]);
        Assert.Contains("Import failed: stop after binding", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncResolvesPackagePatternAliasBeforeExecutingImport()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturingDatasetSourceResolver datasetSourceResolver = new();
        StubImportServiceFactory importServiceFactory = new(_ => CreateImportService(
            new StubImportSink(),
            datasetSourceResolver,
            new ThrowingImportedSceneSourceFactory()));

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

        int exitCode = await application.RunAsync(
            [
                "import",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                fixturePath,
                "--packages",
                "waterbody",
                "--waterbody-pattern",
                "*foo",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal(1, exitCode);
        ValidatedPlateauImportRequest? capturedRequest = datasetSourceResolver.CapturedRequest;
        Assert.NotNull(capturedRequest);
        Assert.Equal(["wtr"], capturedRequest.PackageNames);
        Assert.NotNull(capturedRequest.PackagePatterns);
        Assert.Equal("*foo", capturedRequest.PackagePatterns["wtr"]);
        Assert.Contains("Import failed: stop after binding", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncExecutesSearchAndStatsCommands()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        CliApplication application = CreateApplication(
            standardOutput,
            standardError,
            new StubImportServiceFactory(_ => throw new InvalidOperationException("should not import")));

        int searchExitCode = await application.RunAsync(
            [
                "search",
                "--citygml-source",
                fixturePath,
                "--mesh-code",
                "53394525",
                "--packages",
                "bldg,tran",
                "--format",
                "json",
            ]);
        int statsExitCode = await application.RunAsync(
            [
                "stats",
                "--citygml-source",
                fixturePath,
                "--packages",
                "dem,bldg",
            ]);

        string output = standardOutput.ToString();
        Assert.Equal(0, searchExitCode);
        Assert.Equal(0, statsExitCode);
        Assert.Contains("\"selectedMeshCodes\"", output, StringComparison.Ordinal);
        Assert.Contains("bldg", output, StringComparison.Ordinal);
        Assert.Contains("Recognized CityGML source files:", output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Theory]
    [InlineData("search", "")]
    [InlineData("search", ",")]
    [InlineData("stats", "")]
    [InlineData("stats", ",")]
    public async Task RunAsyncRejectsEmptyInspectionPackageFilters(string command, string packages)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        CliApplication application = CreateApplication(
            standardOutput,
            standardError,
            new StubImportServiceFactory(_ => throw new InvalidOperationException("should not import")));

        List<string> args =
        [
            command,
            "--citygml-source",
            fixturePath,
        ];
        if (command == "search")
        {
            args.Add("--mesh-code");
            args.Add("53394525");
        }

        args.Add("--packages");
        args.Add(packages);

        int exitCode = await application.RunAsync([.. args]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Specify at least one package name.", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncReturnsFailureForSearchAndStatsValidationErrors()
    {
        string missingSourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "missing-citygml");

        using StringWriter searchOutput = new();
        using StringWriter searchError = new();
        CliApplication searchApplication = CreateApplication(
            searchOutput,
            searchError,
            new StubImportServiceFactory(_ => throw new InvalidOperationException("should not import")));

        int searchExitCode = await searchApplication.RunAsync(
            [
                "search",
                "--citygml-source",
                missingSourcePath,
                "--mesh-code",
                "5339",
            ]);

        using StringWriter statsOutput = new();
        using StringWriter statsError = new();
        CliApplication statsApplication = CreateApplication(
            statsOutput,
            statsError,
            new StubImportServiceFactory(_ => throw new InvalidOperationException("should not import")));

        int statsExitCode = await statsApplication.RunAsync(
            [
                "stats",
                "--citygml-source",
                missingSourcePath,
            ]);

        Assert.Equal(1, searchExitCode);
        Assert.Equal(string.Empty, searchOutput.ToString());
        Assert.Contains(missingSourcePath, searchError.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, statsExitCode);
        Assert.Equal(string.Empty, statsOutput.ToString());
        Assert.Contains(missingSourcePath, statsError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellation()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        StubImportServiceFactory importServiceFactory = new(_ => throw new OperationCanceledException());

        CliApplication application = CreateApplication(standardOutput, standardError, importServiceFactory);

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

    public static IEnumerable<object[]> RepeatedScalarResoniteLinkOptionCases()
    {
        yield return
        [
            new[] { "--resonitelink-port", "12345", "--resonitelink-port", "23456" },
            "resonitelink-port",
        ];
        yield return
        [
            new[] { "--resonitelink-url", "ws://localhost:12345/", "--resonitelink-url", "ws://localhost:23456/" },
            "resonitelink-url",
        ];
        yield return
        [
            new[] { "--resonitelink-port", "12345", "--resonitelink-connections", "1", "--resonitelink-connections", "2" },
            "resonitelink-connections",
        ];
    }

    private static CliApplication CreateApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        StubImportServiceFactory importServiceFactory)
    {
        CliConsoleWriters consoleWriters = new(standardOutput, standardError);
        DatasetInspectionService datasetInspectionService = CreateDatasetInspectionService();
        return new CliApplication(
            new CliCommandFactory(
                [
                    new ImportCliCommand(new DefaultImportCommandHandler(consoleWriters, importServiceFactory)),
                    new SearchCliCommand(new DefaultSearchCommandHandler(consoleWriters, datasetInspectionService)),
                    new StatsCliCommand(new DefaultStatsCommandHandler(consoleWriters, datasetInspectionService)),
                ]),
            consoleWriters);
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

    private sealed class CapturingDatasetSourceResolver : IPlateauDatasetSourceResolver
    {
        public ValidatedPlateauImportRequest? CapturedRequest { get; private set; }

        public Task<ResolvedLocalPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            _ = workRoot;
            _ = cancellationToken;
            CapturedRequest = request;
            throw new InvalidOperationException("stop after binding");
        }
    }

    private sealed class ThrowingImportedSceneSourceFactory : IImportedSceneSourceFactory
    {
        public Task<IImportedSceneSource> CreateAsync(
            ResolvedLocalPlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new InvalidOperationException("should not create imported scene source");
        }
    }

    private sealed class StubImportServiceFactory(Func<ImportSinkCliOptions, PlateauImportService> createImportService)
        : IImportServiceFactory
    {
        public List<(ImportSinkCliOptions Sink, ResoniteSceneBuildCliOptions SceneBuild)> CapturedOptions { get; } = [];

        public PlateauImportService Create(
            ImportSinkCliOptions sinkOptions,
            ResoniteSceneBuildCliOptions sceneBuildOptions)
        {
            CapturedOptions.Add((sinkOptions, sceneBuildOptions));
            return createImportService(sinkOptions);
        }
    }
}
