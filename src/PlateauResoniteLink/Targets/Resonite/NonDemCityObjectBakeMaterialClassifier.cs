using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemCityObjectBakeMaterialClassifier
{
    public static bool CanBufferCityObjectMaterials(
        ResoniteConstructionCityObject cityObject,
        NonDemCityObjectBakePolicy policy)
    {
        if (!TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex))
        {
            return false;
        }

        bool hasAtlasCandidateSubmesh = false;
        foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes)
        {
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                return false;
            }

            NonDemMaterialBakeCategory category = Classify(material);
            hasAtlasCandidateSubmesh |= category == NonDemMaterialBakeCategory.AtlasCandidate;
            if (category == NonDemMaterialBakeCategory.PreservedCommonMaterial && !policy.PreserveCommonMaterials)
            {
                return false;
            }

            if (category == NonDemMaterialBakeCategory.PreservedVertexColor && !policy.PreserveVertexColorMaterials)
            {
                return false;
            }

            if (category == NonDemMaterialBakeCategory.PreservedTextureless && !policy.PreserveTexturelessMaterials)
            {
                return false;
            }
        }

        return policy.RequireAtlasCandidateMaterial ? hasAtlasCandidateSubmesh : true;
    }

    public static bool TryCreateMaterialBySubmeshIndex(
        ResoniteConstructionCityObject cityObject,
        out Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex)
    {
        materialBySubmeshIndex = [];
        foreach (ResoniteMaterialBinding material in cityObject.Materials)
        {
            foreach (int submeshIndex in material.SubmeshIndices)
            {
                if (!materialBySubmeshIndex.TryAdd(submeshIndex, material))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static NonDemMaterialBakeCategory Classify(ResoniteMaterialBinding material)
    {
        if (IsAtlasBakeCandidate(material))
        {
            return NonDemMaterialBakeCategory.AtlasCandidate;
        }

        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return NonDemMaterialBakeCategory.PreservedVertexColor;
        }

        if (material.AssetScope == ResoniteMaterialAssetScope.Common
            || !string.IsNullOrWhiteSpace(material.Family))
        {
            return CanPreserveAsCommonMaterial(material)
                ? NonDemMaterialBakeCategory.PreservedCommonMaterial
                : NonDemMaterialBakeCategory.PreservedOther;
        }

        if (material.TexturePayload is null)
        {
            return NonDemMaterialBakeCategory.PreservedTextureless;
        }

        return NonDemMaterialBakeCategory.PreservedOther;
    }

    private static bool IsAtlasBakeCandidate(ResoniteMaterialBinding material)
    {
        if (material.DepthOffset is not null
            || material.Projection != ResoniteMaterialProjection.Uv
            || material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            return false;
        }

        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.TexturePayload is null
            || material.TextureSourceKind != ResoniteTextureSourceKind.Dataset)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return true;
        }

        return material.TerrainOverlay is null
            && ResoniteMaterialSharing.CanUseSharedAlbedoOnlyMaterial(material);
    }

    private static bool CanPreserveAsCommonMaterial(ResoniteMaterialBinding material)
    {
        return material.AssetBinding.IsSharedCommon;
    }
}
