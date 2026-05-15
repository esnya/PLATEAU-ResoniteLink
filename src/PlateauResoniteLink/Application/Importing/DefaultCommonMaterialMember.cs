using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record DefaultCommonMaterialMember(
    DefaultCommonMaterialMemberKind Kind,
    MaterialProjection Projection,
    MaterialDepthOffset? DepthOffset = null,
    string? Family = null,
    int? BundledVariantIndex = null)
{
    private static readonly ColorRgba CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public static DefaultCommonMaterialMember Bundled(string family, int variantIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        _ = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);
        return new DefaultCommonMaterialMember(
            DefaultCommonMaterialMemberKind.Bundled,
            GetBundledProjection(family),
            Family: family,
            BundledVariantIndex: variantIndex);
    }

    public static DefaultCommonMaterialMember GenericUv(MaterialDepthOffset? depthOffset = null) =>
        new(DefaultCommonMaterialMemberKind.GenericAlbedo, MaterialProjection.Uv, depthOffset);

    public static DefaultCommonMaterialMember VertexColorUv(MaterialDepthOffset? depthOffset = null) =>
        new(DefaultCommonMaterialMemberKind.VertexColor, MaterialProjection.Uv, depthOffset);

    public MaterialBinding CreateBinding(IReadOnlyList<int> submeshIndices)
    {
        ArgumentNullException.ThrowIfNull(submeshIndices);

        return Kind switch
        {
            DefaultCommonMaterialMemberKind.Bundled => CreateBundledBinding(submeshIndices),
            DefaultCommonMaterialMemberKind.GenericAlbedo => new MaterialBinding(
                BaseColor: CanonicalBaseColor,
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: Projection,
                DepthOffset: DepthOffset,
                SubmeshIndices: submeshIndices,
                ReuseScope: MaterialReuseScope.Shared,
                CommonMaterial: this),
            DefaultCommonMaterialMemberKind.VertexColor => new MaterialBinding(
                BaseColor: CanonicalBaseColor,
                MaterialType: MaterialType.VertexColor,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Bundled,
                Projection: Projection,
                DepthOffset: DepthOffset,
                SubmeshIndices: submeshIndices,
                ReuseScope: MaterialReuseScope.Shared,
                CommonMaterial: this),
            _ => throw new InvalidOperationException($"Unsupported common material member kind '{Kind}'."),
        };
    }

    private MaterialBinding CreateBundledBinding(IReadOnlyList<int> submeshIndices)
    {
        string family = Family ?? throw new InvalidOperationException("Bundled common material member requires a family.");
        int variantIndex = BundledVariantIndex ?? 0;
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);
        Float2 textureScale = ToContract(variant.TextureSet.TextureScale);
        Float2? textureOffset = variant.TextureSet.TextureOffset is null
            ? null
            : ToContract(variant.TextureSet.TextureOffset);
        return new MaterialBinding(
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: Projection,
            DepthOffset: null,
            SubmeshIndices: submeshIndices,
            TextureScale: textureScale,
            Family: family,
            TextureOffset: textureOffset,
            ReuseScope: MaterialReuseScope.Shared,
            BundledVariantIndex: variantIndex,
            CommonMaterial: this);
    }

    private static MaterialProjection GetBundledProjection(string family)
    {
        return string.Equals(family, BundledDefaultMaterialFamilies.RoadUv, StringComparison.Ordinal)
            || BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(family, StringComparer.Ordinal)
            ? MaterialProjection.Uv
            : MaterialProjection.Triplanar;
    }

    private static Float2 ToContract(ScalarPair value) => new(value.X, value.Y);
}

public enum DefaultCommonMaterialMemberKind
{
    Bundled = 0,
    GenericAlbedo = 1,
    VertexColor = 2,
}
