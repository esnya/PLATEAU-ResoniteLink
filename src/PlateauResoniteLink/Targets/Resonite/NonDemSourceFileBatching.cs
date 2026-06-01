using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemSourceFileBatching
{
    public static IComparer<NonDemSourceFileBatchKey> KeyComparer { get; } = new SourceFileBatchKeyComparer();

    public static NonDemSourceFileBatchKey CreateKey(
        ResoniteConstructionCityObject cityObject,
        NonDemCityObjectBakePolicy policy,
        NonDemSourceFileScope sourceFileScope)
    {
        string context = policy.Name;

        return new NonDemSourceFileBatchKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName.ToLowerInvariant(),
            cityObject.LodLevel,
            context,
            sourceFileScope);
    }

    public static string CreateBatchSlotKey(NonDemSourceFileBatchKey sourceFileKey, int batchIndex)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake-{Path.GetFileNameWithoutExtension(sourceFileKey.SourceFileScope.RelativePath)}-{sourceFileKey.PackageName}-lod{lodToken}-{batchIndex + 1}");
    }

    public static string CreateBatchDisplayName(NonDemSourceFileBatchKey sourceFileKey, int batchIndex, string batchSlotKey)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceFileKey.PackageName} LOD{lodToken} #{batchIndex + 1} [{batchSlotKey}]");
    }

    public static string CreateAtlasTextureIdentity(NonDemSourceFileBatchKey sourceFileKey, int batchIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlastex-{sourceFileKey.SourceFileScope.RelativePath}-{batchIndex + 1}");
    }

    private sealed class SourceFileBatchKeyComparer : IComparer<NonDemSourceFileBatchKey>
    {
        public int Compare(NonDemSourceFileBatchKey x, NonDemSourceFileBatchKey y)
        {
            int compare = string.CompareOrdinal(x.ActualMeshCode, y.ActualMeshCode);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.PackageName, y.PackageName);
            if (compare != 0)
            {
                return compare;
            }

            compare = Nullable.Compare(x.LodLevel, y.LodLevel);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.PolicyContext, y.PolicyContext);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.SourceFileScope.RelativePath, y.SourceFileScope.RelativePath);
            if (compare != 0)
            {
                return compare;
            }

            return 0;
        }
    }
}
