using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Profiles;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CommonMaterialCatalogTests
{
    [Fact]
    public void CreateForPackages_IncludesSharedAlbedoAndVertexColorCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);

        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset is null
                && material.Family is null);
        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset is null);
    }

    [Fact]
    public void CreateForPackages_UsesFacadeVariantProfileForBundledFacadeCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);

        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);

        for (int variantIndex = 0; variantIndex < BundledDefaultMaterialFamilies.FacadeVariants.Count; variantIndex++)
        {
            MaterialBinding facadeMaterial = Assert.Single(
                materials,
                material => material.Family == BundledDefaultMaterialFamilies.Facade
                    && material.Projection == MaterialProjection.Uv
                    && material.BundledVariantIndex == variantIndex);
            Float2 expectedScale = ExpectedFacadeScale(variantIndex);
            Float2? expectedOffset = ExpectedFacadeOffset(variantIndex);

            Assert.Equal(expectedScale, facadeMaterial.TextureScale);
            Assert.Equal(expectedOffset, facadeMaterial.TextureOffset);
            Assert.Equal(
                ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                    BundledDefaultMaterialFamilies.Facade,
                    variantIndex,
                    ResoniteMaterialProjection.Uv,
                    new ResoniteFloat2(expectedScale.X, expectedScale.Y),
                    expectedOffset is null
                        ? null
                        : new ResoniteFloat2(expectedOffset.X, expectedOffset.Y)),
                facadeMaterial.MaterialKey);
        }

        Assert.Equal(
            new Float2(BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.X, BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.Y),
            roofMaterial.TextureScale);
        Assert.DoesNotContain(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Uv);
    }

    [Fact]
    public void BundledFacadeProfiles_DeclareFacadeFloorUnitScaleSemantic()
    {
        BundledDefaultMaterialProfile facadeProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Facade, 0));
        BundledDefaultMaterialProfile roofProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Roof, 0));

        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, facadeProfile.ScaleSemantic);
        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.WorldMeters, roofProfile.ScaleSemantic);
    }

    [Fact]
    public void CreateForPackages_PrecreatesReachableBuildingWallSkinFallbackFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);

        foreach (string family in BundledDefaultMaterialFamilies.BuildingWallSkinFamilies)
        {
            Assert.Equal(
                BundledDefaultMaterialFamilies.GetVariants(family).Count,
                materials.Count(material => material.Family == family && material.Projection == MaterialProjection.Uv));
            Assert.DoesNotContain(
                materials,
                material => material.Family == family && material.Projection == MaterialProjection.Triplanar);
        }
    }

    [Fact]
    public void CreateForPackages_PrecreatesReachableBuildingFacadeFallbackFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);

        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFallbackFamilies)
        {
            Assert.Equal(
                BundledDefaultMaterialFamilies.GetVariants(family).Count,
                materials.Count(material => material.Family == family && material.Projection == MaterialProjection.Uv));
            Assert.DoesNotContain(
                materials,
                material => material.Family == family && material.Projection == MaterialProjection.Triplanar);
        }
    }

    [Fact]
    public void WallSkinProfiles_UseFacadeFloorUnitUvSemantic()
    {
        foreach (string family in BundledDefaultMaterialFamilies.BuildingWallSkinFamilies)
        {
            foreach (string variant in BundledDefaultMaterialFamilies.GetVariants(family))
            {
                BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(variant);

                Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, profile.ScaleSemantic);
                Assert.True(profile.TextureScale.X > 0.0);
                Assert.Equal(profile.TextureScale.X, profile.TextureScale.Y, 6);
                Assert.Null(profile.TextureOffset);
            }
        }
    }

    private static Float2 ExpectedFacadeScale(int variantIndex)
    {
        _ = variantIndex;
        return new Float2(1.0 / 6.0, 1.0 / 6.0);
    }

    private static Float2? ExpectedFacadeOffset(int variantIndex)
    {
        _ = variantIndex;
        return new Float2(0.0, 0.5 / 6.0);
    }

    [Fact]
    public void CreateForPackages_UsesTargetCanonicalKeysForBundledCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["bldg"]);

        MaterialBinding facadeMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Facade
                && material.Projection == MaterialProjection.Uv
                && material.BundledVariantIndex == 0);
        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);

        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                BundledDefaultMaterialFamilies.Facade,
                0,
                ResoniteMaterialProjection.Uv,
                new ResoniteFloat2(facadeMaterial.TextureScale!.X, facadeMaterial.TextureScale.Y),
                new ResoniteFloat2(facadeMaterial.TextureOffset!.X, facadeMaterial.TextureOffset.Y)),
            facadeMaterial.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                BundledDefaultMaterialFamilies.Roof,
                0,
                ResoniteMaterialProjection.Triplanar,
                new ResoniteFloat2(roofMaterial.TextureScale!.X, roofMaterial.TextureScale.Y),
                textureOffset: null),
            roofMaterial.MaterialKey);
    }

    [Fact]
    public void CreateForPackages_AssignsStableAndDistinctKeysToSharedCommonMaterials()
    {
        CommonMaterialCatalog catalog = new();
        IReadOnlyList<MaterialBinding> firstMaterials = catalog.CreateForPackages(["bldg"]);
        IReadOnlyList<MaterialBinding> secondMaterials = catalog.CreateForPackages(["bldg"]);

        MaterialBinding sharedGeneric = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv);
        MaterialBinding sharedVertexColor = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv);
        MaterialBinding repeatedSharedGeneric = Assert.Single(
            secondMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv);

        Assert.Equal(sharedGeneric.MaterialKey, repeatedSharedGeneric.MaterialKey);
        Assert.NotEqual(sharedGeneric.MaterialKey, sharedVertexColor.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset: null),
            sharedGeneric.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalVertexColorCommonMaterialKey(
                ResoniteMaterialProjection.Uv,
                depthOffset: null),
            sharedVertexColor.MaterialKey);
        Assert.DoesNotContain(
            firstMaterials.Where(material => material.Family is not null).Select(static material => material.MaterialKey),
            key => string.Equals(key, sharedGeneric.MaterialKey, StringComparison.Ordinal));
        Assert.DoesNotContain(
            firstMaterials.Where(material => material.Family is not null).Select(static material => material.MaterialKey),
            key => string.Equals(key, sharedVertexColor.MaterialKey, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateForPackages_IncludesExpandedRoadAndGenericVariants()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().CreateForPackages(["tran", "frn", "brid"]);

        Assert.Equal(
            BundledDefaultMaterialFamilies.RoadVariants.Count * 2,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.Road));
        Assert.Equal(
            BundledDefaultMaterialFamilies.CityFurnitureVariants.Count * 2,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.CityFurniture));
        Assert.Contains(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Other
                && material.BundledVariantIndex == BundledDefaultMaterialFamilies.OtherVariants.Count - 1
                && material.Projection == MaterialProjection.Uv);
        Assert.Contains(
            BundledDefaultMaterialFamilies.OtherVariants,
            path => path.StartsWith("default-materials/texturecan/", StringComparison.Ordinal));
    }
}
