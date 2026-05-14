using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteCommonMaterialAsset(
    ResoniteMaterialBinding Material,
    CreatedMaterialAsset Asset)
{
    public ResoniteCommonMaterialKey Key => ResoniteCommonMaterialSlots.GetKey(Material);

    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal readonly record struct ResoniteCommonMaterialPlan(
    ResoniteMaterialBinding Material)
{
    public ResoniteCommonMaterialKey Key => ResoniteCommonMaterialSlots.GetKey(Material);

    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal readonly record struct ResoniteCommonMaterialKey(
    string FamilySlotName,
    string MaterialSlotName)
{
    public string SortKey => string.Concat(FamilySlotName, "/", MaterialSlotName);
}

internal sealed class ResoniteCommonMaterialAssetSet
{
    private readonly Dictionary<ResoniteCommonMaterialKey, ResoniteCommonMaterialAsset> assetsByKey = [];

    public int Count => assetsByKey.Count;

    public IEnumerable<ResoniteCommonMaterialAsset> Assets => assetsByKey.Values;

    public void Set(ResoniteCommonMaterialAsset asset)
    {
        assetsByKey[asset.Key] = asset;
    }

    public bool TryGetAsset(ResoniteMaterialBinding material, out CreatedMaterialAsset asset)
    {
        if (assetsByKey.TryGetValue(ResoniteCommonMaterialSlots.GetKey(material), out ResoniteCommonMaterialAsset entry))
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
    public static ResoniteCommonMaterialKey GetKey(ResoniteMaterialBinding material)
    {
        ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);
        return new ResoniteCommonMaterialKey(
            ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(normalizedMaterial),
            GetSlotName(normalizedMaterial));
    }

    public static string GetSlotName(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.CreateMaterialSlotName(
            ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material),
            useCommonMaterialAssets: true);
    }
}
