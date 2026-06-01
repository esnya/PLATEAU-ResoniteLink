using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal abstract class CommonMaterialDefinition
{
    private protected static readonly ColorRgba CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    private protected CommonMaterialDefinition(
        MaterialProjection projection,
        string memberName,
        MaterialDepthOffset? depthOffset)
    {
        ArgumentNullException.ThrowIfNull(memberName);

        Projection = projection;
        MemberName = memberName;
        DepthOffset = depthOffset;
    }

    public abstract DefaultCommonMaterialMemberKind Kind { get; }

    public MaterialProjection Projection { get; }

    public string MemberName { get; }

    public MaterialDepthOffset? DepthOffset { get; }

    public virtual string? Family => null;

    public virtual int? BundledVariantIndex => null;

    public virtual BundledDefaultMaterialVariant? BundledVariant => null;

    internal abstract MaterialBinding CreateBinding(
        DefaultCommonMaterialMember member,
        IReadOnlyList<int> submeshIndices);

    private protected static Float2 ToContract(ScalarPair value) => new(value.X, value.Y);
}

internal sealed class BundledCommonMaterialDefinition : CommonMaterialDefinition
{
    private readonly BundledDefaultMaterialVariant bundledVariant;
    private readonly string family;

    internal BundledCommonMaterialDefinition(
        MaterialProjection projection,
        string memberName,
        string family,
        int bundledVariantIndex)
        : base(projection, memberName, depthOffset: null)
    {
        ArgumentNullException.ThrowIfNull(family);

        this.family = family;
        VariantIndex = bundledVariantIndex;
        bundledVariant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, bundledVariantIndex);
    }

    public override DefaultCommonMaterialMemberKind Kind => DefaultCommonMaterialMemberKind.Bundled;

    public override string? Family => family;

    public override int? BundledVariantIndex => VariantIndex;

    public int VariantIndex { get; }

    public override BundledDefaultMaterialVariant? BundledVariant => bundledVariant;

    internal override MaterialBinding CreateBinding(
        DefaultCommonMaterialMember member,
        IReadOnlyList<int> submeshIndices)
    {
        Float2 textureScale = ToContract(bundledVariant.TextureSet.TextureScale);
        Float2? textureOffset = bundledVariant.TextureSet.TextureOffset is null
            ? null
            : ToContract(bundledVariant.TextureSet.TextureOffset);

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
            BundledVariantIndex: VariantIndex,
            CommonMaterial: member);
    }
}

internal sealed class GenericAlbedoCommonMaterialDefinition : CommonMaterialDefinition
{
    internal GenericAlbedoCommonMaterialDefinition(
        string memberName,
        MaterialDepthOffset? depthOffset)
        : base(MaterialProjection.Uv, memberName, depthOffset)
    {
    }

    public override DefaultCommonMaterialMemberKind Kind => DefaultCommonMaterialMemberKind.GenericAlbedo;

    internal override MaterialBinding CreateBinding(
        DefaultCommonMaterialMember member,
        IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: Projection,
            DepthOffset: DepthOffset,
            SubmeshIndices: submeshIndices,
            ReuseScope: MaterialReuseScope.Shared,
            CommonMaterial: member);
    }
}

internal sealed class VertexColorCommonMaterialDefinition : CommonMaterialDefinition
{
    internal VertexColorCommonMaterialDefinition(
        string memberName,
        MaterialDepthOffset? depthOffset)
        : base(MaterialProjection.Uv, memberName, depthOffset)
    {
    }

    public override DefaultCommonMaterialMemberKind Kind => DefaultCommonMaterialMemberKind.VertexColor;

    internal override MaterialBinding CreateBinding(
        DefaultCommonMaterialMember member,
        IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: Projection,
            DepthOffset: DepthOffset,
            SubmeshIndices: submeshIndices,
            ReuseScope: MaterialReuseScope.Shared,
            CommonMaterial: member);
    }
}
