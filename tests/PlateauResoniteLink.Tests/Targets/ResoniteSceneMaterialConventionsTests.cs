using System;
using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteSceneMaterialConventionsTests
{
    [Fact]
    public void CreateTextureMembers_ForAlbedo_UsesUrlOnly()
    {
        Dictionary<string, Member> members = ResoniteSceneMaterialConventions.CreateTextureMembers(
            new Uri("resdb:///texture/albedo"),
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo);

        Field_Uri url = Assert.IsType<Field_Uri>(members["URL"]);

        Assert.Equal("resdb:///texture/albedo", url.Value.ToString());
        Assert.DoesNotContain("PreferredProfile", members.Keys);
        Assert.DoesNotContain("WrapModeU", members.Keys);
        Assert.DoesNotContain("WrapModeV", members.Keys);
    }

    [Fact]
    public void CreateTextureMembers_ForMetallic_PrefersLinearProfileWithoutExplicitWrap()
    {
        Dictionary<string, Member> members = ResoniteSceneMaterialConventions.CreateTextureMembers(
            new Uri("resdb:///texture/metallic"),
            ResoniteSceneMaterialConventions.TextureMemberRole.Metallic);

        Assert.Equal("Linear", Assert.IsType<Field_Nullable_Enum>(members["PreferredProfile"]).Value);
        Assert.DoesNotContain("WrapModeU", members.Keys);
        Assert.DoesNotContain("WrapModeV", members.Keys);
    }

    [Fact]
    public void CreateTextureMembers_ForTerrainMainTextureOverride_ClampsWithoutPreferredProfile()
    {
        Dictionary<string, Member> members = ResoniteSceneMaterialConventions.CreateTextureMembers(
            new Uri("resdb:///texture/override"),
            ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride);

        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(members["WrapModeU"]).Value);
        Assert.Equal("Clamp", Assert.IsType<Field_Enum>(members["WrapModeV"]).Value);
        Assert.DoesNotContain("PreferredProfile", members.Keys);
    }

    [Fact]
    public void CreateTextureIdentity_ForMaterialTextureRoles_UsesStableTokens()
    {
        Assert.Equal(
            new TextureIdentity("albedo"),
            ResoniteSceneMaterialConventions.CreateTextureIdentity(
                ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo));
        Assert.Equal(
            new TextureIdentity("normal"),
            ResoniteSceneMaterialConventions.CreateTextureIdentity(
                ResoniteSceneMaterialConventions.PlannedTextureRole.Normal));
        Assert.Equal(
            new TextureIdentity("height"),
            ResoniteSceneMaterialConventions.CreateTextureIdentity(
                ResoniteSceneMaterialConventions.PlannedTextureRole.Height));
        Assert.Equal(
            new TextureIdentity("metallic"),
            ResoniteSceneMaterialConventions.CreateTextureIdentity(
                ResoniteSceneMaterialConventions.PlannedTextureRole.Metallic));
        Assert.Equal(
            new TextureIdentity("emission"),
            ResoniteSceneMaterialConventions.CreateTextureIdentity(
                ResoniteSceneMaterialConventions.PlannedTextureRole.Emission));
    }

    [Fact]
    public void CreateMaterialSlotName_ForCommonMaterial_UsesStableSharedDiscriminators()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: FacadeDefaultTilesPerMeter(),
            TextureOffset: new ResoniteFloat2(0.0, 0.5 / 6.0),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0));

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("variant-0", slotName);
        Assert.DoesNotContain(' ', slotName);
    }

    [Fact]
    public void CreateMaterialSlotName_ForCommonMaterialWithNonDefaultScale_KeepsSemanticSlotName()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.5),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0));

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("variant-0", slotName);
        Assert.DoesNotContain("scale", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMaterialSlotName_ForVariantSpecificFacadeDefault_DoesNotAddScaleDiscriminator()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0 / 6.0, 1.0 / 6.0),
            TextureOffset: new ResoniteFloat2(0.0, 0.5 / 6.0),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 1,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 1));

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("variant-1", slotName);
        Assert.DoesNotContain("scale", slotName, StringComparison.Ordinal);
        Assert.DoesNotContain("offset", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDedicatedMaterialSlotName_ForDedicatedMaterial_UsesZeroBasedMaterialIndexPresentationName()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new RawRgba32ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/payload-a.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0);

        string slotName = ResoniteSceneMaterialConventions.CreateDedicatedMaterialSlotName(material, materialIndex: 0);

        Assert.Equal("material-000-pbs-uv-uv", slotName);
        Assert.DoesNotContain("payload", slotName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("textures", slotName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2x3", slotName, StringComparison.Ordinal);
        Assert.DoesNotContain("0.5", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDedicatedMaterialSlotName_ForDedicatedMaterial_RejectsNegativeMaterialIndex()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResoniteSceneMaterialConventions.CreateDedicatedMaterialSlotName(material, materialIndex: -1));
    }

    [Fact]
    public void CreateMaterialSlotName_ForGenericSharedMaterial_UsesOnlyRenderingDiscriminators()
    {
        ResoniteMaterialBinding material = new(
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
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("uv", slotName);
        Assert.DoesNotContain("offset", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMaterialSlotName_ForGenericSharedMaterial_OmitsExplicitZeroOffset()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            TextureOffset: new ResoniteFloat2(0.0, 0.0),
            Family: null,
            BundledVariantIndex: null,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("uv", slotName);
    }

    [Fact]
    public void CreateCommonMaterialSlotLookupNames_ForIdentityGenericCommonMaterial_UsesCanonicalNameOnly()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            BundledVariantIndex: null,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        IReadOnlyList<string> slotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);

        Assert.Equal(["uv"], slotLookupNames);
    }

    [Fact]
    public void CreateCommonMaterialSlotLookupNames_ForIdentityScaleGenericOffsetMaterial_UsesCanonicalNameOnly()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            TextureScale: null,
            TextureOffset: new ResoniteFloat2(0.25, 0.75),
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        IReadOnlyList<string> slotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);

        Assert.Equal(
            ["uv-terrain-aligned"],
            slotLookupNames);
    }

    [Fact]
    public void CreateMaterialSlotName_ForVertexColorCommonMaterial_UsesVertexColorName()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);

        Assert.Equal("uv", slotName);
    }



    [Fact]
    public void NormalizeBatchGroupedMaterialBinding_KeepsTerrainOverlayProviderIndependentFromCommonIdentity()
    {
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay),
            Family: null,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.Common, normalized.AssetScope);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, normalized.CommonMaterial);
        Assert.Null(normalized.TexturePayload);
        Assert.Same(overlay, normalized.TerrainOverlay);
        Assert.Equal("53394525", normalized.TerrainMeshCode);
        Assert.Equal(
            ["uv"],
            ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(normalized));
    }

















    [Fact]
    public void NormalizeBatchGroupedMaterialBinding_DemotesTintedBundledFamilyCommonMaterial()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(0.8, 0.7, 0.6, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: FacadeDefaultTilesPerMeter(),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0));

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteColor(0.8, 0.7, 0.6, 1.0), normalized.BaseColor);
        Assert.Null(normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }

    [Fact]
    public void NormalizeBatchGroupedMaterialBinding_DemotesWhiteBundledFamilyCommonMaterialWithUvOrDepthTransform()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
            SubmeshIndices: [0],
            TextureScale: FacadeDefaultTilesPerMeter(),
            TextureOffset: new ResoniteFloat2(0.125, 0.25),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0));

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteMaterialDepthOffset(1.0, 1.0), normalized.DepthOffset);
        Assert.Null(normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }


    private static TerrainTextureOverlay CreateThirdMeshOverlay(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: 512);
    }

    private static ResoniteFloat2 FacadeDefaultTilesPerMeter()
    {
        ScalarPair value = BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue;
        return new ResoniteFloat2(value.X, value.Y);
    }

    private static ResoniteMaterialBinding CreateBundledCommonMaterial(string family)
    {
        return new ResoniteMaterialBinding(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: family,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(family, 0));
    }

    private static ResoniteMaterialBinding CreateGenericCommonMaterial(ResoniteMaterialDepthOffset? depthOffset)
    {
        return new ResoniteMaterialBinding(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: depthOffset,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());
    }

}
