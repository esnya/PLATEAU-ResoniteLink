using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneMaterialConventionsTests
{
    [Fact]
    public void CreateMaterialSlotName_ForCommonMaterial_UsesClassifiedName()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "common|facade|variant:0|Uv|scale:13x13",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeter,
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.Common);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);

        Assert.Equal("shared_uv_variant_0_facade001-2k-jpg-color", slotName);
    }

    [Fact]
    public void CreateMaterialSlotName_ForCommonMaterialWithNonDefaultScale_KeepsOnlyScaleDiscriminator()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "common|facade|variant:0|Uv|scale:0.5x0.5",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.5),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.Common);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);

        Assert.Equal("shared_uv_variant_0_scale_0.5x0.5_facade001-2k-jpg-color", slotName);
    }

    [Fact]
    public void CreateMaterialSlotName_ForDedicatedMaterial_KeepsDetailedIdentity()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "dedicated-material",
            BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: false);

        Assert.Contains("pbs-uv_uv_", slotName, StringComparison.Ordinal);
        Assert.Contains("_0.5x0.25_", slotName, StringComparison.Ordinal);
        Assert.Contains("_0.125x0.75_", slotName, StringComparison.Ordinal);
        Assert.Contains("_2x3_", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMaterialSlotName_ForGenericSharedMaterial_UsesOnlyRenderingDiscriminators()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "generic|Uv|scale:1x1|offset:0.25x0.75|depth:none",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.25, 0.75),
            Family: null,
            BundledVariantIndex: null,
            AssetScope: ResoniteMaterialAssetScope.Common);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);

        Assert.Equal("shared_uv_generic_scale_1x1_offset_0.25x0.75", slotName);
    }
}
