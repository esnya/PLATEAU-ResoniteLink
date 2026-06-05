using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed class DefaultCommonMaterialMember : IEquatable<DefaultCommonMaterialMember>
{
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

        return Definition.CreateBinding(this, submeshIndices);
    }
}

public enum DefaultCommonMaterialMemberKind
{
    Bundled = 0,
    GenericAlbedo = 1,
    VertexColor = 2,
}
