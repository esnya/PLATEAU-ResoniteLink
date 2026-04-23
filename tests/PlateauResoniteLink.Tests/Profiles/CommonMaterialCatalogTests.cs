using System.Collections.Generic;
using System.Globalization;

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

            Assert.Equal(expectedScale, facadeMaterial.TextureScale);
            Assert.Null(facadeMaterial.TextureOffset);
            Assert.Equal(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"common|facade|variant:{variantIndex}|Uv|scale:{expectedScale.X:0.######}x{expectedScale.Y:0.######}|offset:none"),
                facadeMaterial.MaterialKey);
        }

        Assert.Equal(
            new Float2(BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.X, BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.Y),
            roofMaterial.TextureScale);
    }

    private static Float2 ExpectedFacadeScale(int variantIndex)
    {
        return variantIndex == 0
            ? new Float2(1.0 / 52.0, 1.0 / 32.5)
            : new Float2(1.0 / 19.5, 1.0 / 19.5);
    }
}
