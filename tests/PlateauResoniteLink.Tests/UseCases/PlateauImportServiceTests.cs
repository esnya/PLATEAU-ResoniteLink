using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "PlateauImportService owns the scene builder lifetime in these tests.")]
public sealed class PlateauImportServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UsesPackageCatalogCommonMaterialsForSetup()
    {
        using TemporaryDirectory workRoot = new();

        Uri rawSourceUri = new("https://example.invalid/tokyo23ku/source-archive.zip");
        string datasetWorkRoot = Path.Combine(workRoot.Path, "tokyo23ku");
        string resolvedSourcePath = RemoteDatasetResourceLayout.GetRemoteResourcePath(
            datasetWorkRoot,
            rawSourceUri,
            "source-archive");
        PlateauImportRequest rawRequest = new(
            Dataset: " tokyo23ku ",
            MeshCode: "53394525",
            Source: DatasetLocation.Remote(rawSourceUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(resolvedSourcePath),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            Source: new ValidatedLocalDatasetLocation(resolvedSourcePath),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(resolvedSourcePath);
        LocalCityGmlDocumentReadResult readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        IReadOnlyList<MaterialBinding> sourceCommonMaterials = [
            new MaterialBinding(
                MaterialKey: "shared-mat-a",
                BaseColor: new ColorRgba(1, 1, 1, 1),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                ReuseScope: MaterialReuseScope.Shared),
            new MaterialBinding(
                MaterialKey: "shared-mat-b",
                BaseColor: new ColorRgba(1, 1, 1, 1),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [1],
                ReuseScope: MaterialReuseScope.Shared),
            new MaterialBinding(
                MaterialKey: "shared-mat-a",
                BaseColor: new ColorRgba(1, 1, 1, 1),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [2],
                ReuseScope: MaterialReuseScope.Shared),
        ];
        RecordingSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubConstructionSource source = new(
            CreateMetadata(resolvedRequest, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingConstructionSourceFactory constructionSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory,
            new CommonMaterialCatalog(),
            new ArchiveFileLayoutPolicy());

        ImportExecutionResult result = await service.ExecuteAsync(rawRequest, workRoot.Path);

        Assert.Equal(Path.Combine(workRoot.Path, "tokyo23ku"), datasetSourceResolver.LastWorkRoot);
        Assert.NotNull(sceneBuilder.ConnectedRequest);
        Assert.Equal("tokyo23ku", sceneBuilder.ConnectedRequest!.Dataset);
        Assert.Equal("53394525", sceneBuilder.ConnectedRequest.MeshCode);
        Assert.Equal(rawSourceUri, sceneBuilder.ConnectedRequest.ServerUri);
        Assert.Equal(["bldg"], sceneBuilder.ConnectedRequest.PackageNames);
        Assert.NotNull(constructionSourceFactory.LastRequest);
        Assert.Equal("tokyo23ku", constructionSourceFactory.LastRequest!.Dataset);
        Assert.Equal("53394525", constructionSourceFactory.LastRequest.MeshCode);
        Assert.Equal(resolvedSourcePath, constructionSourceFactory.LastRequest.LocalSourcePath);
        Assert.Equal(["bldg"], constructionSourceFactory.LastRequest.PackageNames);
        Assert.NotNull(sceneBuilder.BeginRequest);
        Assert.Equal(resolvedSourcePath, sceneBuilder.BeginRequest!.Metadata.Request.LocalSourcePath);
        Assert.Equal(resolvedSourcePath, sceneBuilder.BeginRequest.ResolvedSourcePath);
        Assert.Equal(Path.Combine(workRoot.Path, "tokyo23ku"), sceneBuilder.BeginRequest.WorkRoot);
        Assert.Equal(["bldg"], sceneBuilder.BeginRequest.Metadata.SourceDataset.PackageNames);
        Assert.Equal(readResult.DocumentSet.RelativeSourceFiles, sceneBuilder.BeginRequest.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(readResult.DocumentSet.SelectedMeshCodes, sceneBuilder.BeginRequest.Metadata.SourceDataset.SelectedMeshCodes);
        Assert.NotNull(sceneBuilder.BeginRequest.CommonMaterials);
        Assert.Equal(
            new CommonMaterialCatalog().CreateForPackages(["bldg"]).Select(static material => material.MaterialKey).OrderBy(static key => key),
            [.. sceneBuilder.BeginRequest.CommonMaterials.Select(material => material.MaterialKey).OrderBy(materialKey => materialKey)]);
        Assert.All(
            sourceCommonMaterials.Select(static material => material.MaterialKey),
            materialKey => Assert.DoesNotContain(materialKey, sceneBuilder.BeginRequest.CommonMaterials.Select(material => material.MaterialKey)));
        Assert.Single(sceneBuilder.ProcessedCityObjects);
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(1, sceneBuilder.ExecuteCallCount);
        Assert.Equal(1, constructionSourceFactory.CreateCallCount);
        Assert.Equal(1, sceneBuilder.DisposeCount);
        Assert.Equal(source.Metadata.SceneName, result.Metadata.SceneName);
        Assert.Equal(source.Metadata.SourceDataset.PackageNames, result.Metadata.SourceDataset.PackageNames);
        Assert.Equal(["stub://destination"], result.Destinations);
        Assert.Equal(1 + readResult.DocumentSet.RelativeSourceFiles.Count, result.DataSourceUsages?.Count);
        Assert.Contains(
            result.DataSourceUsages ?? [],
            static usage => usage.Category == ImportDataSourceCategory.CityGmlSourceFile
                && string.Equals(usage.Identity, "udx/bldg/53394525/building.gml", StringComparison.Ordinal)
                && usage.UsedCount == 1);
        Assert.Contains(
            result.DataSourceUsages ?? [],
            static usage => usage.Category == ImportDataSourceCategory.DemTextureSource
                && string.Equals(usage.Identity, "terrain://ortho-primary", StringComparison.Ordinal)
                && usage.UsedCount == 2);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsValidationExceptionWhenSourceProducesNoCityObjects_AndStillDisposesSceneBuilder()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            Source: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        LocalCityGmlDocumentReadResult readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubConstructionSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles),
            cityObjects: []);
        RecordingConstructionSourceFactory constructionSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory,
            new CommonMaterialCatalog(),
            new ArchiveFileLayoutPolicy());

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("No triangulated CityGML geometry", StringComparison.Ordinal));
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(0, sceneBuilder.ExecuteCallCount);
        Assert.Equal(1, constructionSourceFactory.CreateCallCount);
        Assert.Equal(1, sceneBuilder.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_UsesPackageCatalogCommonMaterialsWhenSourceEnumerationIsEmpty()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            Source: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        LocalCityGmlDocumentReadResult readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubConstructionSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingConstructionSourceFactory constructionSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory,
            new CommonMaterialCatalog(),
            new ArchiveFileLayoutPolicy());

        _ = await service.ExecuteAsync(request, workRoot.Path);

        Assert.NotNull(sceneBuilder.BeginRequest);
        Assert.Equal(
            new CommonMaterialCatalog().CreateForPackages(["bldg"]).Select(static material => material.MaterialKey).OrderBy(static key => key),
            sceneBuilder.BeginRequest!.CommonMaterials.Select(material => material.MaterialKey).OrderBy(static key => key));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOperationalFailureWhenTargetFailsEveryCityObject()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            Source: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        LocalCityGmlDocumentReadResult readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneBuilder sceneBuilder = new()
        {
            ExecutionResultFactory = static cityObjectCount => new SceneImportExecutionResult(
                ["stub://destination"],
                ProcessedCityObjectCount: 0,
                FailedCityObjectCount: cityObjectCount),
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubConstructionSource source = new(CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingConstructionSourceFactory constructionSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory,
            new CommonMaterialCatalog(),
            new ArchiveFileLayoutPolicy());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains("Live send failed for all", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, sceneBuilder.ExecuteCallCount);
        Assert.Single(sceneBuilder.ProcessedCityObjects);
        Assert.Equal(1, sceneBuilder.DisposeCount);
    }

    private static LocalCityGmlDocumentReadResult CreateReadResult(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<string> relativeSourceFiles)
    {
        return new LocalCityGmlDocumentReadResult(
            new LocalCityGmlDocumentSet(
                datasetSource,
                relativeSourceFiles,
                packageNames,
                [],
                ["53394525"]),
            new LocalCityGmlBootstrapContext(
                [],
                new GeodeticPoint(35.0, 139.0, 0.0)));
    }

    private static ImportedSceneMetadata CreateMetadata(
        PlateauImportRequest request,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<string> sourceFiles)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: request,
            SourceDataset: new PlateauSourceDataset(packageNames, sourceFiles, [], ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(false, "credit", "license", "https://example.invalid/license"),
                []),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }

    private sealed class RecordingSceneBuilder : ISceneSink
    {
        public int ExecuteCallCount { get; private set; }

        public PlateauImportRequest? ConnectedRequest { get; private set; }

        public SceneBuildRequest? BeginRequest { get; private set; }

        public List<ImportedCityObject> ProcessedCityObjects { get; } = [];

        public int DisposeCount { get; private set; }

        public Func<int, SceneImportExecutionResult>? ExecutionResultFactory { get; init; }

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedCityObject> cityObjects,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            ConnectedRequest = plan.NormalizedRequest;
            BeginRequest = plan.SceneBuildRequest;
            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                ProcessedCityObjects.Add(cityObject);
            }

            return ExecutionResultFactory is null
                ? new SceneImportExecutionResult(
                    ["stub://destination"],
                    ProcessedCityObjects.Count,
                    DataSourceUsages:
                    [
                        new ImportDataSourceUsage(
                            ImportDataSourceCategory.DemTextureSource,
                            "terrain://ortho-primary",
                            2),
                    ])
                : ExecutionResultFactory(ProcessedCityObjects.Count);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDatasetSourceResolver(ValidatedPlateauImportRequest resolvedRequest) : IPlateauDatasetSourceResolver
    {
        public int ResolveCallCount { get; private set; }

        public string? LastWorkRoot { get; private set; }

        public Task<ValidatedPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastWorkRoot = workRoot;
            return Task.FromResult(resolvedRequest);
        }
    }

    private sealed class RecordingConstructionSourceFactory(
        IImportedSceneSource source,
        LocalCityGmlDocumentReadResult readResult) : IImportedSceneSourceFactory
    {
        public int CreateCallCount { get; private set; }

        public PlateauImportRequest? LastRequest { get; private set; }

        public Task<IImportedSceneSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            LastRequest = request;
            _ = readResult;
            return Task.FromResult(source);
        }
    }

    private sealed class StubConstructionSource(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ImportedCityObject>? cityObjects = null)
        : IImportedSceneSource
    {
        private readonly IReadOnlyList<ImportedCityObject> cityObjects = cityObjects ?? [CreateCityObject()];

        public ImportedSceneMetadata Metadata { get; } = metadata;

        public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ImportedCityObject cityObject in cityObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return cityObject;
            }

            await Task.CompletedTask;
        }

        private static ImportedCityObject CreateCityObject()
        {
            return new ImportedCityObject(
                ObjectKey: "city-object",
                DisplayName: "City Object",
                PackageName: "bldg",
                ActualMeshCode: "53394525",
                LodLevel: 1,
                Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
                Geometry: new TriangleMeshGeometry(new ImportedMesh(
                    [
                        new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                        new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                        new MeshVertex(new Float3(0.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                    ],
                    [new MeshSubmesh(0, "material", [0, 1, 2])])),
                Materials: [],
                SourceObjectKey: "source-object",
                SourceUnitKey: "source-unit",
                SourceFileRelativePath: "udx/bldg/53394525/building.gml");
        }
    }

    private sealed class RecordingDatasetSource(string sourcePath) : IPlateauDatasetContentSource
    {
        public string SourcePath => sourcePath;

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
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
