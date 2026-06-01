using System;

using System.Diagnostics.CodeAnalysis;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.UseCases;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class SceneImportExecutionPlanTests
{
    [Fact]
    public void CreateCarriesResolvedLocalRequest()
    {
        string workRoot = "work";
        Uri remoteCityGmlUri = new("https://example.test/tokyo23ku.zip");
        ValidatedPlateauImportRequest validatedRequest = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Remote(remoteCityGmlUri),
                PackageNames: ["bldg"]));
        string resolvedSourcePath = RemoteDatasetResourceLayout.GetRemoteResourcePath(
            workRoot,
            remoteCityGmlUri,
            "source-archive");
        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            validatedRequest,
            new ValidatedLocalDatasetLocation(resolvedSourcePath),
            demTextureSource: null);
        ImportedSceneMetadata metadata = CreateMetadata(resolvedRequest.ToImportRequest());

        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            resolvedRequest,
            metadata,
            workRoot,
            CommonMaterialCatalog.Create());

        Assert.Equal(metadata.SourceDataset, plan.SceneImportRequest.Metadata.SourceDataset);
        Assert.Equal(resolvedSourcePath, plan.SceneImportRequest.Metadata.Request.CityGmlLocalSourcePath);
    }

    private static ImportedSceneMetadata CreateMetadata(PlateauImportRequest request)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: request,
            SourceDataset: new PlateauSourceDataset(["bldg"], [], ["53394525"]),
            Attribution: new Attribution(
                new LicenseMetadata(true, "credit", "license", "https://example.invalid/license")),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }
}
