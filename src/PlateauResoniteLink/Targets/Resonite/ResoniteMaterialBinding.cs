using System;
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
    ResoniteMaterialAssetBinding AssetBinding,
    ResoniteFloat2? TextureScale = null,
    string? Family = null,
    ResoniteFloat2? TextureOffset = null,
    TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
    int? BundledVariantIndex = null)
{
    public TerrainTextureOverlay? TerrainOverlay => TerrainOverlayMaterial?.Overlay;

    public string? TerrainMeshCode => TerrainOverlayMaterial?.MeshCode.Value;

    public ResoniteMaterialAssetScope AssetScope => AssetBinding.AssetScope;

    public DefaultCommonMaterialMember? CommonMaterial => AssetBinding.CommonMaterial;
}

public abstract record ResoniteMaterialAssetBinding
{
    private ResoniteMaterialAssetBinding()
    {
    }

    public static ResoniteMaterialAssetBinding Presentation { get; } = new PresentationMaterialAssetBinding();

    public static ResoniteMaterialAssetBinding Shared { get; } = new SharedMaterialAssetBinding();

    public static ResoniteMaterialAssetBinding PresentationCommon(DefaultCommonMaterialMember commonMaterial)
        => new PresentationCommonMaterialAssetBinding(commonMaterial);

    public static ResoniteMaterialAssetBinding SharedCommon(DefaultCommonMaterialMember commonMaterial)
        => new SharedCommonMaterialAssetBinding(commonMaterial);

    public ResoniteMaterialAssetScope AssetScope => this is SharedMaterialAssetBinding or SharedCommonMaterialAssetBinding
        ? ResoniteMaterialAssetScope.Common
        : ResoniteMaterialAssetScope.PresentationSlotScoped;

    public DefaultCommonMaterialMember? CommonMaterial => this switch
    {
        PresentationCommonMaterialAssetBinding presentationCommon => presentationCommon.Member,
        SharedCommonMaterialAssetBinding sharedCommon => sharedCommon.Member,
        _ => null,
    };

    public bool IsSharedCommon => this is SharedCommonMaterialAssetBinding;

    private sealed record PresentationMaterialAssetBinding : ResoniteMaterialAssetBinding;

    private sealed record SharedMaterialAssetBinding : ResoniteMaterialAssetBinding;

    private sealed record PresentationCommonMaterialAssetBinding : ResoniteMaterialAssetBinding
    {
        public PresentationCommonMaterialAssetBinding(DefaultCommonMaterialMember commonMaterial)
        {
            ArgumentNullException.ThrowIfNull(commonMaterial);
            Member = commonMaterial;
        }

        public DefaultCommonMaterialMember Member { get; }
    }

    private sealed record SharedCommonMaterialAssetBinding : ResoniteMaterialAssetBinding
    {
        public SharedCommonMaterialAssetBinding(DefaultCommonMaterialMember commonMaterial)
        {
            ArgumentNullException.ThrowIfNull(commonMaterial);
            Member = commonMaterial;
        }

        public DefaultCommonMaterialMember Member { get; }
    }
}
