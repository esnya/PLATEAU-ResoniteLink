using System;
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
    private readonly Dictionary<string, ResoniteCommonMaterialAsset> assetsBySlotName = new(StringComparer.Ordinal);

    public int Count => assetsBySlotName.Count;

    public IEnumerable<ResoniteCommonMaterialAsset> Assets => assetsBySlotName.Values;

    public void Set(ResoniteCommonMaterialAsset asset)
    {
        assetsBySlotName[asset.SlotName] = asset;
    }

    public bool TryGetAsset(ResoniteMaterialBinding material, out CreatedMaterialAsset asset)
    {
        if (assetsBySlotName.TryGetValue(ResoniteCommonMaterialSlots.GetSlotName(material), out ResoniteCommonMaterialAsset entry))
        {
            asset = entry.Asset;
            return true;
        }

        asset = default;
        return false;
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
