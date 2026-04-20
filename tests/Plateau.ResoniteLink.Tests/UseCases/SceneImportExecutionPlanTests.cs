using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.UseCases;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class SceneImportExecutionPlanTests
{
    [Fact]
    public void Constructor_AllowsResolvedLocalCityGmlSourceForRemoteInput()
    {
        string workRoot = "work";
        Uri remoteCityGmlUri = new("https://example.test/tokyo23ku.zip");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Remote(remoteCityGmlUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteCityGmlUri, "source-archive")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                new StubDatasetContentSource(resolvedRequest.LocalSourcePath!),
                workRoot));

        Assert.Same(normalizedRequest, plan.NormalizedRequest);
        Assert.Equal(resolvedRequest.LocalSourcePath, plan.SceneBuildRequest.Metadata.Request.LocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsDifferentRemoteCityGmlSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Remote(new Uri("https://example.test/tokyo23ku.zip")),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Remote(new Uri("https://example.test/other.zip")),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalCityGmlSourceThatDoesNotMatchDatasetContentSource()
    {
        string workRoot = "work";
        Uri remoteCityGmlUri = new("https://example.test/tokyo23ku.zip");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Remote(remoteCityGmlUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteCityGmlUri, "source-archive")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                new StubDatasetContentSource("resolved-source-b"),
                workRoot));

        Assert.Equal(resolvedRequest.LocalSourcePath, plan.SceneBuildRequest.Metadata.Request.LocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsExecutionIdentityMismatch()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            Dataset = "yokohama",
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_RejectsUnexpectedSourceKindTransition()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Remote(new Uri("https://example.test/tokyo23ku.zip")),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_RejectsDifferentLocalCityGmlSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source-a"),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local("raw-source-b"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_RejectsDifferentLocalDemTextureSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Local("ortho-a.tif"));
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("ortho-b.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_RejectsDifferentRemoteDemTextureSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(new Uri("https://example.test/ortho-a.tif")));
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Remote(new Uri("https://example.test/ortho-b.tif")),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalDemTextureSourceForRemoteInput()
    {
        string datasetRoot = "resolved-source";
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri));
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                new StubDatasetContentSource(datasetRoot),
                workRoot));

        Assert.Equal(
            WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho"),
            plan.SceneBuildRequest.Metadata.Request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalDemTextureSourceForRemoteCityGmlAndRemoteOrthoInput()
    {
        string workRoot = "C:\\work\\plateau-13213-higashimurayama-shi-2020";
        Uri remoteCityGmlUri = new("https://example.test/higashimurayama.zip");
        Uri remoteDemTextureUri = new("https://example.test/higashimurayama-ortho.7z");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "higashimurayama",
            MeshCode: "53395325",
            Source: PlateauImportSource.Remote(remoteCityGmlUri),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri));
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteCityGmlUri, "source-archive")),
            DemTextureSource = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                new StubDatasetContentSource(resolvedRequest.LocalSourcePath!),
                workRoot));

        Assert.Equal(
            WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho"),
            plan.SceneBuildRequest.Metadata.Request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalDemTextureSourceForLocalCityGmlAndRemoteOrthoInput()
    {
        string datasetRoot = "C:\\data\\plateau-13213-higashimurayama-shi-2020";
        string workRoot = "C:\\work\\plateau-13213-higashimurayama-shi-2020";
        Uri remoteDemTextureUri = new("https://example.test/higashimurayama-ortho.7z");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "higashimurayama",
            MeshCode: "53395325",
            Source: PlateauImportSource.Local(datasetRoot),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri));
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local(
                WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                new StubDatasetContentSource(datasetRoot),
                workRoot));

        Assert.Equal(
            WorkRootLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho"),
            plan.SceneBuildRequest.Metadata.Request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsResolvedLocalDemTextureSourceThatDoesNotMatchExpectedRemoteMaterializationPath()
    {
        string datasetRoot = "resolved-source";
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri));
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("unexpected-local-ortho.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                resolvedRequest,
                new SceneBuildRequest(
                    CreateMetadata(resolvedRequest),
                    new StubDatasetContentSource(datasetRoot),
                    workRoot)));
    }

    [Fact]
    public void Constructor_RejectsSceneBuildRequestMetadataRequestMismatch()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Remote(new Uri("https://example.test/tokyo23ku.zip")),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local("resolved-source"),
        };
        PlateauImportRequest buildRequest = resolvedRequest with
        {
            Source = PlateauImportSource.Local("other-source"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                resolvedRequest,
                new SceneBuildRequest(
                    CreateMetadata(buildRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    [Fact]
    public void Constructor_RejectsSceneBuildRequestDemTextureSourceMismatch()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"],
            DemTextureSource: PlateauImportSource.Remote(new Uri("https://example.test/ortho-a.tif")));
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("resolved-ortho-a.tif"),
        };
        PlateauImportRequest buildRequest = resolvedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("resolved-ortho-b.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                resolvedRequest,
                new SceneBuildRequest(
                    CreateMetadata(buildRequest),
                    new StubDatasetContentSource("resolved-source"),
                    "work")));
    }

    private static ConstructionMetadata CreateMetadata(PlateauImportRequest request)
    {
        return new ConstructionMetadata(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: request,
            SourceDataset: new PlateauSourceDataset(["bldg"], [], [], ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid/license"),
                []),
            LocalOrigin: new LocalOrigin(35.0, 139.0, 0.0));
    }

    private sealed class StubDatasetContentSource(string sourcePath) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
