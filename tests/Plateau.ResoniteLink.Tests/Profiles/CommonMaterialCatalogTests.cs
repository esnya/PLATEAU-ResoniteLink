using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Profiles;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CommonMaterialCatalogTests
{
    [Fact]
    public void CreateForPackages_IncludesSharedAlbedoAndVertexColorCommonMaterials()
    {
        IReadOnlyList<ResoniteMaterialBinding> materials = CommonMaterialCatalog.CreateForPackages(["bldg"]);

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

}
