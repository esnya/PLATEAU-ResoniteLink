using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.UseCases;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class SceneImportExecutionPlanTests
{
    [Fact]
    public void Constructor_AllowsSourceResolvedLocationDifferences()
    {
        PlateauImportRequest normalizedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("raw-source"),
            PackageNames: ["bldg"]);
        PlateauImportRequest resolvedRequest = normalizedRequest with
        {
            Source = PlateauImportSource.Local("resolved-source"),
        };

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                "resolved-source",
                "work"));

        Assert.Same(normalizedRequest, plan.NormalizedRequest);
        Assert.Equal("resolved-source", plan.SceneBuildRequest.Metadata.Request.LocalSourcePath);
    }

    [Fact]
    public void Constructor_AllowsRemoteNormalizedRequestToResolveIntoLocalBuildRequest()
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

        SceneImportExecutionPlan plan = new(
            normalizedRequest,
            new SceneBuildRequest(
                CreateMetadata(resolvedRequest),
                "resolved-source",
                "work"));

        Assert.Same(normalizedRequest, plan.NormalizedRequest);
        Assert.Equal(DatasetSourceKind.Local, plan.SceneBuildRequest.Metadata.Request.SourceKind);
        Assert.Equal("resolved-source", plan.SceneBuildRequest.Metadata.Request.LocalSourcePath);
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
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    "resolved-source",
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
                new SceneBuildRequest(
                    CreateMetadata(mismatchedRequest),
                    "resolved-source",
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
}
