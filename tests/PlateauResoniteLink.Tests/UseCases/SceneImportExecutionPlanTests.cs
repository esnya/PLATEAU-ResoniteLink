using System.Diagnostics.CodeAnalysis;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

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
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteCityGmlUri, "source-archive")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot));

        Assert.Same(normalizedRequest, plan.NormalizedRequest);
        Assert.Same(resolvedRequest, plan.ResolvedRequest);
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
                new SceneBuildRequest(CreateMetadata(mismatchedRequest), "resolved-source", "work")));
    }

    [Fact]
    public void Constructor_RejectsDifferentLocalDemTextureSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            DemTextureSource: PlateauImportSource.Local("ortho-a.tif"),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("ortho-b.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneBuildRequest(CreateMetadata(mismatchedRequest), "resolved-source", "work")));
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalDemTextureSourceForRemoteInput()
    {
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local(
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot));

        Assert.Equal(
            RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho"),
            plan.SceneBuildRequest.Metadata.Request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsResolvedLocalDemTextureSourceThatDoesNotMatchExpectedRemoteMaterializationPath()
    {
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            DemTextureSource: PlateauImportSource.Remote(remoteDemTextureUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = PlateauImportSource.Local("unexpected-local-ortho.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                resolvedRequest,
                new SceneBuildRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot)));
    }

    private static ImportedSceneMetadata CreateMetadata(PlateauImportRequest request)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: request,
            SourceDataset: new PlateauSourceDataset(["bldg"], [], [], ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid/license"),
                []),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }
}
