using System;
using System.Collections.Generic;
using System.Linq;

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

internal sealed class ResoniteCommonMaterialAssetAccumulator
{
    private readonly Dictionary<DefaultCommonMaterialMember, ResoniteCommonMaterialAsset> assetsByMember = [];
    private readonly List<DefaultCommonMaterialMember> memberOrder = [];

    public ResoniteCommonMaterialAssetAccumulator()
    {
    }

    public ResoniteCommonMaterialAssetAccumulator(CommonMaterialCatalog<ResoniteCommonMaterialAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        foreach (ResoniteCommonMaterialAsset asset in assets)
        {
            Set(asset);
        }
    }

    public int Count => memberOrder.Count;

    public void Set(ResoniteCommonMaterialAsset asset)
    {
        if (!assetsByMember.ContainsKey(asset.Member))
        {
            memberOrder.Add(asset.Member);
        }

        assetsByMember[asset.Member] = asset;
    }

    public bool TryGetAsset(DefaultCommonMaterialMember member, out CreatedMaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (assetsByMember.TryGetValue(member, out ResoniteCommonMaterialAsset entry))
        {
            asset = entry.Asset;
            return true;
        }

        asset = default;
        return false;
    }

    public CommonMaterialCatalog<ResoniteCommonMaterialAsset> ToCatalog()
    {
        return new CommonMaterialCatalog<ResoniteCommonMaterialAsset>(
            memberOrder.Select(member => assetsByMember[member]).ToArray());
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
