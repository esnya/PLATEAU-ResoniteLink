using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DefaultMaterialResolverTests
{
    private readonly DefaultMaterialResolver resolver = new();

    [Fact]
    public void ResolveMaterialUsesDatasetTextureWhenPresent()
    {
        ResoniteTexturePayload payload = new(4, 4, "srgb", new byte[4 * 4 * 4], "udx/bldg/53394525/appearance/roof.png");

        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePayload: payload,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Same(payload, material.TexturePayload);
        Assert.Equal(ResoniteTextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Null(material.TextureScale);
        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope);
    }

    [Fact]
    public void ResolveMaterialFallsBackToBundledFacadeForBuildingUvProjection()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePayload: null,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, material.Family);
        Assert.NotNull(material.TextureScale);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
    }

    [Fact]
    public void ResolveMaterialUsesWireframeForOverlayPackages()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "luse",
            texturePayload: null,
            preferUvProjection: false,
            familyOverride: null,
            variantSelectionKey: "luse:tri");

        Assert.Equal(ResoniteMaterialType.Wireframe, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, material.AssetScope);
    }

    [Fact]
    public void ResolveMaterialUsesCityFurnitureFallbackFamily()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "frn",
            texturePayload: null,
            preferUvProjection: false,
            familyOverride: null,
            variantSelectionKey: "frn:tri");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
        Assert.NotNull(material.TextureScale);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.GetVariants(BundledDefaultMaterialFamilies.CityFurniture)[0]),
            material.TextureScale);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
    }

    [Fact]
    public void ResolveMaterialUsesRoadFamilyForPathLikePackageWithoutTexture()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "wwy",
            texturePayload: null,
            preferUvProjection: false,
            familyOverride: null,
            variantSelectionKey: "wwy:tri");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Equal(BundledDefaultMaterialFamilies.Road, material.Family);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
    }
}
