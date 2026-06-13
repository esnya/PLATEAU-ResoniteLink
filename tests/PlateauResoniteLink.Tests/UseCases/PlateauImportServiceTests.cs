using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Diagnostics;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "PlateauImportService owns the scene sink lifetime in these tests.")]
public sealed class PlateauImportServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UsesCodebaseReachableCommonMaterialsForSetup()
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
            CityGmlSource: DatasetLocation.Remote(rawSourceUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(resolvedSourcePath),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(resolvedSourcePath),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(resolvedSourcePath);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneSink sceneSink = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubImportedSceneSource source = new(
            CreateMetadata(resolvedRequest, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        ImportExecutionResult result = await service.ExecuteAsync(rawRequest, workRoot.Path);

        Assert.Equal(Path.Combine(workRoot.Path, "tokyo23ku"), datasetSourceResolver.LastWorkRoot);
        Assert.Equal("tokyo23ku", sceneSink.ConnectedDataset);
        Assert.Equal("53394525", sceneSink.ConnectedMeshCode);
        Assert.NotNull(importedSceneSourceFactory.LastRequest);
        Assert.Equal("tokyo23ku", importedSceneSourceFactory.LastRequest!.Dataset);
        Assert.Equal("53394525", importedSceneSourceFactory.LastRequest.MeshCode);
        Assert.Equal(resolvedSourcePath, importedSceneSourceFactory.LastRequest.CityGmlLocalSourcePath);
        Assert.Equal(["bldg"], importedSceneSourceFactory.LastRequest.PackageNames);
        Assert.NotNull(sceneSink.BeginRequest);
        Assert.Equal(resolvedSourcePath, sceneSink.BeginRequest!.Metadata.Request.CityGmlLocalSourcePath);
        Assert.Equal(Path.Combine(workRoot.Path, "tokyo23ku"), sceneSink.BeginRequest.WorkRoot);
        Assert.Equal(["bldg"], sceneSink.BeginRequest.Metadata.SourceDataset.PackageNames);
        Assert.Equal(readResult.DocumentSet.RelativeSourceFiles, sceneSink.BeginRequest.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(readResult.DocumentSet.SelectedMeshCodes, sceneSink.BeginRequest.Metadata.SourceDataset.SelectedMeshCodes);
        Assert.NotNull(sceneSink.BeginRequest.CommonMaterials);
        Assert.Equal(
            CommonMaterialCatalog.Create().Map(CreateMaterialSignature).EnumerateItems(),
            sceneSink.BeginRequest.CommonMaterials.Map(CreateMaterialSignature).EnumerateItems());
        Assert.Single(sceneSink.ProcessedCityObjects);
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
        Assert.Equal(1, importedSceneSourceFactory.CreateCallCount);
        Assert.Equal(1, sceneSink.DisposeCount);
        Assert.Equal(source.Metadata.SceneName, result.Metadata.SceneName);
        Assert.Equal(source.Metadata.SourceDataset.PackageNames, result.Metadata.SourceDataset.PackageNames);
        Assert.Equal(["stub://destination"], result.Destinations);
        Assert.Equal(1 + readResult.DocumentSet.RelativeSourceFiles.Count, result.DataSourceUsages.Count);
        Assert.Contains(
            result.DataSourceUsages,
            static usage => usage.Category == ImportDataSourceCategory.CityGmlSourceFile
                && string.Equals(usage.Description, "udx/bldg/53394525/building.gml", StringComparison.Ordinal)
                && usage.UsedCount == 1);
        Assert.Contains(
            result.DataSourceUsages,
            static usage => usage.Category == ImportDataSourceCategory.DemTextureSource
                && string.Equals(usage.Description, "terrain://ortho-primary", StringComparison.Ordinal)
                && usage.UsedCount == 2);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsValidationExceptionWhenSourceProducesNoCityObjects_AfterSceneSinkExecution()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneSink sceneSink = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubImportedSceneSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles),
            cityObjects: []);
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("No triangulated CityGML geometry", StringComparison.Ordinal));
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
        Assert.Empty(sceneSink.ProcessedCityObjects);
        Assert.Equal(1, importedSceneSourceFactory.CreateCallCount);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_DisposesSceneSinkWhenSourceResolutionFails()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingSceneSink sceneSink = new();
        ThrowingDatasetSourceResolver datasetSourceResolver = new();
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(
            new StubImportedSceneSource(CreateMetadata(request, ["bldg"], ["udx/bldg/53394525/building.gml"])),
            CreateReadResult(new RecordingDatasetSource(rawSourceRoot.Path), ["bldg"], ["udx/bldg/53394525/building.gml"]));

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Equal("resolver-failed", exception.Message);
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(0, importedSceneSourceFactory.CreateCallCount);
        Assert.Equal(0, sceneSink.ExecuteCallCount);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_SuppressesDisposeFailureWhenResolverAlreadyFailed()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingSceneSink sceneSink = new()
        {
            DisposeException = new InvalidOperationException("dispose-failed"),
        };
        ThrowingDatasetSourceResolver datasetSourceResolver = new();
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(
            new StubImportedSceneSource(CreateMetadata(request, ["bldg"], ["udx/bldg/53394525/building.gml"])),
            CreateReadResult(new RecordingDatasetSource(rawSourceRoot.Path), ["bldg"], ["udx/bldg/53394525/building.gml"]));

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Equal("resolver-failed", exception.Message);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesDisposeFailureAfterSuccessfulExecution()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneSink sceneSink = new()
        {
            DisposeException = new InvalidOperationException("dispose-failed"),
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubImportedSceneSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Equal("dispose-failed", exception.Message);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsEmptySourceEnumerationAfterSinkExecution()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneSink sceneSink = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubImportedSceneSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles),
            cityObjects: []);
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("No triangulated CityGML geometry", StringComparison.Ordinal));
        Assert.NotNull(sceneSink.BeginRequest);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotReadObjectUnitsBeforeSinkExecution()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        ImportedCityObject firstCityObject = CreateCityObject("first-city-object");
        ImportedCityObject secondCityObject = CreateCityObject("second-city-object");
        StubImportedSceneSource source = new(
            CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles),
            objectUnits:
            [
                CreateObjectUnit("first-source.gml", firstCityObject),
                CreateObjectUnit("second-source.gml", secondCityObject),
            ]);
        RecordingSceneSink sceneSink = new()
        {
            OnExecuteBeforeRead = _ =>
            {
                Assert.False(source.EnumerationStarted);
                Assert.Equal(0, source.YieldedObjectUnitCount);
            },
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        _ = await service.ExecuteAsync(request, workRoot.Path);

        Assert.True(source.EnumerationStarted);
        Assert.Equal(2, source.YieldedObjectUnitCount);
        Assert.Equal([firstCityObject.ObjectKey, secondCityObject.ObjectKey], sceneSink.ProcessedCityObjects.Select(static cityObject => cityObject.ObjectKey));
    }

    [Fact]
    public async Task ExecuteAsync_ScopesSourceActivityToSceneSourceCreationOnly()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = static source => string.Equals(source.Name, PlateauDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        bool factoryObservedSourceActivity = false;
        StubImportedSceneSource source = new(CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingSceneSink sceneSink = new()
        {
            OnExecuteBeforeRead = _ => Assert.NotEqual("plateau.import.source", Activity.Current?.OperationName),
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(
            source,
            readResult,
            onCreate: () =>
            {
                factoryObservedSourceActivity = true;
                Assert.Equal("plateau.import.source", Activity.Current?.OperationName);
            });

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        _ = await service.ExecuteAsync(request, workRoot.Path);

        Assert.True(factoryObservedSourceActivity);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RunsSourcePreflightBeforeSinkExecution()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();
        string demTextureSourcePath = Path.Combine(rawSourceRoot.Path, "ortho.tif");
        File.WriteAllBytes(demTextureSourcePath, [0]);

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["dem"],
            DemTextureSource: DatasetLocation.Local(demTextureSourcePath));
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["dem"],
            DemTextureSource: new ValidatedLocalDatasetLocation(demTextureSourcePath));
        ImportedSceneSourceSnapshot readResult = CreateReadResult(
            new RecordingDatasetSource(rawSourceRoot.Path),
            ["dem"],
            ["udx/dem/53394525/terrain.gml"]);
        RecordingSceneSink sceneSink = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        PreflightThrowingImportedSceneSource source = new(
            CreateMetadata(request, ["dem"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains("invalid-dem-texture-source", exception.Errors);
        Assert.Equal(1, datasetSourceResolver.ResolveCallCount);
        Assert.Equal(1, importedSceneSourceFactory.CreateCallCount);
        Assert.Equal(1, source.PreflightCallCount);
        Assert.False(source.EnumerationStarted);
        Assert.Equal(0, sceneSink.ExecuteCallCount);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOperationalFailureWhenTargetFailsEveryCityObject()
    {
        using TemporaryDirectory rawSourceRoot = new();
        using TemporaryDirectory workRoot = new();

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex("^53394525$", RegexOptions.CultureInvariant),
            CityGmlSource: new ValidatedLocalDatasetLocation(rawSourceRoot.Path),
            PackageNames: ["bldg"]);
        RecordingDatasetSource datasetSource = new(rawSourceRoot.Path);
        ImportedSceneSourceSnapshot readResult = CreateReadResult(datasetSource, ["bldg"], ["udx/bldg/53394525/building.gml"]);
        RecordingSceneSink sceneSink = new()
        {
            ExecutionResultFactory = static cityObjectCount => new SceneImportExecutionResult(
                ["stub://destination"],
                ProcessedCityObjectCount: 0,
                FailedCityObjectCount: cityObjectCount),
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(validatedRequest);
        StubImportedSceneSource source = new(CreateMetadata(request, ["bldg"], readResult.DocumentSet.RelativeSourceFiles));
        RecordingImportedSceneSourceFactory importedSceneSourceFactory = new(source, readResult);

        PlateauImportService service = new(
            sceneSink,
            datasetSourceResolver,
            importedSceneSourceFactory,
            CommonMaterialCatalog.Create(),
            new ArchiveFileLayoutPolicy());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(request, workRoot.Path));

        Assert.Contains("Live send failed for all", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, sceneSink.ExecuteCallCount);
        Assert.Single(sceneSink.ProcessedCityObjects);
        Assert.Equal(1, sceneSink.DisposeCount);
    }

    private static ImportedSceneSourceSnapshot CreateReadResult(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<string> relativeSourceFiles)
    {
        return new ImportedSceneSourceSnapshot(
            new ImportedSceneSourceDataset(
                datasetSource,
                relativeSourceFiles,
                packageNames,
                [],
                ["53394525"]),
            new ImportedSceneSourceContext(
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
            SourceDataset: new PlateauSourceDataset(packageNames, sourceFiles, ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(false, "credit", "license", "https://example.invalid/license")),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }

    private static ImportedCityObject CreateCityObject(string objectKey)
    {
        return new ImportedCityObject(
            ObjectKey: objectKey,
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
                [new MeshSubmesh(0, [0, 1, 2])])),
            Materials: [],
            SourceFileRelativePath: "udx/bldg/53394525/building.gml");
    }

    private static ImportedObjectUnit CreateObjectUnit(string sourceFileRelativePath, ImportedCityObject cityObject)
    {
        return new ImportedObjectUnit(
            sourceFileRelativePath,
            cityObject.PackageName,
            1,
            [cityObject]);
    }

    private static string CreateMaterialSignature(MaterialBinding material)
    {
        string submeshes = string.Join("/", material.SubmeshIndices);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{material.BaseColor.R},{material.BaseColor.G},{material.BaseColor.B},{material.BaseColor.A}|"
            + $"{material.MaterialType}|{material.TextureSourceKind}|{material.Projection}|"
            + $"{material.DepthOffset?.Factor}:{material.DepthOffset?.Units}|"
            + $"{material.TextureScale?.X}:{material.TextureScale?.Y}|"
            + $"{material.Family}|{material.TextureOffset?.X}:{material.TextureOffset?.Y}|"
            + $"{material.ReuseScope}|{material.BundledVariantIndex}|{material.TerrainMeshCode}|{submeshes}");
    }

    private static string CreateMaterialSignature(DefaultCommonMaterialMember material)
    {
        return CreateMaterialSignature(material.CreateBinding([0]));
    }

    private sealed class RecordingSceneSink : ISceneSink
    {
        public int ExecuteCallCount { get; private set; }

        public string? ConnectedDataset { get; private set; }

        public string? ConnectedMeshCode { get; private set; }

        public SceneImportRequest? BeginRequest { get; private set; }

        public List<ImportedCityObject> ProcessedCityObjects { get; } = [];

        public int DisposeCount { get; private set; }

        public Func<int, SceneImportExecutionResult>? ExecutionResultFactory { get; init; }

        public Exception? DisposeException { get; init; }

        public Action<SceneImportExecutionPlan>? OnExecuteBeforeRead { get; init; }

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            ConnectedDataset = plan.SceneImportRequest.Metadata.Request.Dataset;
            ConnectedMeshCode = plan.SceneImportRequest.Metadata.Request.MeshCode;
            BeginRequest = plan.SceneImportRequest;
            OnExecuteBeforeRead?.Invoke(plan);
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                ProcessedCityObjects.AddRange(objectUnit.CityObjects);
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
            if (DisposeException is not null)
            {
                throw DisposeException;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDatasetSourceResolver(ValidatedPlateauImportRequest resolvedRequest) : IPlateauDatasetSourceResolver
    {
        public int ResolveCallCount { get; private set; }

        public string? LastWorkRoot { get; private set; }

        public Task<ResolvedLocalPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastWorkRoot = workRoot;
            ValidatedLocalDatasetLocation localSource = Assert.IsType<ValidatedLocalDatasetLocation>(resolvedRequest.CityGmlSource);
            ValidatedLocalDatasetLocation? localDemTextureSource = resolvedRequest.DemTextureSource is null
                ? null
                : Assert.IsType<ValidatedLocalDatasetLocation>(resolvedRequest.DemTextureSource);
            return Task.FromResult(ResolvedLocalPlateauImportRequest.Create(
                request,
                localSource,
                localDemTextureSource,
                workRoot));
        }
    }

    private sealed class ThrowingDatasetSourceResolver : IPlateauDatasetSourceResolver
    {
        public int ResolveCallCount { get; private set; }

        public Task<ResolvedLocalPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            throw new InvalidOperationException("resolver-failed");
        }
    }

    private sealed class RecordingImportedSceneSourceFactory(
        IImportedSceneSource source,
        ImportedSceneSourceSnapshot readResult,
        Action? onCreate = null) : IImportedSceneSourceFactory
    {
        public int CreateCallCount { get; private set; }

        public ResolvedLocalPlateauImportRequest? LastRequest { get; private set; }

        public Task<IImportedSceneSource> CreateAsync(
            ResolvedLocalPlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            CreateCallCount++;
            LastRequest = request;
            _ = readResult;
            onCreate?.Invoke();
            return Task.FromResult(source);
        }
    }

    private sealed class StubImportedSceneSource(
        ImportedSceneMetadata metadata,
        IReadOnlyList<ImportedCityObject>? cityObjects = null,
        IReadOnlyList<ImportedObjectUnit>? objectUnits = null)
        : IImportedSceneSource
    {
        private readonly IReadOnlyList<ImportedObjectUnit> objectUnits = objectUnits
            ?? (cityObjects ?? [CreateCityObject("city-object")])
                .Select(static cityObject => CreateObjectUnit("test-source.gml", cityObject))
                .ToArray();

        public ImportedSceneMetadata Metadata { get; } = metadata;

        public bool EnumerationStarted { get; private set; }

        public int YieldedObjectUnitCount { get; private set; }

        public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationStarted = true;
            foreach (ImportedObjectUnit objectUnit in objectUnits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                YieldedObjectUnitCount++;
                yield return objectUnit;
                await Task.CompletedTask;
            }
        }
    }

    private sealed class PreflightThrowingImportedSceneSource(ImportedSceneMetadata metadata)
        : IImportedSceneSource, IImportedSceneSourcePreflight
    {
        public ImportedSceneMetadata Metadata { get; } = metadata;

        public int PreflightCallCount { get; private set; }

        public bool EnumerationStarted { get; private set; }

        public Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCallCount++;
            throw new PlateauImportValidationException(["invalid-dem-texture-source"]);
        }

        public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationStarted = true;
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateObjectUnit("test-source.gml", CreateCityObject("city-object"));
            await Task.CompletedTask;
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
