using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed class DefaultCommonMaterialMember : IEquatable<DefaultCommonMaterialMember>
{
    private static readonly ColorRgba CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    private DefaultCommonMaterialMember(CommonMaterialDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public DefaultCommonMaterialMemberKind Kind => Definition.Kind;

    public MaterialProjection Projection => Definition.Projection;

    public MaterialDepthOffset? DepthOffset => Definition.DepthOffset;

    public string? Family => Definition.Family;

    public int? BundledVariantIndex => Definition.BundledVariantIndex;

    internal CommonMaterialDefinition Definition { get; }

    internal BundledDefaultMaterialVariant? BundledVariant => Definition.BundledVariant;

    internal static DefaultCommonMaterialMember Create(CommonMaterialDefinition definition)
    {
        return new DefaultCommonMaterialMember(definition);
    }

    public bool Equals(DefaultCommonMaterialMember? other)
    {
        return other is not null && ReferenceEquals(Definition, other.Definition);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as DefaultCommonMaterialMember);
    }

    public override int GetHashCode()
    {
        return Definition.GetHashCode();
    }

    public MaterialBinding CreateBinding(IReadOnlyList<int> submeshIndices)
    {
        ArgumentNullException.ThrowIfNull(submeshIndices);

        return Kind switch
        {
            DefaultCommonMaterialMemberKind.Bundled => CreateBundledBinding(submeshIndices),
            DefaultCommonMaterialMemberKind.GenericAlbedo => new SharedCommonMaterialBinding(
                BaseColor: CanonicalBaseColor,
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: Projection,
                DepthOffset: DepthOffset,
                SubmeshIndices: submeshIndices,
                commonMaterial: this),
            DefaultCommonMaterialMemberKind.VertexColor => new SharedCommonMaterialBinding(
                BaseColor: CanonicalBaseColor,
                MaterialType: MaterialType.VertexColor,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Bundled,
                Projection: Projection,
                DepthOffset: DepthOffset,
                SubmeshIndices: submeshIndices,
                commonMaterial: this),
            _ => throw new InvalidOperationException($"Unsupported common material member kind '{Kind}'."),
        };
    }

    private SharedCommonMaterialBinding CreateBundledBinding(IReadOnlyList<int> submeshIndices)
    {
        string family = Family ?? throw new InvalidOperationException("Bundled common material member requires a family.");
        int variantIndex = BundledVariantIndex ?? 0;
        BundledDefaultMaterialVariant variant = BundledVariant
            ?? throw new InvalidOperationException("Bundled common material member requires a variant.");
        Float2 textureScale = ToContract(variant.TextureSet.TextureScale);
        Float2? textureOffset = variant.TextureSet.TextureOffset is null
            ? null
            : ToContract(variant.TextureSet.TextureOffset);
        return new SharedCommonMaterialBinding(
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
            commonMaterial: this,
            BundledVariantIndex: variantIndex);
    }

    private static Float2 ToContract(ScalarPair value) => new(value.X, value.Y);
}

public enum DefaultCommonMaterialMemberKind
{
    Bundled = 0,
    GenericAlbedo = 1,
    VertexColor = 2,
}
