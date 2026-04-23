using System;
using System.Collections.Generic;
namespace PlateauResoniteLink.Application.Importing;

internal static class NonDemBatching
{
    internal static ImportedCityObjectScopeKey ResolveRequiredScopeKey(ImportedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath)
            && string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            throw new InvalidOperationException(
                $"Buffered city object '{cityObject.PackageName}/{cityObject.ObjectKey}' must provide SourceFileRelativePath or SourceUnitKey before non-DEM bake.");
        }

        return new ImportedCityObjectScopeKey(ResolveScopePath(cityObject), cityObject.LodLevel);
    }

    internal static NonDemSourceUnitBatchKey CreateSourceUnitBatchKey(
        ImportedCityObject cityObject,
        NonDemBatchMaterialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(policy);

        return new NonDemSourceUnitBatchKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            policy.Name,
            ResolveScopePath(cityObject));
    }

    internal static bool CanBufferCityObjectMaterials(
        ImportedCityObject cityObject,
        NonDemBatchMaterialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(policy);

        if (!TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, MaterialBinding> materialBySubmeshIndex))
        {
            return false;
        }

        bool hasAtlasCandidateSubmesh = false;
        foreach (MeshSubmesh submesh in cityObject.Mesh.Submeshes)
        {
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out MaterialBinding? material))
            {
                return false;
            }

            NonDemBatchMaterialCategory category = ClassifyMaterial(material);
            hasAtlasCandidateSubmesh |= category == NonDemBatchMaterialCategory.AtlasCandidate;
            if (category == NonDemBatchMaterialCategory.PreservedSharedMaterial && !policy.PreserveSharedMaterials)
            {
                return false;
            }

            if (category == NonDemBatchMaterialCategory.PreservedVertexColor && !policy.PreserveVertexColorMaterials)
            {
                return false;
            }

            if (category == NonDemBatchMaterialCategory.PreservedTextureless && !policy.PreserveTexturelessMaterials)
            {
                return false;
            }
        }

        return policy.RequireAtlasCandidateMaterial ? hasAtlasCandidateSubmesh : true;
    }

    internal static bool TryCreateMaterialBySubmeshIndex(
        ImportedCityObject cityObject,
        out Dictionary<int, MaterialBinding> materialBySubmeshIndex)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        materialBySubmeshIndex = [];
        foreach (MaterialBinding material in cityObject.Materials)
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

    internal static NonDemBatchMaterialCategory ClassifyMaterial(MaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (IsAtlasBakeCandidate(material))
        {
            return NonDemBatchMaterialCategory.AtlasCandidate;
        }

        if (material.MaterialType == MaterialType.VertexColor)
        {
            return NonDemBatchMaterialCategory.PreservedVertexColor;
        }

        if (material.ReuseScope == MaterialReuseScope.Shared
            || !string.IsNullOrWhiteSpace(material.Family))
        {
            return CanPreserveAsSharedMaterial(material)
                ? NonDemBatchMaterialCategory.PreservedSharedMaterial
                : NonDemBatchMaterialCategory.PreservedOther;
        }

        if (material.TexturePayload is null)
        {
            return NonDemBatchMaterialCategory.PreservedTextureless;
        }

        return NonDemBatchMaterialCategory.PreservedOther;
    }

    private static string ResolveScopePath(ImportedCityObject cityObject)
    {
        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            return cityObject.SourceFileRelativePath!;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            return cityObject.SourceUnitKey!;
        }

        throw new InvalidOperationException(
            $"Buffered city object '{cityObject.PackageName}/{cityObject.ObjectKey}' must provide SourceFileRelativePath or SourceUnitKey before non-DEM bake.");
    }

    private static bool IsAtlasBakeCandidate(MaterialBinding material)
    {
        if (material.DepthOffset is not null
            || material.Projection != MaterialProjection.Uv
            || material.ReuseScope == MaterialReuseScope.Shared)
        {
            return false;
        }

        if (material.MaterialType != MaterialType.Standard
            || material.TexturePayload is null
            || material.TextureSourceKind != TextureSourceKind.Dataset)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return true;
        }

        return material.TerrainOverlay is null
            && CanUseSharedAlbedoOnlyMaterial(material);
    }

    private static bool CanPreserveAsSharedMaterial(MaterialBinding material)
    {
        if (material.MaterialType == MaterialType.VertexColor)
        {
            return material.Projection == MaterialProjection.Uv
                && IsWhiteBaseColor(material.BaseColor)
                && material.TexturePayload is null
                && material.TerrainOverlay is null
                && material.TextureScale is null
                && material.TextureOffset is null;
        }

        return !string.IsNullOrWhiteSpace(material.Family)
            || (material.MaterialType == MaterialType.Standard
                && material.Projection == MaterialProjection.Uv
                && material.TextureSourceKind == TextureSourceKind.Dataset
                && material.TexturePayload is not null
                && material.DepthOffset is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && IsWhiteBaseColor(material.BaseColor)
                && material.TerrainOverlay is null);
    }

    private static bool CanUseSharedAlbedoOnlyMaterial(MaterialBinding material)
    {
        return material.MaterialType == MaterialType.Standard
            && material.Projection == MaterialProjection.Uv
            && material.TextureSourceKind == TextureSourceKind.Dataset
            && material.TexturePayload is not null
            && material.DepthOffset is null
            && material.TextureScale is null
            && material.TextureOffset is null
            && IsWhiteBaseColor(material.BaseColor);
    }

    private static bool IsWhiteBaseColor(ColorRgba color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }
}

internal sealed record NonDemBatchMaterialPolicy(
    string Name,
    bool RequireAtlasCandidateMaterial,
    bool PreserveVertexColorMaterials,
    bool PreserveTexturelessMaterials,
    bool PreserveSharedMaterials);

internal readonly record struct ImportedCityObjectScopeKey(
    string CityGmlScopeKey,
    int? LodLevel);

internal readonly record struct NonDemSourceUnitBatchKey(
    string ActualMeshCode,
    string PackageName,
    int? LodLevel,
    string PolicyContext,
    string CityGmlScopeKey);

internal enum NonDemBatchMaterialCategory
{
    AtlasCandidate = 0,
    PreservedSharedMaterial = 1,
    PreservedTextureless = 2,
    PreservedVertexColor = 3,
    PreservedOther = 4,
}
