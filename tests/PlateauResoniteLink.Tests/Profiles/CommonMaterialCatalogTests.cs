using System.Collections.Generic;

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
            material => material.MaterialKey == ResoniteMaterialSharing.CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset: null));
        Assert.Contains(
            materials,
            material => material.MaterialKey == ResoniteMaterialSharing.CreateCanonicalVertexColorCommonMaterialKey(
                ResoniteMaterialProjection.Uv,
                depthOffset: null));
    }

    [Fact]
    public void CreateForPackages_UsesFacadeFloorSquareScaleForBundledFacadeCommonMaterials()
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
            new Float2(
                FacadeMaterialUvScaling.CommonMaterialScale.X,
                FacadeMaterialUvScaling.CommonMaterialScale.Y),
            facadeMaterial.TextureScale);
        Assert.Equal(
            new Float2(
                BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeter.X,
                BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeter.Y),
            roofMaterial.TextureScale);
    }

}
