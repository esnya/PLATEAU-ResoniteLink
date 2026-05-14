using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteCommonMaterialAsset(
    DefaultCommonMaterialMember Member,
    ResoniteMaterialBinding Material,
    CreatedMaterialAsset Asset)
{
    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal readonly record struct ResoniteCommonMaterialPlan(
    DefaultCommonMaterialMember Member,
    ResoniteMaterialBinding Material)
{
    public string SlotName => ResoniteCommonMaterialSlots.GetSlotName(Material);
}

internal static class ResoniteCommonMaterialPlans
{
    public static CommonMaterialCatalog<ResoniteCommonMaterialPlan> CreateCatalogPlans(
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        return commonMaterials.Select(static member =>
        {
            ResoniteMaterialBinding material = SceneImportContractMapper.ToInternal(member.CreateBinding([0]));
            if (material.AssetScope != ResoniteMaterialAssetScope.Common)
            {
                throw new InvalidOperationException(
                    "Common material setup received a non-common material. Use the static common material catalog boundary.");
            }

            return new ResoniteCommonMaterialPlan(member, material);
        });
    }
}

internal static class ResoniteCommonMaterialAssets
{
    public static CommonMaterialCatalog<ResoniteCommonMaterialAsset> Set(
        CommonMaterialCatalog<ResoniteCommonMaterialAsset> assets,
        ResoniteCommonMaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ResoniteCommonMaterialAsset[] updatedAssets = new ResoniteCommonMaterialAsset[
            ContainsMember(assets, asset.Member) ? assets.Count : assets.Count + 1];
        int writeIndex = 0;
        bool replaced = false;
        foreach (ResoniteCommonMaterialAsset existingAsset in assets)
        {
            if (existingAsset.Member == asset.Member)
            {
                updatedAssets[writeIndex++] = asset;
                replaced = true;
            }
            else
            {
                updatedAssets[writeIndex++] = existingAsset;
            }
        }

        if (!replaced)
        {
            updatedAssets[writeIndex] = asset;
        }

        return new CommonMaterialCatalog<ResoniteCommonMaterialAsset>(updatedAssets);
    }

    public static bool TryGetAsset(
        CommonMaterialCatalog<ResoniteCommonMaterialAsset> assets,
        DefaultCommonMaterialMember member,
        out CreatedMaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(member);

        foreach (ResoniteCommonMaterialAsset entry in assets)
        {
            if (entry.Member == member)
            {
                asset = entry.Asset;
                return true;
            }
        }

        asset = default;
        return false;
    }

    private static bool ContainsMember(
        CommonMaterialCatalog<ResoniteCommonMaterialAsset> assets,
        DefaultCommonMaterialMember member)
    {
        foreach (ResoniteCommonMaterialAsset asset in assets)
        {
            if (asset.Member == member)
            {
                return true;
            }
        }

        return false;
    }
}

internal static class ResoniteCommonMaterialSlots
{
    public static string GetSlotName(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.CreateMaterialSlotName(
            material,
            useCommonMaterialAssets: true);
    }
}
