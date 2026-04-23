using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultMaterialResolverTests
{
    private readonly DefaultMaterialResolver resolver = new();

    [Fact]
    public void ResolveMaterialUsesDatasetTextureWhenPresent()
    {
        TexturePayload payload = new(4, 4, "srgb", new byte[4 * 4 * 4], "udx/bldg/53394525/appearance/roof.png");

        ResolvedMaterial material = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePayload: payload,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Same(payload, material.TexturePayload);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Null(material.TextureScale);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
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
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(
            BundledDefaultMaterialFamilies.Facade,
            material.BundledVariantIndex!.Value);
        BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(texturePath);

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.Facade, material.Family);
        Assert.Equal(
            new Float2(
                profile.TextureScale.X,
                profile.TextureScale.Y),
            material.TextureScale);
        Assert.Equal(
            profile.TextureOffset is null
                ? null
                : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y),
            material.TextureOffset);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
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

        Assert.Equal(MaterialType.Wireframe, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
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

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Triplanar, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
        Assert.NotNull(material.TextureScale);
        Assert.Equal(
            ToContractFloat2(BundledDefaultMaterialProfiles.GetTilesPerMeterValue(BundledDefaultMaterialFamilies.GetVariants(BundledDefaultMaterialFamilies.CityFurniture)[0])),
            material.TextureScale);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
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

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Equal(BundledDefaultMaterialFamilies.Road, material.Family);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialUsesStableBundledVariantSelection()
    {
        ResolvedMaterial first = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePayload: null,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");
        ResolvedMaterial second = resolver.ResolveMaterial(
            packageName: "bldg",
            texturePayload: null,
            preferUvProjection: true,
            familyOverride: null,
            variantSelectionKey: "bldg:uv");

        Assert.Equal(BundledDefaultMaterialFamilies.Facade, first.Family);
        Assert.Equal(first.BundledVariantIndex, second.BundledVariantIndex);
        Assert.Equal(first.TextureScale, second.TextureScale);
        Assert.Equal(first.TextureOffset, second.TextureOffset);
        Assert.Equal(
            ToContractFloat2(BundledDefaultMaterialProfiles.GetProfile(
                BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Facade, first.BundledVariantIndex!.Value)).TextureScale),
            first.TextureScale);
        Assert.Equal(
            ToContractFloat2Nullable(BundledDefaultMaterialProfiles.GetProfile(
                BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Facade, first.BundledVariantIndex!.Value)).TextureOffset),
            first.TextureOffset);
    }

    [Fact]
    public void ResolveMaterialCanReachEveryFacadeVariantWithExpectedNormalizedScale()
    {
        Dictionary<int, ResolvedMaterial> materialsByVariant = [];
        for (int attempt = 0; attempt < 256 && materialsByVariant.Count < BundledDefaultMaterialFamilies.FacadeVariants.Count; attempt++)
        {
            string variantSelectionKey = $"bldg:uv:{attempt}";
            ResolvedMaterial material = resolver.ResolveMaterial(
                packageName: "bldg",
                texturePayload: null,
                preferUvProjection: true,
                familyOverride: null,
                variantSelectionKey: variantSelectionKey);
            materialsByVariant.TryAdd(material.BundledVariantIndex!.Value, material);
        }

        Assert.Equal(BundledDefaultMaterialFamilies.FacadeVariants.Count, materialsByVariant.Count);
        foreach (ResolvedMaterial material in materialsByVariant.Values)
        {
            Assert.Equal(new Float2(1.0 / 6.0, 1.0 / 6.0), material.TextureScale);
            Assert.Equal(new Float2(0.0, 0.5 / 6.0), material.TextureOffset);
        }
    }

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float2? ToContractFloat2Nullable(ScalarPair? value)
    {
        return value is null ? null : new Float2(value.X, value.Y);
    }
}
