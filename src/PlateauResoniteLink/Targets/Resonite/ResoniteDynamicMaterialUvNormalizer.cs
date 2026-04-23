using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public static class ResoniteDynamicMaterialUvNormalizer
{
    public static bool ShouldBakeTextureTransform(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && material.AssetScope != ResoniteMaterialAssetScope.Common
            && HasBakeableTextureTransform(material);
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

        (ResoniteFloat2 bakeScale, ResoniteFloat2 bakeOffset) = CreateBakeTransform(material);
        return new ResoniteFloat2(
            (sourceUv.X * bakeScale.X) + bakeOffset.X,
            (sourceUv.Y * bakeScale.Y) + bakeOffset.Y);
    }

    private static bool HasBakeableTextureTransform(ResoniteMaterialBinding material)
    {
        return IsBundledFamilyMaterial(material)
            ? material.TextureScale is not null || material.TextureOffset is not null
            : HasEffectiveTextureTransform(material);
    }

    private static bool HasEffectiveTextureTransform(ResoniteMaterialBinding material)
    {
        return !IsIdentityTextureScale(material.TextureScale)
            || !IsZeroTextureOffset(material.TextureOffset);
    }

    private static (ResoniteFloat2 Scale, ResoniteFloat2 Offset) CreateBakeTransform(ResoniteMaterialBinding material)
    {
        if (!IsBundledFamilyMaterial(material))
        {
            return (
                material.TextureScale ?? new ResoniteFloat2(1.0, 1.0),
                material.TextureOffset ?? new ResoniteFloat2(0.0, 0.0));
        }

        string bundledVariantPath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0);
        ScalarPair implicitScaleValue = BundledDefaultMaterialProfiles.GetTilesPerMeterValue(bundledVariantPath);
        ResoniteFloat2 implicitScale = new(implicitScaleValue.X, implicitScaleValue.Y);
        ResoniteFloat2 explicitScale = material.TextureScale ?? implicitScale;
        ResoniteFloat2 explicitOffset = material.TextureOffset ?? new ResoniteFloat2(0.0, 0.0);
        return (
            new ResoniteFloat2(
                explicitScale.X / implicitScale.X,
                explicitScale.Y / implicitScale.Y),
            new ResoniteFloat2(
                explicitOffset.X / implicitScale.X,
                explicitOffset.Y / implicitScale.Y));
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
