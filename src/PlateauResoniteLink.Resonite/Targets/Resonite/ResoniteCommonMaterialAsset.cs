using System;
using System.Collections.Generic;

using PlateauResoniteLink.Core.Application.Importing.Contracts;


namespace PlateauResoniteLink.Resonite.Targets.Resonite;

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
        return commonMaterials.Map(static member =>
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
    private readonly Dictionary<CommonMaterialDefinition, ResoniteCommonMaterialAsset> assetsByDefinition = new(ReferenceEqualityComparer.Instance);
    private readonly CommonMaterialCatalog<DefaultCommonMaterialMember> members;

    public ResoniteCommonMaterialAssetAccumulator()
        : this(CommonMaterialCatalog.Create())
    {
    }

    public ResoniteCommonMaterialAssetAccumulator(CommonMaterialCatalog<ResoniteCommonMaterialAsset> assets)
        : this(CommonMaterialCatalog.Create())
    {
        ArgumentNullException.ThrowIfNull(assets);
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialAsset> asset in assets.EnumerateMembers())
        {
            if (!IsMissing(asset.Item.Asset))
            {
                assetsByDefinition[asset.Definition] = asset.Item;
            }
        }
    }

    private ResoniteCommonMaterialAssetAccumulator(CommonMaterialCatalog<DefaultCommonMaterialMember> members)
    {
        this.members = members;
    }

    public int Count => assetsByDefinition.Count;

    public void Set(ResoniteCommonMaterialAsset asset)
    {
        assetsByDefinition[asset.Member.Definition] = asset;
    }

    public bool TryGetAsset(DefaultCommonMaterialMember member, out CreatedMaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (assetsByDefinition.TryGetValue(member.Definition, out ResoniteCommonMaterialAsset entry)
            && !IsMissing(entry.Asset))
        {
            asset = entry.Asset;
            return true;
        }

        asset = default;
        return false;
    }

    public CommonMaterialCatalog<ResoniteCommonMaterialAsset> ToCatalog()
    {
        return members.Map(member =>
        {
            if (assetsByDefinition.TryGetValue(member.Definition, out ResoniteCommonMaterialAsset asset))
            {
                return asset;
            }

            return new ResoniteCommonMaterialAsset(
                member,
                SceneImportContractMapper.ToInternal(member.CreateBinding([0])),
                default);
        });
    }

    private static bool IsMissing(CreatedMaterialAsset asset)
    {
        return string.IsNullOrWhiteSpace(asset.MaterialComponent.Value);
    }
}

internal static class ResoniteCommonMaterialSlots
{
    public static string GetSlotName(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.CreateMaterialSlotName(material);
    }
}
