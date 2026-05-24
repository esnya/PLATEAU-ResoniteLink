using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemBakeSourceScope
{
    internal static IComparer<NonDemBakeSourceFileKey> OrderComparer { get; } = new SourceFileKeyComparer();

    internal static NonDemBakeSourceFileKey Create(
        ResoniteConstructionCityObject cityObject,
        NonDemCityObjectBakePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(policy);

        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath)
            ? null
            : cityObject.SourceFileRelativePath;
        if (sourceFileRelativePath is null)
        {
            throw new InvalidOperationException(
                $"Non-DEM batch candidate '{cityObject.DisplayName}' did not provide source scope. "
                + "Source-owned batching requires SourceFileRelativePath.");
        }

        return new NonDemBakeSourceFileKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName.ToLowerInvariant(),
            cityObject.LodLevel,
            policy.Name,
            sourceFileRelativePath);
    }

    internal static string CreateBatchSlotKey(NonDemBakeSourceFileKey sourceFileKey, int batchIndex)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake-{Path.GetFileNameWithoutExtension(sourceFileKey.SourceFileRelativePath)}-{sourceFileKey.PackageName}-lod{lodToken}-{batchIndex + 1}");
    }

    internal static string CreateBatchDisplayName(
        NonDemBakeSourceFileKey sourceFileKey,
        int batchIndex,
        string batchSlotKey)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceFileKey.PackageName} LOD{lodToken} #{batchIndex + 1} [{batchSlotKey}]");
    }

    internal static string CreateAtlasTextureIdentity(NonDemBakeSourceFileKey sourceFileKey, int batchIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlastex-{sourceFileKey.SourceFileRelativePath}-{batchIndex + 1}");
    }

    private sealed class SourceFileKeyComparer : IComparer<NonDemBakeSourceFileKey>
    {
        public int Compare(NonDemBakeSourceFileKey x, NonDemBakeSourceFileKey y)
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

            compare = string.CompareOrdinal(x.SourceFileRelativePath, y.SourceFileRelativePath);
            if (compare != 0)
            {
                return compare;
            }

            return 0;
        }
    }
}

internal readonly record struct NonDemBakeSourceFileKey(
    string ActualMeshCode,
    string PackageName,
    int? LodLevel,
    string PolicyContext,
    string SourceFileRelativePath);
