using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteMaterialBinding(
    ResoniteColor BaseColor,
    ResoniteMaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    ResoniteMaterialDepthOffset? DepthOffset,
    IReadOnlyList<int> SubmeshIndices,
    ResoniteFloat2? TextureScale = null,
    string? Family = null,
    ResoniteFloat2? TextureOffset = null,
    ResoniteMaterialAssetScope AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped,
    TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
    int? BundledVariantIndex = null,
    DefaultCommonMaterialMember? CommonMaterial = null)
{
    public TerrainTextureOverlay? TerrainOverlay => TerrainOverlayMaterial?.Overlay;

    public string? TerrainMeshCode => TerrainOverlayMaterial?.MeshCode.Value;
}
