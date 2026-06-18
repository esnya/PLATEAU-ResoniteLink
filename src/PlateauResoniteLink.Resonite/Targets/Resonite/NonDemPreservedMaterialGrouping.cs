using System;
using System.Collections.Generic;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class NonDemPreservedMaterialGrouping
{
    public static IEqualityComparer<NonDemPreservedMaterialGroupingKey> KeyComparer { get; } =
        new PreservedMaterialGroupingKeyComparer();

    public static NonDemPreservedMaterialGroupingKey CreateKey(ResoniteMaterialBinding material)
    {
        ResoniteMaterialBinding normalizedMaterial = NormalizeMaterial(material);
        if (normalizedMaterial.CommonMaterial is not null)
        {
            return new NonDemPreservedMaterialGroupingKey(
                normalizedMaterial.CommonMaterial,
                new ResoniteColor(0.0, 0.0, 0.0, 0.0),
                default,
                normalizedMaterial.TexturePayload,
                normalizedMaterial.TextureSourceKind,
                normalizedMaterial.TerrainOverlay,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                normalizedMaterial.TerrainMeshCode);
        }

        return new NonDemPreservedMaterialGroupingKey(
            null,
            normalizedMaterial.BaseColor,
            normalizedMaterial.MaterialType,
            normalizedMaterial.TexturePayload,
            normalizedMaterial.TextureSourceKind,
            normalizedMaterial.TerrainOverlay,
            normalizedMaterial.Projection,
            normalizedMaterial.DepthOffset,
            normalizedMaterial.TextureScale,
            normalizedMaterial.TextureOffset,
            normalizedMaterial.AssetScope,
            normalizedMaterial.Family,
            normalizedMaterial.BundledVariantIndex,
            normalizedMaterial.TerrainMeshCode);
    }

    public static ResoniteMaterialBinding NormalizeMaterial(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);
    }

    private sealed class PreservedMaterialGroupingKeyComparer :
        IEqualityComparer<NonDemPreservedMaterialGroupingKey>
    {
        public bool Equals(NonDemPreservedMaterialGroupingKey x, NonDemPreservedMaterialGroupingKey y)
        {
            if (!EqualityComparer<DefaultCommonMaterialMember?>.Default.Equals(x.CommonMaterial, y.CommonMaterial))
            {
                return false;
            }

            if (x.CommonMaterial is not null)
            {
                return ReferenceEqualityComparer.Instance.Equals(x.TexturePayload, y.TexturePayload)
                    && x.TextureSourceKind == y.TextureSourceKind
                    && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(x.TerrainOverlay, y.TerrainOverlay)
                    && string.Equals(x.TerrainMeshCode, y.TerrainMeshCode, StringComparison.Ordinal);
            }

            return x.BaseColor == y.BaseColor
                && x.MaterialType == y.MaterialType
                && ReferenceEqualityComparer.Instance.Equals(x.TexturePayload, y.TexturePayload)
                && x.TextureSourceKind == y.TextureSourceKind
                && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(x.TerrainOverlay, y.TerrainOverlay)
                && x.Projection == y.Projection
                && EqualityComparer<ResoniteMaterialDepthOffset?>.Default.Equals(x.DepthOffset, y.DepthOffset)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureScale, y.TextureScale)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureOffset, y.TextureOffset)
                && x.AssetScope == y.AssetScope
                && string.Equals(x.Family, y.Family, StringComparison.Ordinal)
                && x.BundledVariantIndex == y.BundledVariantIndex
                && string.Equals(x.TerrainMeshCode, y.TerrainMeshCode, StringComparison.Ordinal);
        }

        public int GetHashCode(NonDemPreservedMaterialGroupingKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.CommonMaterial);
            if (obj.CommonMaterial is not null)
            {
                AddTexturePayloadHash(ref hash, obj.TexturePayload);
                hash.Add(obj.TextureSourceKind);
                hash.Add(obj.TerrainOverlay);
                hash.Add(obj.TerrainMeshCode, StringComparer.Ordinal);
                return hash.ToHashCode();
            }

            hash.Add(obj.BaseColor);
            hash.Add(obj.MaterialType);
            AddTexturePayloadHash(ref hash, obj.TexturePayload);
            hash.Add(obj.TextureSourceKind);
            hash.Add(obj.TerrainOverlay);
            hash.Add(obj.Projection);
            hash.Add(obj.DepthOffset);
            hash.Add(obj.TextureScale);
            hash.Add(obj.TextureOffset);
            hash.Add(obj.AssetScope);
            hash.Add(obj.Family, StringComparer.Ordinal);
            hash.Add(obj.BundledVariantIndex);
            hash.Add(obj.TerrainMeshCode, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        private static void AddTexturePayloadHash(ref HashCode hash, ResoniteTexturePayload? texturePayload)
        {
            hash.Add(texturePayload is null
                ? 0
                : ReferenceEqualityComparer.Instance.GetHashCode(texturePayload));
        }
    }
}
