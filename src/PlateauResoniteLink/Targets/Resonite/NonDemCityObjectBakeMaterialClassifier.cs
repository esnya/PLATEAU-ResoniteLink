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

            NonDemMaterialBakeClassification classification = Classify(material);
            hasAtlasCandidateSubmesh |= classification is NonDemMaterialBakeClassification.AtlasCandidate;
            if (classification is NonDemMaterialBakeClassification.Preserved { Kind: NonDemPreservedMaterialKind.CommonMaterial }
                && !policy.PreserveCommonMaterials)
            {
                return false;
            }

            if (classification is NonDemMaterialBakeClassification.Preserved { Kind: NonDemPreservedMaterialKind.VertexColor }
                && !policy.PreserveVertexColorMaterials)
            {
                return false;
            }

            if (classification is NonDemMaterialBakeClassification.Preserved { Kind: NonDemPreservedMaterialKind.Textureless }
                && !policy.PreserveTexturelessMaterials)
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

    public static NonDemMaterialBakeClassification Classify(ResoniteMaterialBinding material)
    {
        if (NonDemAtlasBakeMaterial.TryCreate(material) is NonDemAtlasBakeMaterial atlasCandidate)
        {
            return new NonDemMaterialBakeClassification.AtlasCandidate(atlasCandidate);
        }

        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return new NonDemMaterialBakeClassification.Preserved(NonDemPreservedMaterialKind.VertexColor);
        }

        if (material.AssetScope == ResoniteMaterialAssetScope.Common
            || !string.IsNullOrWhiteSpace(material.Family))
        {
            return CanPreserveAsCommonMaterial(material)
                ? new NonDemMaterialBakeClassification.Preserved(NonDemPreservedMaterialKind.CommonMaterial)
                : new NonDemMaterialBakeClassification.Preserved(NonDemPreservedMaterialKind.Other);
        }

        if (material.TexturePayload is null)
        {
            return new NonDemMaterialBakeClassification.Preserved(NonDemPreservedMaterialKind.Textureless);
        }

        return new NonDemMaterialBakeClassification.Preserved(NonDemPreservedMaterialKind.Other);
    }

    private static bool CanPreserveAsCommonMaterial(ResoniteMaterialBinding material)
    {
        return material.CommonMaterial is not null;
    }
}
