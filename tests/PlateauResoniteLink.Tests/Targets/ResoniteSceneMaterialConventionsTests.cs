using System;
using System.Collections.Generic;

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
    public void CreateMaterialSlotName_ForCommonMaterial_UsesStableSharedDiscriminators()
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

        Assert.StartsWith("shared_uv_variant_0_", slotName, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', slotName);
        Assert.DoesNotContain("common|facade|variant:0", slotName, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMaterialSlotName_ForCommonMaterialWithNonDefaultScale_AddsScaleDiscriminator()
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

        Assert.StartsWith("shared_uv_variant_0_", slotName, StringComparison.Ordinal);
        Assert.Contains("_scale_0.5x0.5_", slotName, StringComparison.Ordinal);
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
        Assert.DoesNotContain("_0.5x0.25_", slotName, StringComparison.Ordinal);
        Assert.DoesNotContain("_0.125x0.75_", slotName, StringComparison.Ordinal);
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

        Assert.Equal("shared_uv_generic_offset_0.25x0.75", slotName);
    }

    [Fact]
    public void CreateMaterialSlotName_ForGenericSharedMaterial_OmitsExplicitZeroOffset()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "generic|Uv|scale:none|offset:0x0|depth:none",
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
            AssetScope: ResoniteMaterialAssetScope.Common);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);

        Assert.Equal("shared_uv_generic", slotName);
    }

    [Fact]
    public void CreateCommonMaterialSlotLookupNames_ForIdentityGenericCommonMaterial_IncludesLegacyScaleOneName()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: ResoniteSceneMaterialConventions.CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset: null),
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
            AssetScope: ResoniteMaterialAssetScope.Common);

        IReadOnlyList<string> slotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);

        Assert.Equal(["shared_uv_generic", "shared_uv_generic_scale_1x1"], slotLookupNames);
    }

    [Fact]
    public void CreateCommonMaterialSlotLookupNames_ForIdentityScaleGenericOffsetMaterial_IncludesLegacyScaleOneName()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: ResoniteSceneMaterialConventions.CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: new ResoniteFloat2(0.25, 0.75),
                depthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0)),
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            TextureScale: null,
            TextureOffset: new ResoniteFloat2(0.25, 0.75),
            AssetScope: ResoniteMaterialAssetScope.Common);

        IReadOnlyList<string> slotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);

        Assert.Equal(
            ["shared_uv_generic_offset_0.25x0.75_depth_2x3", "shared_uv_generic_scale_1x1_offset_0.25x0.75_depth_2x3"],
            slotLookupNames);
    }

    [Fact]
    public void CreateMaterialSlotName_ForVertexColorCommonMaterial_UsesVertexColorName()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: ResoniteSceneMaterialConventions.CreateCanonicalVertexColorCommonMaterialKey(
                ResoniteMaterialProjection.Uv,
                depthOffset: null),
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            AssetScope: ResoniteMaterialAssetScope.Common);

        string slotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);

        Assert.Equal("shared_uv_vertex-color", slotName);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_AllowsTerrainOverlayAsMainTextureOverride()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TerrainOverlay: overlay,
            Family: null,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out ResoniteMaterialBinding normalizedMaterial,
            out string familySlotName);

        Assert.True(normalized);
        Assert.Equal("generic", familySlotName);
        Assert.Equal(ResoniteMaterialAssetScope.Common, normalizedMaterial.AssetScope);
        Assert.Equal("generic|Uv|scale:none|offset:none|depth:none", normalizedMaterial.MaterialKey);
        Assert.Null(normalizedMaterial.TerrainOverlay);
        Assert.Equal(new ResoniteColor(1.0, 1.0, 1.0, 1.0), normalizedMaterial.BaseColor);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_AllowsPayloadMaterialWithExplicitNoOpTextureTransform()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "payload-noop-transform",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/noop-transform.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.0, 0.0),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out ResoniteMaterialBinding normalizedMaterial,
            out string familySlotName);

        Assert.True(normalized);
        Assert.Equal("generic", familySlotName);
        Assert.Equal(ResoniteMaterialAssetScope.Common, normalizedMaterial.AssetScope);
        Assert.Null(normalizedMaterial.TextureScale);
        Assert.Null(normalizedMaterial.TextureOffset);
        Assert.Equal("generic|Uv|scale:none|offset:none|depth:none", normalizedMaterial.MaterialKey);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_AllowsVertexColorSharedCommonMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "vertex-color-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out ResoniteMaterialBinding normalizedMaterial,
            out string familySlotName);

        Assert.True(normalized);
        Assert.Equal("vertex-color", familySlotName);
        Assert.Equal(ResoniteMaterialAssetScope.Common, normalizedMaterial.AssetScope);
        Assert.Equal("vertex-color|Uv|depth:none", normalizedMaterial.MaterialKey);
        Assert.Equal(ResoniteMaterialType.VertexColor, normalizedMaterial.MaterialType);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsTintedVertexColorMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "vertex-color-tinted-material",
            BaseColor: new ResoniteColor(0.8, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsTransformedVertexColorMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "vertex-color-transformed-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(2.0, 1.0),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsTransformedGenericSharedMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "generic-shared-transformed-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/transformed-generic.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.25, 0.75),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsTransformedGenericCommonMaterialWithoutTerrainOverlay()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "generic-shared-transformed-common-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.25, 0.75),
            AssetScope: ResoniteMaterialAssetScope.Common);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsTintedBundledFamilySharedMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-tinted-material",
            BaseColor: new ResoniteColor(0.8, 0.7, 0.6, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeter,
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_RejectsBundledFamilySharedMaterialWithUvOrDepthTransform()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-transformed-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeter,
            TextureOffset: new ResoniteFloat2(0.125, 0.25),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out _,
            out _);

        Assert.False(normalized);
    }

    [Fact]
    public void NormalizeCommonMaterialBinding_DemotesTintedBundledFamilyCommonMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-tinted-common-material",
            BaseColor: new ResoniteColor(0.8, 0.7, 0.6, 1.0),
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

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteColor(0.8, 0.7, 0.6, 1.0), normalized.BaseColor);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, normalized.Family);
    }

    [Fact]
    public void NormalizeCommonMaterialBinding_DemotesWhiteBundledFamilyCommonMaterialWithUvOrDepthTransform()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-white-transformed-common-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeter,
            TextureOffset: new ResoniteFloat2(0.125, 0.25),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.Common);

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteMaterialDepthOffset(1.0, 1.0), normalized.DepthOffset);
        Assert.Equal(new ResoniteFloat2(0.125, 0.25), normalized.TextureOffset);
    }

    [Fact]
    public void NormalizeBatchGroupedMaterialBinding_DemotesTintedBundledFamilyCommonMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-tinted-batch-material",
            BaseColor: new ResoniteColor(0.8, 0.7, 0.6, 1.0),
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

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteColor(0.8, 0.7, 0.6, 1.0), normalized.BaseColor);
    }

    [Fact]
    public void NormalizeBatchGroupedMaterialBinding_DemotesWhiteBundledFamilyCommonMaterialWithUvOrDepthTransform()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-family-white-transformed-batch-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(1.0, 1.0),
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeter,
            TextureOffset: new ResoniteFloat2(0.125, 0.25),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.Common);

        ResoniteMaterialBinding normalized = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, normalized.AssetScope);
        Assert.Equal(new ResoniteMaterialDepthOffset(1.0, 1.0), normalized.DepthOffset);
        Assert.Equal(new ResoniteFloat2(0.125, 0.25), normalized.TextureOffset);
    }

    [Fact]
    public void TryNormalizeSharedMaterialBinding_AllowsTransformedTerrainOverlaySharedMaterial()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay-transformed-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.375),
            TerrainOverlay: overlay,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        bool normalized = ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
            material,
            out ResoniteMaterialBinding normalizedMaterial,
            out string familySlotName);

        Assert.True(normalized);
        Assert.Equal("generic", familySlotName);
        Assert.Equal(ResoniteMaterialAssetScope.Common, normalizedMaterial.AssetScope);
        Assert.Equal(new ResoniteFloat2(0.5, 0.25), normalizedMaterial.TextureScale);
        Assert.Equal(new ResoniteFloat2(0.125, 0.375), normalizedMaterial.TextureOffset);
        Assert.Equal("generic|Uv|scale:0.5x0.25|offset:0.125x0.375|depth:none", normalizedMaterial.MaterialKey);
    }
}
