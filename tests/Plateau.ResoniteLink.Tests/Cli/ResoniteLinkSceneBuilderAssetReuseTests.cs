using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
public sealed class ResoniteLinkSceneBuilderAssetReuseTests
{
    private const string DatasetName = "reuse-test";
    private const string MeshCode = "53394525";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task BuildAsyncSharesCommonMaterialAssetsAcrossCityObjectsInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-one"),
                CreateBundledTriangleCityObject("shared-material-two"),
            ],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-two");
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, firstMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.StartsWith(
            $"PLATEAU {DatasetName}/Assets/Common/",
            client.SlotPaths[commonMaterialContainerSlotId],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncDoesNotShareCommonMaterialAssetsWhenUvScaleDiffers()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-scale-one"),
                CreateBundledTriangleCityObject("shared-material-scale-two", textureScale: new ResoniteFloat2(0.5, 0.5)),
            ],
            client);

        Assert.NotEqual(
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-one"),
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-two"));
    }

    [Fact]
    public async Task BuildAsyncDemotesPayloadTexturesToDedicatedMaterials()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-one",
                    ResoniteLinkSceneBuilderTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-one.png")),
                CreatePayloadTriangleCityObject(
                    "dataset-texture-two",
                    ResoniteLinkSceneBuilderTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-two.png")),
            ],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-two");
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("/Assets/Common/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEqual(firstMaterialId, secondMaterialId);
        Assert.DoesNotContain(firstMaterialId, commonMaterialIds);
        Assert.DoesNotContain(secondMaterialId, commonMaterialIds);
        Assert.Contains(client.ImportedRawTextures, static texture => texture.Identity == "textures/albedo-one.png");
        Assert.Contains(client.ImportedRawTextures, static texture => texture.Identity == "textures/albedo-two.png");
    }

    [Fact]
    public async Task BuildAsyncReusesNamedDatasetRootAssetsAndCommonAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneBuilderRecordingClient client = new();

        await ResoniteLinkSceneBuilderTestSupport.BuildSceneTwiceAsync(
            metadata,
            [CreateBundledTriangleCityObject("reuse-run-one")],
            [CreateBundledTriangleCityObject("reuse-run-two")],
            client);

        Slot datasetRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByNameOutsideAssets(client, $"PLATEAU {DatasetName}");
        Slot assetsRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets");
        Slot commonRoot = ResoniteLinkSceneBuilderTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets/Common");

        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, $"PLATEAU {DatasetName}", StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal)
            && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(1, client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "Common", StringComparison.Ordinal)
            && string.Equals(slot.Parent?.TargetID, assetsRoot.ID, StringComparison.Ordinal)));
        Assert.True(ResoniteLinkSceneBuilderTestSupport.IsDescendantOf(client, commonRoot.ID, assetsRoot.ID));
        Assert.True(client.ImportedMeshes.Count >= 2);
    }

    private static ResoniteConstructionMetadata CreateMetadata(string datasetRoot)
    {
        return ResoniteLinkSceneBuilderTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetRoot,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles:
            [
                $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml",
            ]);
    }

    private static ResoniteConstructionCityObject CreateBundledTriangleCityObject(
        string objectIdentity,
        ResoniteFloat2? textureScale = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("triangle-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreatePayloadTriangleCityObject(
        string objectIdentity,
        ResoniteTexturePayload payload)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLinkSceneBuilderTestSupport.CreateTriangleMesh("triangle-material"),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: BundledDefaultMaterialFamilies.Facade,
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static string GetRendererMaterialReferenceTarget(
        SceneBuilderRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(renderer.Members["Materials"]);
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(materials.Elements));
        return materialReference.TargetID;
    }
}
