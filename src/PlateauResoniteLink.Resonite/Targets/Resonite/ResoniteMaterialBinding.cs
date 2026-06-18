using System;
using System.Collections.Generic;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

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

    public DefaultCommonMaterialMember? CommonMaterial => this is ICommonMaterialAssetBinding commonBinding
        ? commonBinding.Member
        : null;

    public bool IsSharedCommon => this is SharedCommonMaterialAssetBinding;

    private interface ICommonMaterialAssetBinding
    {
        DefaultCommonMaterialMember Member { get; }
    }

    private sealed record PresentationMaterialAssetBinding : ResoniteMaterialAssetBinding;

    private sealed record SharedMaterialAssetBinding : ResoniteMaterialAssetBinding;

    private sealed record PresentationCommonMaterialAssetBinding : ResoniteMaterialAssetBinding, ICommonMaterialAssetBinding
    {
        public PresentationCommonMaterialAssetBinding(DefaultCommonMaterialMember commonMaterial)
        {
            ArgumentNullException.ThrowIfNull(commonMaterial);
            Member = commonMaterial;
        }

        public DefaultCommonMaterialMember Member { get; }
    }

    private sealed record SharedCommonMaterialAssetBinding : ResoniteMaterialAssetBinding, ICommonMaterialAssetBinding
    {
        public SharedCommonMaterialAssetBinding(DefaultCommonMaterialMember commonMaterial)
        {
            ArgumentNullException.ThrowIfNull(commonMaterial);
            Member = commonMaterial;
        }

        public DefaultCommonMaterialMember Member { get; }
    }
}
