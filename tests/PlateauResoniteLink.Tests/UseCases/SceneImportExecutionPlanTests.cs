using System;

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
            Source: DatasetLocation.Remote(remoteCityGmlUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = DatasetLocation.Local(
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteCityGmlUri, "source-archive")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneImportRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot, CommonMaterialCatalogSnapshot.Empty));

        Assert.Equal(normalizedRequest, plan.NormalizedRequest);
        Assert.Equal(resolvedRequest, plan.ResolvedRequest);
        Assert.Equal(resolvedRequest.LocalSourcePath, plan.SceneImportRequest.Metadata.Request.LocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsDifferentRemoteCityGmlSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Remote(new Uri("https://example.test/tokyo23ku.zip")),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            Source = DatasetLocation.Remote(new Uri("https://example.test/other.zip")),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneImportRequest(CreateMetadata(mismatchedRequest), "resolved-source", "work", CommonMaterialCatalogSnapshot.Empty)));
    }

    [Fact]
    public void Constructor_RejectsDifferentLocalDemTextureSource()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("raw-source"),
            DemTextureSource: DatasetLocation.Local("ortho-a.tif"),
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            DemTextureSource = DatasetLocation.Local("ortho-b.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneImportRequest(CreateMetadata(mismatchedRequest), "resolved-source", "work", CommonMaterialCatalogSnapshot.Empty)));
    }

    [Fact]
    public void Constructor_RejectsDifferentTerrainMeshMode()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("raw-source"),
            TerrainMeshMode: TerrainMeshMode.Static,
            PackageNames: ["bldg"]);
        PlateauImportRequest mismatchedRequest = normalizedRequest with
        {
            TerrainMeshMode = TerrainMeshMode.Dynamic,
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                mismatchedRequest,
                new SceneImportRequest(CreateMetadata(mismatchedRequest), "resolved-source", "work", CommonMaterialCatalogSnapshot.Empty)));
    }

    [Fact]
    public void Constructor_AllowsResolvedLocalDemTextureSourceForRemoteInput()
    {
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("raw-source"),
            DemTextureSource: DatasetLocation.Remote(remoteDemTextureUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = DatasetLocation.Local(
                RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho")),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            resolvedRequest,
            new SceneImportRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot, CommonMaterialCatalogSnapshot.Empty));

        Assert.Equal(
            RemoteDatasetResourceLayout.GetRemoteResourcePath(workRoot, remoteDemTextureUri, "source-ortho"),
            plan.SceneImportRequest.Metadata.Request.DemTextureLocalSourcePath);
    }

    [Fact]
    public void Constructor_RejectsResolvedLocalDemTextureSourceThatDoesNotMatchExpectedRemoteMaterializationPath()
    {
        string workRoot = "work";
        Uri remoteDemTextureUri = new("https://example.test/ortho-a.tif");
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("raw-source"),
            DemTextureSource: DatasetLocation.Remote(remoteDemTextureUri),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            DemTextureSource = DatasetLocation.Local("unexpected-local-ortho.tif"),
        };

        Assert.Throws<ArgumentException>(
            () => new SceneImportExecutionPlan(
                normalizedRequest,
                resolvedRequest,
                new SceneImportRequest(CreateMetadata(resolvedRequest), "resolved-source", workRoot, CommonMaterialCatalogSnapshot.Empty)));
    }

    private static ImportedSceneMetadata CreateMetadata(PlateauImportRequest request)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: request,
            SourceDataset: new PlateauSourceDataset(["bldg"], [], ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid/license"),
                []),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }
}
