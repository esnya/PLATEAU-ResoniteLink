using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CommonMaterialDefinition
{
    internal CommonMaterialDefinition(
        DefaultCommonMaterialMemberKind kind,
        MaterialProjection projection,
        string memberName,
        MaterialDepthOffset? depthOffset = null,
        string? family = null,
        int? bundledVariantIndex = null)
    {
        Kind = kind;
        Projection = projection;
        MemberName = memberName;
        DepthOffset = depthOffset;
        Family = family;
        BundledVariantIndex = bundledVariantIndex;
        BundledVariant = family is null || bundledVariantIndex is null
            ? null
            : BundledDefaultMaterialFamilies.GetVariantDefinition(family, bundledVariantIndex.Value);
    }

    public DefaultCommonMaterialMemberKind Kind { get; }

    public MaterialProjection Projection { get; }

    public string MemberName { get; }

    public MaterialDepthOffset? DepthOffset { get; }

    public string? Family { get; }

    public int? BundledVariantIndex { get; }

    public BundledDefaultMaterialVariant? BundledVariant { get; }
}
