using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteCommonMaterialAsset(
    ResoniteMaterialBinding Material,
    CreatedMaterialAsset Asset)
{
    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal readonly record struct ResoniteCommonMaterialPlan(
    ResoniteMaterialBinding Material)
{
    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal sealed class ResoniteCommonMaterialAssetSet
{
    private readonly List<ResoniteCommonMaterialAsset> assets = [];

    public int Count => assets.Count;

    public IEnumerable<ResoniteCommonMaterialAsset> Assets => assets;

    public void Set(ResoniteCommonMaterialAsset asset)
    {
        ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(asset.Material);
        for (int i = 0; i < assets.Count; i++)
        {
            if (CommonMaterialMatches(assets[i].Material, normalizedMaterial))
            {
                assets[i] = asset;
                return;
            }
        }

        assets.Add(asset);
    }

    public bool TryGetAsset(ResoniteMaterialBinding material, out CreatedMaterialAsset asset)
    {
        ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);
        foreach (ResoniteCommonMaterialAsset entry in assets)
        {
            if (CommonMaterialMatches(entry.Material, normalizedMaterial))
            {
                asset = entry.Asset;
                return true;
            }
        }

        asset = default;
        return false;
    }

    private static bool CommonMaterialMatches(
        ResoniteMaterialBinding left,
        ResoniteMaterialBinding right)
    {
        left = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(left);
        right = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(right);
        return left.AssetScope == ResoniteMaterialAssetScope.Common
            && right.AssetScope == ResoniteMaterialAssetScope.Common
            && left.BaseColor == right.BaseColor
            && left.MaterialType == right.MaterialType
            && left.TexturePayload is null
            && right.TexturePayload is null
            && left.TextureSourceKind == right.TextureSourceKind
            && left.Projection == right.Projection
            && left.DepthOffset == right.DepthOffset
            && left.TextureScale == right.TextureScale
            && left.TextureOffset == right.TextureOffset
            && string.Equals(left.Family, right.Family, System.StringComparison.Ordinal)
            && left.BundledVariantIndex == right.BundledVariantIndex
            && left.TerrainOverlay is null
            && right.TerrainOverlay is null
            && string.Equals(left.TerrainMeshCode, right.TerrainMeshCode, System.StringComparison.Ordinal);
    }
}

internal static class ResoniteCommonMaterialSlots
{
    public static string GetSlotName(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.CreateMaterialSlotName(
            ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material),
            useCommonMaterialAssets: true);
    }
}
