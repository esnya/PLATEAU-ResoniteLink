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
    public void Create_IncludesCodebaseReachableAlbedoAndVertexColorCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

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
        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
    }

    [Fact]
    public void Create_UsesResolverReachableVariantProfilesForBundledCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);
        MaterialBinding wallMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.WallResidentialPlasterLow
                && material.Projection == MaterialProjection.Uv
                && material.BundledVariantIndex == 0);
        BundledDefaultMaterialProfile wallProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0));

        Assert.Equal(
            new Float2(BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.X, BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.Y),
            roofMaterial.TextureScale);
        Assert.Equal(new Float2(wallProfile.TextureScale.X, wallProfile.TextureScale.Y), wallMaterial.TextureScale);
        Assert.Equal(new Float2(wallProfile.TextureOffset!.X, wallProfile.TextureOffset.Y), wallMaterial.TextureOffset);
        Assert.DoesNotContain(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Facade);
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
    public void Create_IncludesCodebaseReachableBuildingFacadeFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
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
    public void Create_UsesVariantProfilesForBuildingFacadeFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(variants[variantIndex]);
                MaterialBinding material = Assert.Single(
                    materials,
                    candidate => candidate.Family == family
                        && candidate.Projection == MaterialProjection.Uv
                        && candidate.BundledVariantIndex == variantIndex);

                Assert.Equal(new Float2(profile.TextureScale.X, profile.TextureScale.Y), material.TextureScale);
                Assert.Equal(
                    profile.TextureOffset is null
                        ? null
                        : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y),
                    material.TextureOffset);
            }
        }
    }

    [Fact]
    public void GeneratedFacadeProfiles_UseFacadeFloorUnitUvSemantic()
    {
        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            foreach (string variant in BundledDefaultMaterialFamilies.GetVariants(family))
            {
                BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(variant);

                Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, profile.ScaleSemantic);
                Assert.True(profile.TextureScale.X > 0.0);
                Assert.Equal(profile.TextureScale.X, profile.TextureScale.Y, 6);
            }
        }
    }

    [Fact]
    public void Create_UsesTargetCanonicalKeysForBundledCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

        MaterialBinding wallMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.WallResidentialPlasterLow
                && material.Projection == MaterialProjection.Uv
                && material.BundledVariantIndex == 0);
        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);

        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
                0),
            wallMaterial.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                BundledDefaultMaterialFamilies.Roof,
                0),
            roofMaterial.MaterialKey);
    }

    [Fact]
    public void Create_AssignsStableAndDistinctKeysToCodebaseReachableCommonMaterials()
    {
        CommonMaterialCatalog catalog = new();
        IReadOnlyList<MaterialBinding> firstMaterials = catalog.Create();
        IReadOnlyList<MaterialBinding> secondMaterials = catalog.Create();

        MaterialBinding sharedGeneric = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);
        MaterialBinding terrainAlignedSharedGeneric = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
        MaterialBinding sharedVertexColor = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);
        MaterialBinding terrainAlignedSharedVertexColor = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
        MaterialBinding repeatedSharedGeneric = Assert.Single(
            secondMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);

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
            ResoniteSceneMaterialConventions.CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                new ResoniteMaterialDepthOffset(
                    LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset.Factor,
                    LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset.Units)),
            terrainAlignedSharedGeneric.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalVertexColorCommonMaterialKey(
                ResoniteMaterialProjection.Uv,
                depthOffset: null),
            sharedVertexColor.MaterialKey);
        Assert.Equal(
            ResoniteSceneMaterialConventions.CreateCanonicalVertexColorCommonMaterialKey(
                ResoniteMaterialProjection.Uv,
                new ResoniteMaterialDepthOffset(
                    LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset.Factor,
                    LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset.Units)),
            terrainAlignedSharedVertexColor.MaterialKey);
        Assert.NotEqual(sharedVertexColor.MaterialKey, terrainAlignedSharedVertexColor.MaterialKey);
        Assert.DoesNotContain(
            firstMaterials.Where(material => material.Family is not null).Select(static material => material.MaterialKey),
            key => string.Equals(key, sharedGeneric.MaterialKey, StringComparison.Ordinal));
        Assert.DoesNotContain(
            firstMaterials.Where(material => material.Family is not null).Select(static material => material.MaterialKey),
            key => string.Equals(key, sharedVertexColor.MaterialKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Create_IncludesOnlyResolverReachableRoadAndGenericVariants()
    {
        IReadOnlyList<MaterialBinding> materials = new CommonMaterialCatalog().Create();

        Assert.Equal(
            BundledDefaultMaterialFamilies.RoadVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.RoadUv));
        Assert.Equal(
            BundledDefaultMaterialFamilies.RoadVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.RoadTriplanar));
        Assert.Equal(
            BundledDefaultMaterialFamilies.VegetationVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.Vegetation));
        Assert.Equal(
            BundledDefaultMaterialFamilies.CityFurnitureVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.CityFurniture));
        Assert.Contains(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Other
                && material.BundledVariantIndex == BundledDefaultMaterialFamilies.OtherVariants.Count - 1
                && material.Projection == MaterialProjection.Triplanar);
        Assert.DoesNotContain(
            materials,
            material => material.Family is BundledDefaultMaterialFamilies.Vegetation
                    or BundledDefaultMaterialFamilies.CityFurniture
                    or BundledDefaultMaterialFamilies.Other
                && material.Projection == MaterialProjection.Uv);
        Assert.Contains(
            BundledDefaultMaterialFamilies.OtherVariants,
            variant => variant.TexturePath.StartsWith("default-materials/texturecan/", StringComparison.Ordinal));
    }
}
