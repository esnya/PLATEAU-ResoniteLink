using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DefaultMaterialResolverTests
{
    private readonly DefaultMaterialResolver resolver = new();

    [Fact]
    public void ResolveMaterialUsesDatasetTextureWhenPresent()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePath: "udx/bldg/53394525/appearance/roof.png",
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Equal("udx/bldg/53394525/appearance/roof.png", material.TexturePath);
        Assert.Equal(ResoniteTextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Null(material.TextureScale);
    }

    [Fact]
    public void ResolveMaterialFallsBackToBundledFacadeForBuildingUvProjection()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePath: null,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Contains(material.TexturePath, BundledDefaultMaterialFamilies.FacadeVariants);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, material.Family);
        Assert.NotNull(material.TextureScale);
    }

    [Fact]
    public void ResolveMaterialUsesWireframeForOverlayPackages()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "luse",
            texturePath: null,
            preferUvProjection: false,
            familyOverride: null,
            variantSelectionKey: "luse:tri");

        Assert.Equal(ResoniteMaterialType.Wireframe, material.MaterialType);
        Assert.Null(material.TexturePath);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
    }

    [Fact]
    public void ResolveMaterialUsesFacade001FallbackForCityFurniture()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "frn",
            texturePath: null,
            preferUvProjection: false,
            familyOverride: null,
            variantSelectionKey: "frn:tri");

        Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
        Assert.Equal("default-materials/facade/Facade001_2K-JPG_Color.jpg", material.TexturePath);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
        Assert.NotNull(material.TextureScale);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).X,
            material.TextureScale!.X,
            6);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).Y,
            material.TextureScale.Y,
            6);
    }
}
