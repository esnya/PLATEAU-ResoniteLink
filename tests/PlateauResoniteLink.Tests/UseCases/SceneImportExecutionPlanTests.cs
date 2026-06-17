using System;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

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
            ResolvedLocalPlateauImportRequest.RemoteCityGmlResourcePrefix);
        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            validatedRequest,
            new ValidatedLocalDatasetLocation(resolvedSourcePath),
            demTextureSource: null,
            workRoot);
        ImportedSceneMetadata metadata = CreateMetadata(resolvedRequest.ToImportRequest());

        SceneImportExecutionPlan plan = SceneImportExecutionPlan.Create(
            resolvedRequest,
            metadata,
            workRoot,
            CommonMaterialCatalog.Create());

        Assert.Equal(metadata.SourceDataset, plan.SceneImportRequest.Metadata.SourceDataset);
        Assert.Equal(resolvedSourcePath, plan.SceneImportRequest.Metadata.Request.CityGmlLocalSourcePath);
    }

    [Fact]
    public void CreateRejectsMetadataRequestThatDoesNotMatchResolvedLocalRequest()
    {
        string workRoot = "work";
        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequestTestFactory.Create(
            dataset: "tokyo23ku",
            meshCode: "53394525",
            cityGmlLocalSourcePath: "/tmp/plateau");
        ImportedSceneMetadata metadata = CreateMetadata(
            resolvedRequest.ToImportRequest() with
            {
                MeshCode = "53394526",
            });

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => SceneImportExecutionPlan.Create(
                resolvedRequest,
                metadata,
                workRoot,
                CommonMaterialCatalog.Create()));

        Assert.Equal("metadataRequest", exception.ParamName);
    }

    [Fact]
    public void CreateRejectsMetadataRequestWithDifferentGsiTerrainTileExclusion()
    {
        string workRoot = "work";
        string cityGmlLocalSourcePath = "/tmp/plateau";
        ValidatedPlateauImportRequest validatedRequest = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            MeshCodePattern: new Regex(@"\A(?:53394525)\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            CityGmlSource: new ValidatedLocalDatasetLocation(cityGmlLocalSourcePath),
            PackageNames: ["bldg"],
            ExcludeGsiTerrainTiles: true);
        ResolvedLocalPlateauImportRequest resolvedRequest = ResolvedLocalPlateauImportRequest.Create(
            validatedRequest,
            new ValidatedLocalDatasetLocation(cityGmlLocalSourcePath),
            demTextureSource: null);
        ImportedSceneMetadata metadata = CreateMetadata(
            resolvedRequest.ToImportRequest() with
            {
                ExcludeGsiTerrainTiles = false,
            });

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => SceneImportExecutionPlan.Create(
                resolvedRequest,
                metadata,
                workRoot,
                CommonMaterialCatalog.Create()));

        Assert.Equal("metadataRequest", exception.ParamName);
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
