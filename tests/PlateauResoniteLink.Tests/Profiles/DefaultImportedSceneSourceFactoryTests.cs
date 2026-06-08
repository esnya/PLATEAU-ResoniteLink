using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultImportedSceneSourceFactoryTests
{
    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        RecordingDocumentReader reader = new();
        RecordingComposer composer = new(new StubImportedSceneSource());
        ImportedObjectUnitOptimizer optimizer = PassthroughImportedObjectUnitOptimizer.OptimizeAsync;

        Assert.Throws<ArgumentNullException>(
            "documentReader",
            () => new DefaultImportedSceneSourceFactory(null!, composer.Compose, optimizer));
        Assert.Throws<ArgumentNullException>(
            "constructionComposer",
            () => new DefaultImportedSceneSourceFactory(reader, null!, optimizer));
        Assert.Throws<ArgumentNullException>(
            "objectUnitOptimizer",
            () => new DefaultImportedSceneSourceFactory(reader, composer.Compose, null!));
    }

    [Fact]
    public async Task CreateAsyncUsesDocumentReaderAndComposer()
    {
        StubImportedSceneSource expectedSource = new();
        RecordingDocumentReader reader = new();
        RecordingComposer composer = new(expectedSource);
        DefaultImportedSceneSourceFactory factory = new(
            reader,
            composer.Compose,
            new PassthroughImportedObjectUnitOptimizer());
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;

        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create();

        IImportedSceneSource result = await factory.CreateAsync(request, loggerFactory);

        Assert.Same(expectedSource, result);
        Assert.Equal(request, reader.LastRequest);
        Assert.NotNull(reader.LastLogger);
        Assert.Equal(request, composer.LastRequest);
        Assert.Same(loggerFactory, composer.LastLoggerFactory);
        Assert.Same(reader.ReadResult, composer.LastReadResult);
    }

    [Fact]
    public async Task CreateAsyncKeepsSetupReadResultDiscoveryOnlyWhenDemOverlaysAreNotPreResolved()
    {
        RecordingDocumentReader reader = new(
            new ImportedSceneSourceSnapshot(
                new ImportedSceneSourceDataset(
                    new EmptyDatasetContentSource(),
                    ["udx/dem/53394525/terrain.gml"],
                    ["dem"],
                    [],
                    ["53394525"]),
                new ImportedSceneSourceContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubImportedSceneSource());
        DefaultImportedSceneSourceFactory factory = new(
            reader,
            composer.Compose,
            PassthroughImportedObjectUnitOptimizer.OptimizeAsync);
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            packageNames: ["dem"]);

        _ = await factory.CreateAsync(request);

        Assert.Same(reader.ReadResult, composer.LastReadResult);
        Assert.Empty(reader.ReadResult.DocumentSet.TerrainTextureOverlays);
        Assert.Empty(composer.LastReadResult!.DocumentSet.TerrainTextureOverlays);
    }

    [Fact]
    public async Task CreateAsyncDoesNotParseDemFilesToValidateExplicitDemTextureSource()
    {
        int parseCount = 0;
        SourceFileDescriptor demSourceFile = new(
            "udx/dem/53394525/terrain.gml",
            "dem",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);
        SourceFilePipeline demPipeline = new(
            demSourceFile,
            () =>
            {
                parseCount++;
                throw new InvalidOperationException("Setup must not parse DEM source files.");
            });
        RecordingDocumentReader reader = new(
            new ImportedSceneSourceSnapshot(
                new ImportedSceneSourceDataset(
                    new EmptyDatasetContentSource(),
                    ["udx/dem/53394525/terrain.gml"],
                    ["dem"],
                    [],
                    ["53394525"]),
                new ImportedSceneSourceContext(
                    [demPipeline],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubImportedSceneSource());
        DefaultImportedSceneSourceFactory factory = new(
            reader,
            composer.Compose,
            PassthroughImportedObjectUnitOptimizer.OptimizeAsync);
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            packageNames: ["dem"],
            demTextureLocalSourcePath: "C:\\ortho\\53394525.tif");

        _ = await factory.CreateAsync(request);

        Assert.Equal(0, parseCount);
        Assert.Same(reader.ReadResult, composer.LastReadResult);
    }

    private sealed class RecordingDocumentReader : ICityGmlDocumentReader
    {
        public RecordingDocumentReader(ImportedSceneSourceSnapshot? readResult = null)
        {
            ReadResult = readResult ?? new ImportedSceneSourceSnapshot(
                new ImportedSceneSourceDataset(
                    new EmptyDatasetContentSource(),
                    [],
                    [],
                    [],
                    []),
                new ImportedSceneSourceContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0)));
        }

        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public ILogger? LastLogger { get; private set; }

        public ImportedSceneSourceSnapshot ReadResult { get; }

        public Task<ImportedSceneSourceSnapshot> ReadAsync(
            ResolvedLocalPlateauImportRequest request,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastLogger = logger;
            return Task.FromResult(ReadResult);
        }
    }

    private sealed class RecordingComposer(IImportedSceneSource importedSceneSource)
    {
        internal IImportedSceneSource ImportedSceneSource { get; } = importedSceneSource;

        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public ImportedSceneSourceSnapshot? LastReadResult { get; private set; }

        public ILoggerFactory? LastLoggerFactory { get; private set; }

        public IImportedSceneSource Compose(
            ResolvedLocalPlateauImportRequest request,
            ImportedSceneSourceSnapshot readResult,
            ImportedObjectUnitOptimizer objectUnitOptimizer,
            ILoggerFactory? loggerFactory = null)
        {
            LastRequest = request;
            LastReadResult = readResult;
            LastLoggerFactory = loggerFactory;
            _ = objectUnitOptimizer;
            return ImportedSceneSource;
        }
    }

    private sealed class StubImportedSceneSource : IImportedSceneSource
    {
        public ImportedSceneMetadata Metadata { get; } = new(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: new PlateauImportRequest(
                Dataset: "stub",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local("/tmp/plateau")
),
            SourceDataset: new PlateauSourceDataset([], [], []),
            Attribution: new Attribution(
                new LicenseMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid")),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));

        public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ImportedObjectUnit("stub.gml", "bldg", null, []);
            await Task.CompletedTask;
        }
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public EmptyDatasetContentSource(string sourcePath = "/tmp/plateau")
        {
            SourcePath = sourcePath;
        }

        public string SourcePath { get; }

        public IReadOnlyList<string> EnumerateFiles()
        {
            return [];
        }

        public bool FileExists(string relativePath)
        {
            return false;
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
        }

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }

}
