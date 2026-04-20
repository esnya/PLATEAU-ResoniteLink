using PlateauResoniteLink.Domain.Importing;
namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteMaterialPlanningTests
{
    [Fact]
    public async Task PlanCommonMaterialAssetAsyncImportsMetallicCompanionTextureWithLinearProfile()
    {
        using SceneBuilderRecordingClient client = new();
        ResoniteMaterialPlanning planning = new();
        ResoniteMaterialBinding material = new(
            MaterialKey: "facade-common",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.Facade,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: 0);
        _ = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet);
        string metallicPath = textureSet?.MetallicPath
            ?? throw new InvalidOperationException("Expected facade bundled material to provide a metallic companion texture.");

        PlannedDedicatedMaterialAsset plannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            material,
            CancellationToken.None);

        ResoniteRawTextureImport metallicTexture = Assert.Single(
            client.ImportedRawTextures,
            texture => string.Equals(texture.Identity, metallicPath, StringComparison.Ordinal));
        Assert.Equal(ResoniteTextureColorProfiles.Linear, metallicTexture.ColorProfile);
        Assert.Contains(
            plannedAsset.Textures,
            texture => string.Equals(texture.Identity.Value, "metallic", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveTerrainTextureCanvasMaterialComposesExistingTransformWithCanvasOccupancy()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.4, 0.8),
            TextureOffset: new ResoniteFloat2(0.1, 0.2),
            TerrainOverlay: overlay);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4], "terrain"),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.0, 0.0)),
        };

        ResoniteMaterialBinding effectiveMaterial = ResoniteMaterialPlanning.ResolveTerrainTextureCanvasMaterial(
            material,
            preparedTerrainTextures);

        Assert.Equal(new ResoniteFloat2(0.2, 0.2), effectiveMaterial.TextureScale);
        Assert.Equal(new ResoniteFloat2(0.05, 0.05), effectiveMaterial.TextureOffset);
    }

    [Fact]
    public void ResolveTerrainTextureCanvasMaterialIntroducesScaleOnlyWhenMaterialHadNoExplicitTransform()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TerrainOverlay: overlay);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4], "terrain"),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.0, 0.0)),
        };

        ResoniteMaterialBinding effectiveMaterial = ResoniteMaterialPlanning.ResolveTerrainTextureCanvasMaterial(
            material,
            preparedTerrainTextures);

        Assert.Equal(new ResoniteFloat2(0.5, 0.25), effectiveMaterial.TextureScale);
        Assert.Null(effectiveMaterial.TextureOffset);
    }
}
