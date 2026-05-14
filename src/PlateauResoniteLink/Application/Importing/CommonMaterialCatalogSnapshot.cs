using System;
using System.Collections;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed class CommonMaterialCatalogSnapshot : IReadOnlyList<MaterialBinding>
{
    private readonly IReadOnlyList<MaterialBinding> materials;

    internal CommonMaterialCatalogSnapshot(IReadOnlyList<MaterialBinding> materials)
    {
        RejectDuplicateDefinitions(materials);
        this.materials = materials;
    }

    public static CommonMaterialCatalogSnapshot Empty { get; } = new([]);

    public int Count => materials.Count;

    public MaterialBinding this[int index] => materials[index];

    public IEnumerator<MaterialBinding> GetEnumerator() => materials.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void RejectDuplicateDefinitions(IReadOnlyList<MaterialBinding> materials)
    {
        HashSet<CommonMaterialCatalogItemDefinition> definitions = [];
        foreach (MaterialBinding material in materials)
        {
            if (!definitions.Add(CommonMaterialCatalogItemDefinition.Create(material)))
            {
                throw new InvalidOperationException("Common material catalog contains duplicate material definitions.");
            }
        }
    }

    private readonly record struct CommonMaterialCatalogItemDefinition(
        MaterialType MaterialType,
        TextureSourceKind TextureSourceKind,
        MaterialProjection Projection,
        MaterialDepthOffset? DepthOffset,
        Float2? TextureScale,
        string? Family,
        Float2? TextureOffset,
        int? BundledVariantIndex,
        string? TerrainMeshCode)
    {
        public static CommonMaterialCatalogItemDefinition Create(MaterialBinding material)
        {
            return new CommonMaterialCatalogItemDefinition(
                material.MaterialType,
                material.TextureSourceKind,
                material.Projection,
                material.DepthOffset,
                material.TextureScale,
                material.Family,
                material.TextureOffset,
                material.BundledVariantIndex,
                material.TerrainMeshCode);
        }
    }
}
