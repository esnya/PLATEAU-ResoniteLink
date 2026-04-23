using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public static class ImportedDynamicMaterialUvNormalizer
{
    public static bool ShouldBakeTextureTransform(MaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == MaterialType.Standard
            && material.Projection == MaterialProjection.Uv
            && material.ReuseScope != MaterialReuseScope.Shared
            && HasBakeableTextureTransform(material);
    }

    public static ImportedCityObject Normalize(ImportedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (cityObject.Geometry is not TriangleMeshGeometry triangleMesh
            || !cityObject.Materials.Any(ShouldBakeTextureTransform))
        {
            return cityObject;
        }

        Dictionary<int, MaterialBinding> materialBySubmeshIndex = cityObject.Materials
            .SelectMany(material => material.SubmeshIndices.Select(submeshIndex => (submeshIndex, material)))
            .ToDictionary(static pair => pair.submeshIndex, static pair => pair.material);

        List<MeshVertex> normalizedVertices = [];
        List<MeshSubmesh> normalizedSubmeshes = [];
        foreach (MeshSubmesh submesh in triangleMesh.Mesh.Submeshes)
        {
            materialBySubmeshIndex.TryGetValue(submesh.Index, out MaterialBinding? material);
            List<int> normalizedTriangleVertexIndices = new(submesh.TriangleVertexIndices.Count);
            foreach (int sourceIndex in submesh.TriangleVertexIndices)
            {
                MeshVertex sourceVertex = triangleMesh.Mesh.Vertices[sourceIndex];
                Float2 normalizedUv = material is not null && ShouldBakeTextureTransform(material)
                    ? ApplyTextureTransform(sourceVertex.UV0, material)
                    : sourceVertex.UV0;
                normalizedVertices.Add(sourceVertex with { UV0 = normalizedUv });
                normalizedTriangleVertexIndices.Add(normalizedVertices.Count - 1);
            }

            normalizedSubmeshes.Add(submesh with { TriangleVertexIndices = normalizedTriangleVertexIndices });
        }

        return cityObject with
        {
            Geometry = new TriangleMeshGeometry(new ImportedMesh(normalizedVertices, normalizedSubmeshes)),
            Materials = cityObject.Materials
                .Select(NormalizeMaterialBinding)
                .ToArray(),
        };
    }

    public static MaterialBinding NormalizeMaterialBinding(MaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (material.MaterialType != MaterialType.Standard
            || material.Projection != MaterialProjection.Uv)
        {
            return material;
        }

        if (!HasEffectiveTextureTransform(material))
        {
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

    public static Float2 ApplyTextureTransform(
        Float2 sourceUv,
        MaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(sourceUv);
        ArgumentNullException.ThrowIfNull(material);

        (Float2 bakeScale, Float2 bakeOffset) = CreateBakeTransform(material);
        return new Float2(
            (sourceUv.X * bakeScale.X) + bakeOffset.X,
            (sourceUv.Y * bakeScale.Y) + bakeOffset.Y);
    }

    private static bool HasBakeableTextureTransform(MaterialBinding material)
    {
        return IsBundledFamilyMaterial(material)
            ? material.TextureScale is not null || material.TextureOffset is not null
            : HasEffectiveTextureTransform(material);
    }

    private static bool HasEffectiveTextureTransform(MaterialBinding material)
    {
        return !IsIdentityTextureScale(material.TextureScale)
            || !IsZeroTextureOffset(material.TextureOffset);
    }

    private static (Float2 Scale, Float2 Offset) CreateBakeTransform(MaterialBinding material)
    {
        if (!IsBundledFamilyMaterial(material))
        {
            return (
                material.TextureScale ?? new Float2(1.0, 1.0),
                material.TextureOffset ?? new Float2(0.0, 0.0));
        }

        string bundledVariantPath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0);
        ScalarPair implicitScaleValue = BundledDefaultMaterialProfiles.GetTilesPerMeterValue(bundledVariantPath);
        Float2 implicitScale = new(implicitScaleValue.X, implicitScaleValue.Y);
        Float2 explicitScale = material.TextureScale ?? implicitScale;
        Float2 explicitOffset = material.TextureOffset ?? new Float2(0.0, 0.0);
        return (
            new Float2(
                explicitScale.X / implicitScale.X,
                explicitScale.Y / implicitScale.Y),
            new Float2(
                explicitOffset.X / implicitScale.X,
                explicitOffset.Y / implicitScale.Y));
    }

    private static bool IsBundledFamilyMaterial(MaterialBinding material)
    {
        return material.TextureSourceKind == TextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family);
    }

    private static bool IsIdentityTextureScale(Float2? textureScale)
    {
        return textureScale is null
            || (Math.Abs(textureScale.X - 1.0) < 1e-9
                && Math.Abs(textureScale.Y - 1.0) < 1e-9);
    }

    private static bool IsZeroTextureOffset(Float2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }
}
