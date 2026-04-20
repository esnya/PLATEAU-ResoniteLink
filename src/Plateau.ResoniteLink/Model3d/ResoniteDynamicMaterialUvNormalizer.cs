namespace Plateau.ResoniteLink.Domain.Importing;

public static class ResoniteDynamicMaterialUvNormalizer
{
    public static bool ShouldBakeTextureTransform(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && material.TerrainOverlay is null
            && material.AssetScope != ResoniteMaterialAssetScope.Common
            && !(material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
                && !string.IsNullOrWhiteSpace(material.Family))
            && HasEffectiveTextureTransform(material);
    }

    public static ResoniteConstructionCityObject Normalize(ResoniteConstructionCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (cityObject.Geometry is not ResoniteTriangleMeshGeometry triangleMesh
            || !cityObject.Materials.Any(ShouldBakeTextureTransform))
        {
            return cityObject;
        }

        Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex = cityObject.Materials
            .SelectMany(material => material.SubmeshIndices.Select(submeshIndex => (submeshIndex, material)))
            .ToDictionary(static pair => pair.submeshIndex, static pair => pair.material);

        List<ResoniteMeshVertex> normalizedVertices = [];
        List<ResoniteMeshSubmesh> normalizedSubmeshes = [];
        foreach (ResoniteMeshSubmesh submesh in triangleMesh.Mesh.Submeshes)
        {
            materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material);
            List<int> normalizedTriangleVertexIndices = new(submesh.TriangleVertexIndices.Count);
            foreach (int sourceIndex in submesh.TriangleVertexIndices)
            {
                ResoniteMeshVertex sourceVertex = triangleMesh.Mesh.Vertices[sourceIndex];
                ResoniteFloat2 normalizedUv = material is not null && ShouldBakeTextureTransform(material)
                    ? ApplyTextureTransform(sourceVertex.UV0, material)
                    : sourceVertex.UV0;
                normalizedVertices.Add(sourceVertex with { UV0 = normalizedUv });
                normalizedTriangleVertexIndices.Add(normalizedVertices.Count - 1);
            }

            normalizedSubmeshes.Add(submesh with { TriangleVertexIndices = normalizedTriangleVertexIndices });
        }

        return cityObject with
        {
            Geometry = new ResoniteTriangleMeshGeometry(new ResoniteImportedMesh(normalizedVertices, normalizedSubmeshes)),
            Materials = cityObject.Materials
                .Select(NormalizeMaterialBinding)
                .ToArray(),
        };
    }

    public static ResoniteMaterialBinding NormalizeMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.Projection != ResoniteMaterialProjection.Uv)
        {
            return material;
        }

        if (!HasEffectiveTextureTransform(material))
        {
            if (IsBundledFamilyMaterial(material) && material.TextureScale is not null)
            {
                return material with
                {
                    TextureOffset = null,
                };
            }

            return material with
            {
                TextureScale = null,
                TextureOffset = null,
            };
        }

        return ShouldBakeTextureTransform(material)
            ? material with
            {
                TextureScale = null,
                TextureOffset = null,
            }
            : material;
    }

    public static ResoniteFloat2 ApplyTextureTransform(
        ResoniteFloat2 sourceUv,
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(sourceUv);
        ArgumentNullException.ThrowIfNull(material);

        double scaleX = material.TextureScale?.X ?? 1.0;
        double scaleY = material.TextureScale?.Y ?? 1.0;
        double offsetX = material.TextureOffset?.X ?? 0.0;
        double offsetY = material.TextureOffset?.Y ?? 0.0;
        return new ResoniteFloat2(
            (sourceUv.X * scaleX) + offsetX,
            (sourceUv.Y * scaleY) + offsetY);
    }

    private static bool HasEffectiveTextureTransform(ResoniteMaterialBinding material)
    {
        return !IsIdentityTextureScale(material.TextureScale)
            || !IsZeroTextureOffset(material.TextureOffset);
    }

    private static bool IsBundledFamilyMaterial(ResoniteMaterialBinding material)
    {
        return material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family);
    }

    private static bool IsIdentityTextureScale(ResoniteFloat2? textureScale)
    {
        return textureScale is null
            || (Math.Abs(textureScale.X - 1.0) < 1e-9
                && Math.Abs(textureScale.Y - 1.0) < 1e-9);
    }

    private static bool IsZeroTextureOffset(ResoniteFloat2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }
}
